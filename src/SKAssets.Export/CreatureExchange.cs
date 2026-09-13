using HKFBX.Codec;
using HKFBX.Hkx;
using HKFBX.Model;
using HKSK.Model;
using LeanMeshIO;
using NIFBX.Conversion;
using NIFSharp;
using SKAssets.Export.Fbx;
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
        /// <summary>The file a merged body came out of, relative to the creature.</summary>
        public const string SourceProperty = "sk_source_mesh";

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

                    Mark(body, name);
                    MergeReport result = merge.Add(body);

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

        /// <summary>Says which file a body came out of, before it is folded in.</summary>
        /// <remarks>
        /// Written on the source rather than after the merge, because a merge reuses
        /// nodes the scene already has and says how many rather than which. Marking
        /// first means only the nodes that were actually added keep the mark, which
        /// is the set worth having.
        /// </remarks>
        private static void Mark(FbxDocument document, string name)
        {
            var scene = new FbxScene(document);

            foreach (FbxObject model in scene.OfClass("Model"))
                model.Properties.SetUserString(SourceProperty, name);

            scene.Flush();
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
