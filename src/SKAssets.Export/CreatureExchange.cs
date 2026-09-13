using HKFBX.Codec;
using HKFBX.Hkx;
using HKFBX.Model;
using HKSK.Model;
using LeanMeshIO;
using NIFBX.Conversion;
using NIFSharp;
using SKAssets.Export.Fbx;
using HavokObject = HKFBX.Fbx.FbxObject;
using HavokScene = HKFBX.Fbx.FbxScene;
using FbxObject = NIFBX.Fbx.FbxObject;
using FbxScene = NIFBX.Fbx.FbxScene;

namespace SKAssets.Export
{
    /// <summary>
    /// Everything one creature is made of, as files on disk.
    /// </summary>
    /// <param name="Name">The creature's folder name, which is what the game calls it.</param>
    /// <param name="Skeleton">Its <c>skeleton.nif</c>: the bone tree, the bodies, the constraints.</param>
    /// <param name="Rig">Its <c>skeleton.hkx</c>, where it has one.</param>
    /// <param name="Meshes">The bodies that ride the skeleton, in the order they were found.</param>
    /// <param name="Project">Its Havok project, where a cache knows about it.</param>
    public sealed record CreatureAssets(
        string Name,
        string Skeleton,
        string? Rig,
        IReadOnlyList<string> Meshes,
        ActorProject? Project)
    {
        public override string ToString() =>
            $"{Name}: {Meshes.Count} mesh(es)"
            + (Rig is null ? ", no rig" : "")
            + (Project is null ? ", no project" : $", {Project.Animations.Count} animations");
    }

    /// <summary>What went into a creature's scene.</summary>
    /// <param name="Name">The creature.</param>
    /// <param name="Bones">Bones in the animation rig, or in the mesh where there is no rig.</param>
    /// <param name="Bodies">Ragdoll bodies.</param>
    /// <param name="Joints">Constraints between them.</param>
    /// <param name="Meshes">The mesh files folded in.</param>
    /// <param name="Unmerged">Meshes that would not load, with the reason.</param>
    /// <param name="Unbound">
    /// Names a merged mesh wanted and the scene did not have. A skin bound to a bone
    /// the skeleton does not carry is the usual cause, and it means that part of the
    /// creature will not follow the rig.
    /// </param>
    /// <param name="Clips">What the animations did, or null where there were none.</param>
    public sealed record CreatureReport(
        string Name,
        int Bones,
        int Bodies,
        int Joints,
        IReadOnlyList<string> Meshes,
        IReadOnlyDictionary<string, string> Unmerged,
        IReadOnlyList<string> Unbound,
        ClipReport? Clips)
    {
        public override string ToString() =>
            $"{Name}: {Bones} bones, {Bodies} bodies, {Joints} joints, {Meshes.Count} meshes, "
            + $"{Clips?.Stacks.Count ?? 0} clips"
            + (Unmerged.Count > 0 ? $", {Unmerged.Count} unreadable" : "")
            + (Unbound.Count > 0 ? $", {Unbound.Count} unbound" : "");
    }

    /// <summary>What a scene turns out to hold.</summary>
    /// <param name="HasMesh">Whether anything in it came out of a NIF.</param>
    /// <param name="HasRig">Whether it carries a Havok rig, and possibly a ragdoll.</param>
    /// <param name="HasClips">Whether it carries animations this library put there.</param>
    /// <param name="Sources">The files it was built from, where it says.</param>
    /// <param name="Clips">How many animation stacks are clips.</param>
    public sealed record SceneContents(
        bool HasMesh, bool HasRig, bool HasClips, IReadOnlyList<string> Sources, int Clips)
    {
        /// <summary>Whether it holds anything this library knows what to do with.</summary>
        public bool IsEmpty => !HasMesh && !HasRig && !HasClips;

        public override string ToString()
        {
            var parts = new List<string>();

            if (HasMesh) parts.Add(Sources.Count > 1 ? $"{Sources.Count} meshes" : "a mesh");
            if (HasRig) parts.Add("a rig");
            if (HasClips) parts.Add($"{Clips} clips");

            return parts.Count == 0 ? "nothing this library recognises" : string.Join(", ", parts);
        }
    }

    /// <summary>What a scene was taken apart into.</summary>
    /// <param name="Skeleton">
    /// The <c>skeleton.nif</c>, where the scene held one. Also in <paramref name="Meshes"/>.
    /// </param>
    /// <param name="Meshes">Every mesh the scene held, by the file it belongs in.</param>
    /// <param name="Havok">The rig and its ragdoll, where the scene carried them.</param>
    /// <param name="Clips">What the animations did, or null where there were none to write.</param>
    /// <param name="Held">What the scene was found to hold, in plain words.</param>
    public sealed record CreatureImport(
        NifModel? Skeleton,
        IReadOnlyDictionary<string, NifModel> Meshes,
        SkeletonFile? Havok,
        ImportReport? Clips,
        IReadOnlyList<string> Held)
    {
        public override string ToString() =>
            Held.Count == 0 ? "nothing this library recognises" : string.Join(", ", Held);
    }

    /// <summary>
    /// A whole creature as one scene: its meshes, its skeleton, its ragdoll and
    /// every animation its project has.
    /// </summary>
    /// <remarks>
    /// The pieces have existed separately for a while — <see cref="SkeletonExchange"/>
    /// for the two skeleton files and <see cref="ClipExchange"/> for the clips — and
    /// putting them together was left to the caller, who also had to know that a
    /// creature's visible body is neither of them. It is a third thing: the chicken
    /// is <c>chicken.nif</c> beside its skeleton, the cow is
    /// <c>highlandcow.nif</c>, and the draugr is forty-six files.
    ///
    /// So this is the whole creature in one call. The skeleton scene is built first,
    /// because it is the one that carries the exact poses, the ragdoll and the
    /// constraints; the bodies are folded into it by
    /// <see cref="Fbx.SceneMerge"/>, which reuses a bone the scene already has
    /// rather than adding a second copy of it — that reuse is what binds a skin to
    /// the skeleton, and it is why the files agreeing about bone names is the join
    /// rather than a nicety. The clips go on last, one stack each.
    ///
    /// <b>This is an export.</b> The scene it makes is for looking at and for
    /// authoring against, and taking it back apart is not the mirror of making it:
    /// the skeleton import would rebuild one <c>skeleton.nif</c> holding every body
    /// that was merged in, which is not how the game keeps them. The bodies carry
    /// <see cref="SourceProperty"/> so a future import can tell them apart; nothing
    /// reads it yet.
    /// </remarks>
    public static class CreatureExchange
    {
        /// <summary>
        /// The files a node belongs to, tab separated, relative to the creature.
        /// </summary>
        /// <remarks>
        /// A list rather than a name because a bone is in more than one file: the
        /// skeleton carries it and every body skinned to it carries its own copy,
        /// and the merge keeps one node for all of them. Writing the creature back
        /// out means knowing which files each node has to appear in.
        /// </remarks>
        public const string SourceProperty = "sk_source_mesh";

        private const char Separator = '\t';

        /// <summary>What a creature's skeleton file is always called.</summary>
        private const string DefaultSkeletonName = "skeleton.nif";

        /// <summary>NIFBX's mark that a node came out of a NIF.</summary>
        private const string NifNodeProperty = "nif_block_type";

        /// <summary>
        /// A creature's files, given the folder its skeleton sits in.
        /// </summary>
        /// <remarks>
        /// Everything beside the skeleton and under it, because that is how the game
        /// keeps a creature: one folder, the skeleton at the top of it, and the
        /// bodies and their variants in and below it. The draugr's DLC armour is in
        /// a subfolder and is as much part of the draugr as the rest.
        /// </remarks>
        /// <param name="folder">The folder holding <c>skeleton.nif</c>.</param>
        /// <param name="cache">
        /// A loaded cache, to find the creature's project and its animations. Without
        /// one the scene is built without clips.
        /// </param>
        /// <returns>The assets, or null where the folder holds no skeleton.</returns>
        public static CreatureAssets? Find(string folder, SkyrimCache? cache = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(folder);

            string skeleton = Path.Combine(folder, "skeleton.nif");

            if (!File.Exists(skeleton))
                return null;

            string rig = Path.Combine(folder, "skeleton.hkx");

            List<string> meshes = [.. Directory
                .EnumerateFiles(folder, "*.nif", SearchOption.AllDirectories)
                .Where(path => !string.Equals(path, skeleton, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

            return new CreatureAssets(
                NameOf(folder), skeleton, File.Exists(rig) ? rig : null, meshes, ProjectFor(folder, cache));
        }

        /// <summary>
        /// The creature, as one scene.
        /// </summary>
        /// <param name="assets">Its files, from <see cref="Find"/> or named directly.</param>
        /// <param name="database">The NIF format description.</param>
        /// <param name="report">What went in, and what would not.</param>
        /// <param name="codec">
        /// What decompresses the clips. Null builds the default, which runs Havok's
        /// own codec through mopper.
        /// </param>
        /// <param name="slots">
        /// Which animations to take. Null takes every one the project has, which is
        /// the point of this and is worth thinking about for the two player projects:
        /// the draugr's 216 clips are 187 MB and the player's 1,656 are about a
        /// gigabyte and a half.
        /// </param>
        public static FbxDocument Export(
            CreatureAssets assets,
            NifXmlDatabase database,
            out CreatureReport report,
            IAnimationCodec? codec = null,
            IEnumerable<AnimationSlot>? slots = null)
        {
            ArgumentNullException.ThrowIfNull(assets);
            ArgumentNullException.ThrowIfNull(database);

            NifModel skeleton = NifModel.Load(assets.Skeleton, database);
            SkeletonFile? rig = assets.Rig is null ? null : HkxSkeletonFile.Read(assets.Rig);

            FbxDocument scene = SkeletonExchange.Export(skeleton, rig);

            // The skeleton is a file like any other, so it says so too. Without that
            // a node with no source would have to mean "the skeleton", and a node
            // shared with a body would stop meaning it the moment the body claimed
            // it.
            Mark(scene, Path.GetFileName(assets.Skeleton), append: false);

            var merged = new List<string>();
            var unmerged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var unbound = new List<string>();

            // One merge for the whole creature, so a bone the first body brings in is
            // the bone the second one binds to.
            var merge = new SceneMerge(scene);
            string root = Path.GetDirectoryName(assets.Skeleton) ?? string.Empty;

            foreach (string path in assets.Meshes)
            {
                string name = Path.GetRelativePath(root, path).Replace('\\', '/');

                try
                {
                    FbxDocument body = new NifToFbx(NifModel.Load(path, database)).Convert();

                    Mark(body, name, append: false);
                    MergeReport result = merge.Add(body);

                    // A bone the body is skinned to is in both files, and the merge
                    // kept the skeleton's copy of it. Saying so is what lets the body
                    // be written back out with the bones it had.
                    Claim(scene, result.ReusedNames, name);

                    merged.Add(name);
                    unbound.AddRange(result.UnboundNames);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // One body that will not load is not a reason to lose the rest of
                    // the creature.
                    unmerged[name] = e.Message;
                }
            }

            ClipReport? clips = assets.Project is { } project && rig is not null
                ? ClipExchange.AddClips(scene, rig.Rig, project, slots, codec)
                : null;

            report = new CreatureReport(
                assets.Name,
                rig?.Rig.Bones.Count ?? 0,
                rig?.Bodies.Count ?? 0,
                rig?.Joints.Count ?? 0,
                merged,
                unmerged,
                [.. unbound.Distinct(StringComparer.OrdinalIgnoreCase)],
                clips);

            return scene;
        }

        /// <summary>
        /// The scene taken apart into the files the game reads.
        /// </summary>
        /// <remarks>
        /// The other direction, and it has to cope with whatever the scene turns out
        /// to hold. An FBX arriving from a DCC tool may be a whole creature, or a rig
        /// on its own, or a rig and a ragdoll with no body, or a body with no
        /// animation, or one clip somebody exported to look at. Each part is written
        /// only where the scene actually carries it: asking for a skeleton.hkx from a
        /// scene that never had one would mean inventing a rig, and writing a
        /// skeleton.nif from a scene of pure animation would mean inventing a mesh.
        ///
        /// The meshes come back as they went in, one file each, because
        /// <see cref="Export"/> recorded which files every node belonged to and a
        /// bone belongs to several. Without that the only honest answer would be one
        /// enormous skeleton.nif holding every body that had been merged into it.
        ///
        /// A scene this library did not build has no such record, and is read as one
        /// mesh — which is what it is.
        ///
        /// <b>Nothing is saved here but the animation packfiles</b>, which HKSK
        /// writes as it converts. The models are returned for the caller to place,
        /// and the cache and the character's animation list are edited in memory:
        /// <c>cache.Save()</c>, and <c>project.SaveCharacter()</c> if a slot was
        /// added.
        /// </remarks>
        /// <param name="document">The scene, however much of a creature it holds.</param>
        /// <param name="database">The NIF format description.</param>
        /// <param name="project">
        /// The creature's Havok project, to write the clips into. Without one the
        /// animations are left in the scene.
        /// </param>
        /// <param name="codec">
        /// What compresses the curves. Null builds the default, which runs Havok's
        /// own codec through mopper.
        /// </param>
        public static CreatureImport Import(
            FbxDocument document,
            NifXmlDatabase database,
            ActorProject? project = null,
            IAnimationCodec? codec = null)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(database);

            SceneContents contents = Inspect(document);

            (bool hasMesh, bool hasRig, bool hasClips, IReadOnlyList<string> sources, _) = contents;
            var held = new List<string>();

            var meshes = new Dictionary<string, NifModel>(StringComparer.OrdinalIgnoreCase);

            if (hasMesh && sources.Count > 1)
            {
                foreach (string source in sources)
                    meshes[source] = Rebuild(document, database, source);

                held.Add($"{sources.Count} meshes");
            }
            else if (hasMesh)
            {
                meshes[sources.Count == 1 ? sources[0] : DefaultSkeletonName] =
                    SkeletonExchange.ImportMesh(document, database);

                held.Add("a mesh");
            }

            SkeletonFile? havok = null;

            if (hasRig)
            {
                havok = SkeletonExchange.ImportHavok(document);
                held.Add(havok.Bodies.Count > 0 ? "a rig and a ragdoll" : "a rig");
            }

            ImportReport? clips = null;

            if (hasClips && project is not null)
            {
                clips = ClipExchange.ImportClips(document, project, codec);
                held.Add($"{clips.Clips.Count} clips");
            }
            else if (hasClips)
            {
                held.Add("clips, with no project to put them in");
            }

            meshes.TryGetValue(DefaultSkeletonName, out NifModel? skeleton);

            return new CreatureImport(skeleton, meshes, havok, clips, held);
        }

        /// <summary>
        /// What a scene holds, without converting any of it.
        /// </summary>
        /// <remarks>
        /// The question a tool has to answer before it can do anything useful with
        /// a file somebody handed it, and the same question <see cref="Import"/>
        /// asks itself. Nothing here is expensive: it is four passes over the
        /// object list and no geometry is touched.
        /// </remarks>
        public static SceneContents Inspect(FbxDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            var scene = new FbxScene(document);
            var sources = new List<string>();

            foreach (FbxObject model in scene.OfClass("Model"))
                foreach (string source in SourcesOf(model))
                    if (!sources.Contains(source, StringComparer.OrdinalIgnoreCase))
                        sources.Add(source);

            bool hasMesh = scene.OfClass("Geometry").Any()
                || scene.OfClass("Model").Any(m => m.Properties.GetString(NifNodeProperty).Length > 0);

            bool hasRig = SkeletonExchange.ReadRigBones(document) is not null
                || scene.OfClass("Model").Any(m =>
                    m.Properties.GetString(HavokBridge.BodyPrefix + "ragdoll_bone").Length > 0);

            int clips = scene.OfClass("AnimationStack")
                .Count(stack => stack.Properties.GetString(ClipExchange.StoredNameProperty).Length > 0);

            return new SceneContents(hasMesh, hasRig, clips > 0, sources, clips);
        }

        /// <summary>
        /// One of the files the scene was built from, rebuilt on its own.
        /// </summary>
        /// <remarks>
        /// A copy with everything belonging to other files taken out of it: the
        /// nodes, whatever hangs beneath them, and the animation stacks, which are
        /// the clips' business and not any mesh's. The bones a body shares with the
        /// skeleton stay, because they belong to both.
        /// </remarks>
        private static NifModel Rebuild(FbxDocument document, NifXmlDatabase database, string source)
        {
            FbxDocument only = Only(document, source);

            // The scene's constraints name their bodies the way Havok does. The copy
            // is this method's own, so the mesh spelling goes back and stays.
            JointBridge.InMeshNames(only);

            return new FbxToNif(new FbxScene(only)).Convert(database);
        }

        /// <summary>A copy of the scene holding only what one file contributed.</summary>
        private static FbxDocument Only(FbxDocument document, string source)
        {
            var buffer = new MemoryStream();
            document.Save(buffer);
            buffer.Position = 0;

            FbxDocument copy = FbxDocument.Load(buffer);
            var scene = new HavokScene(copy);

            var models = scene.OfClass("Model").ToList();
            var keep = new HashSet<long>();

            foreach (HavokObject model in models)
            {
                string[] belongs = model.Properties.GetString(SourceProperty)
                    .Split(Separator, StringSplitOptions.RemoveEmptyEntries);

                if (belongs.Contains(source, StringComparer.OrdinalIgnoreCase))
                    keep.Add(model.Id);
            }

            // And what those nodes hang from, all the way up. A body is skinned to
            // bones in the middle of a skeleton it does not own the top of, and a
            // node whose parent has gone is a node nothing can reach: dropping the
            // ancestors takes the whole tree with them and leaves a mesh bound to
            // nothing.
            foreach (HavokObject model in models.Where(m => keep.Contains(m.Id)).ToList())
            {
                HavokObject? above = model;

                while (above is not null
                    && scene.ParentsOf(above.Id).FirstOrDefault(p => p.Class == "Model") is { } parent)
                {
                    if (!keep.Add(parent.Id)) break;
                    above = parent;
                }
            }

            var doomed = new List<HavokObject>();
            var queue = new Queue<HavokObject>();

            foreach (HavokObject o in scene.Objects.ToList())
            {
                if (o.Class == "AnimationStack"
                    && o.Properties.GetString(ClipExchange.StoredNameProperty).Length > 0)
                {
                    doomed.Add(o);
                    continue;
                }

                if (o.Class != "Model" || keep.Contains(o.Id)) continue;

                doomed.Add(o);
                queue.Enqueue(o);
            }

            // And what hung off those nodes -- a mesh's geometry, its skin, its
            // materials. Not other nodes: a bone kept for this file may sit under one
            // that was not, and dropping it would take half the skeleton with it.
            var seen = new HashSet<long>(doomed.Select(o => o.Id));

            while (queue.Count > 0)
            {
                foreach (HavokObject child in scene.ChildrenOf(queue.Dequeue().Id))
                {
                    if (child.Class == "Model" || !seen.Add(child.Id)) continue;

                    doomed.Add(child);
                    queue.Enqueue(child);
                }
            }

            foreach (HavokObject o in doomed)
                scene.Remove(o);

            scene.Flush();
            return copy;
        }

        /// <summary>Says which file a body came out of, before it is folded in.</summary>
        /// <remarks>
        /// Written on the source rather than after the merge, because a merge reuses
        /// nodes the scene already has and says how many rather than which. Marking
        /// first means only the nodes that were actually added keep the mark, which
        /// is the set worth having.
        /// </remarks>
        private static void Mark(FbxDocument document, string name, bool append)
        {
            var scene = new FbxScene(document);

            foreach (FbxObject model in scene.OfClass("Model"))
                Add(model, name, append);

            scene.Flush();
        }

        /// <summary>Adds a file to the list of files a node belongs to.</summary>
        private static void Add(FbxObject model, string name, bool append)
        {
            if (!append)
            {
                model.Properties.SetUserString(SourceProperty, name);
                return;
            }

            List<string> sources = [.. SourcesOf(model)];

            if (sources.Contains(name, StringComparer.OrdinalIgnoreCase))
                return;

            sources.Add(name);
            model.Properties.SetUserString(SourceProperty, string.Join(Separator, sources));
        }

        /// <summary>Says that the named nodes belong to a file as well.</summary>
        private static void Claim(FbxDocument document, IReadOnlyList<string> names, string name)
        {
            if (names.Count == 0) return;

            var wanted = new HashSet<string>(names, StringComparer.Ordinal);
            var scene = new FbxScene(document);

            foreach (FbxObject model in scene.OfClass("Model"))
                if (wanted.Contains(model.Name))
                    Add(model, name, append: true);

            scene.Flush();
        }

        /// <summary>The files a node belongs to.</summary>
        public static IReadOnlyList<string> SourcesOf(FbxObject model)
        {
            ArgumentNullException.ThrowIfNull(model);

            return model.Properties.GetString(SourceProperty)
                .Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>The creature's name: the folder above <c>character assets</c>.</summary>
        private static string NameOf(string folder)
        {
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));

            if (!name.Equals("character assets", StringComparison.OrdinalIgnoreCase))
                return name;

            string? above = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));

            return above is null ? name : Path.GetFileName(above);
        }

        /// <summary>The project whose rig sits in this folder.</summary>
        /// <remarks>
        /// Matched on the folder rather than on the name: a creature's project is not
        /// always called after it — the hare's is <c>HareProject</c> and its folder is
        /// <c>actors/ambient/hare</c>, but the ambient creatures are not all like that
        /// and the skeleton path is what the cache itself states.
        /// </remarks>
        private static ActorProject? ProjectFor(string folder, SkyrimCache? cache)
        {
            if (cache is null) return null;

            string wanted = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

            foreach (ActorProject project in cache.Actors())
            {
                if (project.SkeletonPath is not { } path) continue;

                string held = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(Path.GetDirectoryName(path) ?? string.Empty));

                if (string.Equals(held, wanted, StringComparison.OrdinalIgnoreCase))
                    return project;
            }

            return null;
        }
    }
}
