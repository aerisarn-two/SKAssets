using System.Globalization;
using HKFBX.Codec;
using HKFBX.Hkx;
using HKFBX.Model;
using HKSK.Model;
using LeanMeshIO;
using NIFBX.Conversion;
using NIFBX.Fbx;
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
        /// <remarks>
        /// The two names are matched however they are spelled. The game ships them
        /// capitalised -- `meshes/Actors/Draugr/Character Assets/Skeleton.nif` -- and
        /// asking the filesystem for `skeleton.nif` finds that on Windows and does
        /// not find it anywhere else. So on Linux a creature extracted with the
        /// game's own names was not a creature at all: `convert` on the draugr's
        /// folder took the four files for four unrelated ones, and wrote `Skeleton.nif`
        /// and `Skeleton.hkx` both out as `Skeleton.fbx`, one over the other.
        /// </remarks>
        public static CreatureAssets? Find(string folder, SkyrimCache? cache = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(folder);

            if (!Directory.Exists(folder))
                return null;

            string? skeleton = Beside(folder, "skeleton.nif");

            if (skeleton is null)
                return null;

            string? rig = Beside(folder, "skeleton.hkx");

            List<string> meshes = [.. Directory
                .EnumerateFiles(folder, "*.nif", SearchOption.AllDirectories)
                .Where(path => !string.Equals(path, skeleton, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

            return new CreatureAssets(NameOf(folder), skeleton, rig, meshes, ProjectFor(folder, cache));
        }

        /// <summary>A file in a folder, found whatever case its name is written in.</summary>
        private static string? Beside(string folder, string name)
        {
            string exact = Path.Combine(folder, name);

            if (File.Exists(exact))
                return exact;

            return Directory.EnumerateFiles(folder)
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase));
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
            IAnimationCodec? codec = null,
            FbxToNifOptions? options = null)
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
                    meshes[source] = Rebuild(document, database, source, options);

                held.Add($"{sources.Count} meshes");
            }
            else if (hasMesh)
            {
                string only = sources.Count == 1 ? sources[0] : DefaultSkeletonName;

                meshes[only] = SkeletonExchange.ImportMesh(document, database, options);

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

            // Counted the way `ImportClips` resolves them, or a scene back from a DCC
            // tool reports no clips and then imports 216 of them. The stack properties
            // do not survive Blender and the manifest on the node does.
            IReadOnlyDictionary<string, ClipExchange.ClipRecord> manifest =
                ClipExchange.Manifest(document);

            int clips = scene.OfClass("AnimationStack").Count(stack => ClipExchange.IsClip(
                stack.Name, stack.Properties.GetString(ClipExchange.StoredNameProperty), manifest));

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
        private static NifModel Rebuild(
            FbxDocument document, NifXmlDatabase database, string source, FbxToNifOptions? options)
        {
            FbxDocument only = Only(document, source);

            // The scene's constraints name their bodies the way Havok does. The copy
            // is this method's own, so the mesh spelling goes back and stays.
            JointBridge.InMeshNames(only);

            return new FbxToNif(new FbxScene(only), options).Convert(database);
        }

        /// <summary>A copy of the scene holding only what one file contributed.</summary>
        private static FbxDocument Only(FbxDocument document, string source)
        {
            var buffer = new MemoryStream();
            document.Save(buffer);
            buffer.Position = 0;

            FbxDocument copy = FbxDocument.Load(buffer);
            var scene = new HavokScene(copy);

            IReadOnlyDictionary<string, ClipExchange.ClipRecord> clips = ClipExchange.Manifest(copy);

            var models = scene.OfClass("Model").ToList();
            var keep = new HashSet<long>();

            foreach (HavokObject model in models)
            {
                // A node the Havok rig has and the mesh does not is the rig's, and
                // goes back into the rig rather than into a NIF. `BoneUnion` puts
                // them in so both files can be rebuilt from one scene -- the three
                // `x_` bones every actor rig declares, the werewolf's five leaked
                // rigging helpers, the joints `JointBridge` invents for a ragdoll
                // the mesh has no constraint for -- and marks each one. The mark was
                // written and never read, so they all landed in the NIF: a
                // werewolf's skeleton came back with 155 nodes for its 147.
                if (model.Properties.GetString(BoneOrigin.Property) == BoneOrigin.Havok)
                    continue;

                string[] belongs = model.Properties.GetString(SourceProperty)
                    .Split(Separator, StringSplitOptions.RemoveEmptyEntries);

                if (belongs.Contains(source, StringComparer.OrdinalIgnoreCase))
                    keep.Add(model.Id);
            }

            // A bone a mesh is skinned to belongs to the skeleton too, and it is kept
            // here without the skeleton it hangs off. The game's own files say how:
            // the bones a skinned mesh names sit flat under that mesh's root, each
            // holding where it stands in the world. `Hair01.nif` ships three nodes --
            // itself, `NPC Neck` and `NPC Head`, siblings, at z=124.72 and z=134.30 --
            // and `DraugrMale02.nif` ships its sixty the same way.
            //
            // Keeping the chain instead was the obvious reading and the wrong one. A
            // node whose parent has gone is unreachable, so the ancestors came too,
            // all the way to the top: hair arrived as nine nodes for its three,
            // carrying `_`, `NPC Root`, the COM and three spine bones it is not
            // skinned to, and the skeleton's own `rigPerspective`, `rigVersion` and
            // `species` rode along on `_`.
            //
            // So the bones are flattened rather than the tree kept. Each one is given
            // the transform that puts it where it was, measured before anything is
            // removed, and ends up under this file's own root -- which is where the
            // game puts it, and leaves nothing above it to carry in.
            var borrowed = new List<HavokObject>();
            var world = new Dictionary<long, NifTransform>();
            var view = new FbxScene(copy);
            long? own = OwnRoot(scene, models, source);

            foreach (HavokObject model in models)
            {
                if (!keep.Contains(model.Id) || Owns(scene, model, own))
                    continue;

                if (view[model.Id] is not { } placed)
                    continue;

                borrowed.Add(model);
                world[model.Id] = FbxGlobalTransform.Of(view, placed);
            }

            foreach (HavokObject model in borrowed)
            {
                if (view[model.Id] is not { } placed)
                    continue;

                Place(placed, world[model.Id]);
                Strip(placed);
            }

            var doomed = new List<HavokObject>();
            var queue = new Queue<HavokObject>();

            foreach (HavokObject o in scene.Objects.ToList())
            {
                if (o.Class == "AnimationStack"
                    && ClipExchange.IsClip(
                        o.Name, o.Properties.GetString(ClipExchange.StoredNameProperty), clips))
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

            if (Reroot(copy, source) is { } root && borrowed.Count > 0)
                Reparent(copy, borrowed.Select(b => b.Id).ToHashSet(), root);

            return copy;
        }

        /// <summary>The topmost node this file states, which is the file's own root.</summary>
        private static long? OwnRoot(
            HavokScene scene, IReadOnlyList<HavokObject> models, string source)
        {
            // Only a node standing at the top of the scene, which is what a file's
            // root is before anything is moved. Every bone a mesh is skinned to
            // claims that mesh and states a nif block too, so asking for the
            // topmost claimant picks a bone out of the middle of the skeleton and
            // calls it the file.
            foreach (HavokObject model in models)
            {
                if (Parent(scene, model) is null
                    && Claims(model, source)
                    && model.Properties.GetString(NifNodeProperty).Length > 0)
                {
                    return model.Id;
                }
            }

            return null;
        }

        /// <summary>Whether a node belongs to this file's own tree rather than being borrowed.</summary>
        /// <remarks>
        /// Which file a node *belongs* to cannot be read off the list of files that
        /// name it: a bone is named by the skeleton that states it and by every mesh
        /// skinned to it, and the list says so without saying which of them it is.
        /// Where it hangs says. A skeleton's bones stand under the skeleton's root,
        /// so rebuilding the skeleton finds them at home and leaves them alone --
        /// down to the `TwistOverride` floats on the arm twist bones, which are the
        /// skeleton's own and went missing when "named by more than one file" was
        /// taken to mean "borrowed".
        /// </remarks>
        private static bool Owns(HavokScene scene, HavokObject model, long? own)
        {
            if (own is not { } root)
                return true;

            for (HavokObject? at = model; at is not null; at = Parent(scene, at))
            {
                if (at.Id == root)
                    return true;
            }

            return false;
        }

        private static HavokObject? Parent(HavokScene scene, HavokObject model) =>
            scene.ParentsOf(model.Id).FirstOrDefault(p => p.Class == "Model");

        /// <summary>Takes the extra data off a bone a mesh has borrowed.</summary>
        /// <remarks>
        /// A bone states things about itself that are the skeleton's business and
        /// no mesh's. The game ships `DraugrMale02.nif` with one extra-data block,
        /// the inventory marker on its own root; the four `TwistOverride` floats on
        /// the arm twist bones live in `Skeleton.nif`, and rode into the body with
        /// the bones the body is skinned to.
        /// </remarks>
        private static void Strip(FbxObject model)
        {
            string count = model.Properties.GetString(FbxExtraDataWriter.CountProperty);

            if (count.Length == 0)
                return;

            foreach (FbxProperty70 property in model.Properties.All.ToList())
            {
                if (property.Name.StartsWith(FbxExtraDataWriter.Prefix, StringComparison.Ordinal))
                    model.Properties.Remove(property.Name);
            }

            model.Properties.Remove(FbxExtraDataWriter.CountProperty);
        }

        /// <summary>Writes a transform onto a node, replacing the one it had.</summary>
        private static void Place(FbxObject model, NifTransform transform)
        {
            NifVector3 angles = transform.ToEulerDegrees();

            model.Properties.SetVector3(
                "Lcl Translation", transform.Translation.X, transform.Translation.Y, transform.Translation.Z);
            model.Properties.SetVector3("Lcl Rotation", angles.X, angles.Y, angles.Z);
            model.Properties.SetVector3("Lcl Scaling", transform.Scale, transform.Scale, transform.Scale);
        }

        /// <summary>
        /// Puts what is left under the file's own root, so that it is that file again.
        /// </summary>
        /// <remarks>
        /// The bones a body is skinned to belong to the skeleton as well, so keeping
        /// them keeps every node above them -- which climbs out of the body and into
        /// the skeleton, whose root is then still standing beside the body's own. Two
        /// nodes at the top of a scene and NIFBX writes a third above both, so a
        /// draugr's `DraugrMale02.nif` came back as a `BSFadeNode` named `Scene`
        /// wrapping the `NiNode` it should have been: 118 blocks against the 101 it
        /// went in as, carrying the skeleton's flags and bounds along with it.
        ///
        /// It hid every other difference too. A comparison walks from the root and
        /// stops where two roots are different block types, so `DraugrMale02.nif` and
        /// `Hair01.nif` each reported a single difference and had never been compared
        /// at all.
        ///
        /// Which node is the file's own root is asked of
        /// <see cref="NifNodeProperty"/> rather than of the shape of the scene, and
        /// that distinction is the whole of it. In a scene NIFBX wrote, a file's nodes
        /// hang under its root and the root is the only one of them at the top. In one
        /// Blender wrote they do not: Blender puts every skinned mesh at the top of the
        /// scene, beside the root rather than under it, so a draugr arrives with eight
        /// nodes at the top and six of them are its body's meshes. Picking the first
        /// that named this file picked a mesh, and treating the rest as foreign deleted
        /// the other five.
        ///
        /// So a node at the top is one of three things: this file's root, another of
        /// this file's nodes, or a node kept only because something under it was
        /// needed. The first two stay -- the second reparented under the first -- and
        /// the third is dropped after what hung beneath it is taken over.
        /// </remarks>
        private static long? Reroot(FbxDocument document, string source)
        {
            var scene = new HavokScene(document);
            List<HavokObject> tops = scene.RootModels().ToList();

            HavokObject? own = tops.FirstOrDefault(
                m => Claims(m, source) && m.Properties.GetString(NifNodeProperty).Length > 0);

            if (own is null || tops.Count < 2)
                return own?.Id;

            var adopted = new List<HavokObject>();

            foreach (HavokObject other in tops.Where(m => m.Id != own.Id))
            {
                if (Claims(other, source))
                {
                    // This file's own, standing at the top because the scene was
                    // written that way. It moves under the root, it is not dropped.
                    adopted.Add(other);
                    continue;
                }

                // Kept for what is under it and nothing else. Its children first,
                // because `Remove` takes every connection that mentions it with it.
                adopted.AddRange(scene.ChildrenOf(other.Id).Where(c => c.Class == "Model"));
                scene.Remove(other);
            }

            if (adopted.Count == 0)
                return own.Id;

            scene.Flush();
            Reparent(document, adopted.Select(a => a.Id).ToHashSet(), own.Id);

            return own.Id;
        }

        /// <summary>Moves nodes under a new parent, rather than giving them a second.</summary>
        /// <remarks>
        /// Written on the document because neither scene view can do it: one can remove
        /// an object and the other can join two, and joining a node that already has a
        /// parent gives it two rather than moving it. A node Blender left at the top of
        /// the scene is connected to the scene root, so joining it to the file's root
        /// left it reachable both ways and NIFBX wrote it out twice -- a draugr's hair
        /// came back with two of its mesh, 442 vertices where it has 221, and 315
        /// influence slots naming a different bone with the weight still on them.
        ///
        /// So every object-to-object connection into these nodes goes, and exactly one
        /// takes its place.
        ///
        /// Every connection to a *node*, that is. A node is the source end of more
        /// than one kind of edge: a skin cluster names the bone it deforms with the
        /// same `OO` shape a parent uses, bone first, so sweeping the lot took the
        /// skin off with the parent. A chicken came back as a shape with no
        /// `NiSkinInstance`, no partition and no data, which is a mesh that deforms
        /// with nothing. Only edges whose far end is another node are parenthood.
        /// </remarks>
        private static void Reparent(FbxDocument document, IReadOnlySet<long> movers, long parent)
        {
            if (document["Connections"] is not { } connections)
                return;

            var nodes = new HavokScene(document).OfClass("Model").Select(m => m.Id).ToHashSet();

            // The scene root, which is what a node Blender left at the top hangs from.
            nodes.Add(0L);

            connections.Nodes.RemoveAll(c =>
                c.Name == "C"
                && c.Properties.Count >= 3
                && (c.Properties[0] as string ?? "OO") == "OO"
                && movers.Contains(Convert.ToInt64(c.Properties[1], CultureInfo.InvariantCulture))
                && nodes.Contains(Convert.ToInt64(c.Properties[2], CultureInfo.InvariantCulture)));

            foreach (long mover in movers)
            {
                var joined = new LeanMeshIO.Formats.Fbx.FbxNode("C");

                joined.Properties.Add("OO");
                joined.Properties.Add(mover);
                joined.Properties.Add(parent);

                connections.Nodes.Add(joined);
            }
        }

        /// <summary>Whether a node says it belongs to the given file.</summary>
        private static bool Claims(HavokObject model, string source) =>
            model.Properties.GetString(SourceProperty)
                .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
                .Contains(source, StringComparer.OrdinalIgnoreCase);

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
