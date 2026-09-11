using SKAssets.Content.Nif;

namespace SKAssets.Content.Tests
{
    /// <summary>
    /// Meshes described rather than read.
    /// </summary>
    /// <remarks>
    /// The classification and every rule over it are functions of the census, so a
    /// profile written by hand exercises them exactly as one read from a file does --
    /// and can describe a case the game does not contain, which a corpus cannot.
    /// </remarks>
    internal static class Profiles
    {
        /// <summary>A mesh with geometry and collision and nothing else: most of the game.</summary>
        internal static NifProfile Static() => new()
        {
            RootType = "BSFadeNode",
            Shapes = 3,
            Collisions = 1,
        };

        internal static NifProfile Skeleton() => new()
        {
            RootType = "NiNode",
            IsSkeleton = true,
            Collisions = 20,
            Constraints = 19,
            Nodes = ["NPC Root [Root]", "NPC COM [COM ]", "NPC Spine [Spn0]"],
        };

        internal static NifProfile Attachment() => new()
        {
            RootType = "NiNode",
            Shapes = 1,
            IsSkinned = true,
            IsDismembered = true,
            HasExternalSkeleton = true,
            SkinBones = ["NPC Spine [Spn0]", "NPC L Hand [LHnd]"],
        };

        internal static NifProfile Prop(string graph = @"Dungeons\Nordic\Puzzles\PuzzlePillar01.hkx") => new()
        {
            RootType = "BSFadeNode",
            Shapes = 2,
            Collisions = 1,
            BehaviorGraph = graph,
        };
    }
}
