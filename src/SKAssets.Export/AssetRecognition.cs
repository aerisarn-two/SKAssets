using HKFBX.Codec;
using HKFBX.Hkx;
using HKSK.Model;
using LeanMeshIO;
using NIFSharp;
using SKAssets.Content.Nif;

namespace SKAssets.Export
{
    /// <summary>What a file turned out to be.</summary>
    public enum AssetKind
    {
        /// <summary>Nothing this library reads, or nothing readable at all.</summary>
        Unknown = 0,

        /// <summary>A folder holding a creature: a skeleton, its bodies, its animations.</summary>
        Creature,

        /// <summary>A NIF. What kind of NIF is in the role.</summary>
        Mesh,

        /// <summary>A Havok skeleton packfile: a rig, usually a ragdoll with it.</summary>
        HavokSkeleton,

        /// <summary>A Havok animation packfile.</summary>
        HavokAnimation,

        /// <summary>An FBX. What is in it is in the contents.</summary>
        Scene,

        /// <summary>A folder holding other things.</summary>
        Folder,
    }

    /// <summary>
    /// What a path is, and why we say so.
    /// </summary>
    /// <param name="Path">The file or folder.</param>
    /// <param name="Kind">What it turned out to be.</param>
    /// <param name="Summary">What to tell somebody reading a log.</param>
    /// <param name="Creature">Its files, when it is a creature folder.</param>
    /// <param name="Role">What kind of NIF it is, when it is one.</param>
    /// <param name="Profile">The NIF's shape, when it is one.</param>
    /// <param name="Contents">What the scene holds, when it is an FBX.</param>
    /// <param name="Problem">Why it could not be read, when it could not.</param>
    public sealed record RecognisedAsset(
        string Path,
        AssetKind Kind,
        string Summary,
        CreatureAssets? Creature = null,
        NifRole Role = NifRole.Unknown,
        NifProfile? Profile = null,
        SceneContents? Contents = null,
        string? Problem = null)
    {
        public override string ToString() => $"{System.IO.Path.GetFileName(Path)}: {Summary}";
    }

    /// <summary>
    /// Works out what somebody has handed us.
    /// </summary>
    /// <remarks>
    /// A tool that converts assets has to answer this before it can do anything,
    /// and the answer is not the extension. A <c>.nif</c> may be a rock, a
    /// windmill, a skinned body or an actor's skeleton, and those are converted
    /// differently and belong in different places; a <c>.hkx</c> may be a rig or a
    /// clip, which have nothing in common but their container; an <c>.fbx</c> may
    /// be any combination of all of it.
    ///
    /// So each file is opened and asked. It is cheap -- a NIF's block graph and a
    /// packfile's root container, no geometry and no decompression -- and it is the
    /// difference between a tool that converts what it was given and one that
    /// converts what it assumed.
    /// </remarks>
    public static class AssetRecognition
    {
        /// <summary>What this path is.</summary>
        /// <param name="path">A file or a folder.</param>
        /// <param name="database">The NIF format description.</param>
        /// <param name="cache">
        /// A loaded animation cache, so a creature folder can be matched to its
        /// project. Without one a creature is still a creature and has no clips.
        /// </param>
        public static RecognisedAsset Of(string path, NifXmlDatabase database, SkyrimCache? cache = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(database);

            if (Directory.Exists(path))
                return OfFolder(path, cache);

            if (!File.Exists(path))
                return new RecognisedAsset(path, AssetKind.Unknown, "no such file", Problem: "not found");

            return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".nif" => OfNif(path, database),
                ".hkx" => OfHkx(path),
                ".fbx" => OfFbx(path),
                _ => new RecognisedAsset(path, AssetKind.Unknown, "not a file this converts"),
            };
        }

        /// <summary>A folder: a creature if it holds one, otherwise just a folder.</summary>
        private static RecognisedAsset OfFolder(string path, SkyrimCache? cache)
        {
            if (CreatureExchange.Find(path, cache) is not { } creature)
                return new RecognisedAsset(path, AssetKind.Folder, "a folder");

            string clips = creature.Project is { } project
                ? $"{project.Animations.Count} animations"
                : "no project, so no animations";

            return new RecognisedAsset(
                path,
                AssetKind.Creature,
                $"the creature {creature.Name}: a skeleton, "
                    + $"{creature.Meshes.Count} mesh(es), "
                    + (creature.Rig is null ? "no rig, " : "a rig, ")
                    + clips,
                Creature: creature);
        }

        /// <summary>A NIF, and what kind of NIF.</summary>
        private static RecognisedAsset OfNif(string path, NifXmlDatabase database)
        {
            try
            {
                NifProfile profile = NifProfileReader.Read(NifModel.Load(path, database));
                NifRole role = NifRoles.Of(profile);

                var notes = new List<string>();

                if (profile.IsSkeleton) notes.Add("a ragdoll");

                // Only where the role does not already say it: "a skinned mesh,
                // with skinned" is not a sentence anybody wants in a log.
                if (profile.IsSkinned && role is not (NifRole.SkinnedMesh or NifRole.SkinnedAttachment))
                    notes.Add(profile.HasExternalSkeleton ? "a skin bound elsewhere" : "a skin");

                if (profile.IsDismembered) notes.Add("body part flags");
                if (profile.Collisions > 0) notes.Add($"{profile.Collisions} collision objects");
                if (profile.Constraints > 0) notes.Add($"{profile.Constraints} constraints");

                return new RecognisedAsset(
                    path,
                    AssetKind.Mesh,
                    notes.Count == 0 ? Describe(role) : $"{Describe(role)}, with {string.Join(", ", notes)}",
                    Role: role,
                    Profile: profile);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return new RecognisedAsset(path, AssetKind.Unknown, "a NIF that would not open", Problem: e.Message);
            }
        }

        /// <summary>
        /// A Havok packfile: a skeleton or an animation.
        /// </summary>
        /// <remarks>
        /// Asked in that order because a skeleton is the one that can be identified
        /// without doing any work -- it either holds an <c>hkaSkeleton</c> or it does
        /// not -- and because an animation read is the expensive one.
        /// </remarks>
        private static RecognisedAsset OfHkx(string path)
        {
            try
            {
                HKFBX.Model.SkeletonFile file = HkxSkeletonFile.Read(path);

                return new RecognisedAsset(
                    path,
                    AssetKind.HavokSkeleton,
                    $"a Havok skeleton: {file.Rig.Bones.Count} bones"
                        + (file.Bodies.Count > 0
                            ? $", a ragdoll of {file.Bodies.Count} bodies and {file.Joints.Count} joints"
                            : ", no ragdoll"));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Not a skeleton. The other thing a creature's folder is full of is
                // animations, and those say how long they are without being decoded.
                try
                {
                    (SplineAnimationData spline, _, _) = HkxAnimationFile.ReadAnimation(path);

                    return new RecognisedAsset(
                        path,
                        AssetKind.HavokAnimation,
                        $"a Havok animation: {spline.NumFrames} frames over "
                            + $"{spline.TransformTrackCount} tracks");
                }
                catch (Exception inner) when (inner is not OutOfMemoryException)
                {
                    return new RecognisedAsset(
                        path, AssetKind.Unknown, "a Havok file that is neither a skeleton nor an animation",
                        Problem: e.Message);
                }
            }
        }

        /// <summary>An FBX, and what is in it.</summary>
        private static RecognisedAsset OfFbx(string path)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                SceneContents contents = CreatureExchange.Inspect(FbxDocument.Load(stream));

                return new RecognisedAsset(
                    path, AssetKind.Scene, $"a scene holding {contents}", Contents: contents);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return new RecognisedAsset(path, AssetKind.Unknown, "an FBX that would not open", Problem: e.Message);
            }
        }

        /// <summary>What a role means, in a sentence.</summary>
        public static string Describe(NifRole role) => role switch
        {
            NifRole.StaticGeometry => "a static mesh",
            NifRole.AnimatedMesh => "a mesh that animates itself",
            NifRole.HavokProp => "a prop animated by a Havok project",
            NifRole.ActorSkeleton => "an actor's skeleton",
            NifRole.SkinnedAttachment => "a skinned piece worn on somebody else's skeleton",
            NifRole.SkinnedMesh => "a skinned mesh",
            NifRole.Effect => "an effect",
            NifRole.CameraPath => "a camera path",
            _ => "a NIF of no recognised kind",
        };
    }
}
