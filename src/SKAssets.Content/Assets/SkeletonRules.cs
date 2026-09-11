namespace SKAssets.Content.Assets
{
    /// <summary>
    /// The checks that need two files to make sense of one.
    /// </summary>
    /// <remarks>
    /// A skin names bones and does not carry them; a skeleton mesh carries bones a
    /// Havok rig also declares. Neither file can be judged alone, and both are wrong
    /// in the same way when they disagree -- the mesh loads, and something is in the
    /// wrong place or not weighted at all.
    /// </remarks>
    public static class SkeletonRules
    {
        /// <summary>
        /// Bones a Havok rig declares and no mesh ever carries.
        /// </summary>
        /// <remarks>
        /// Every actor rig in the game declares three of these -- <c>x_NPC
        /// LookNode</c>, <c>x_NPC Translate</c>, <c>x_NPC Rotate</c> -- and no
        /// skeleton mesh contains any of them. They are Havok's own handles, so the
        /// <c>x_</c> prefix is the rule rather than the three names.
        /// </remarks>
        public const string HavokOnlyBonePrefix = "x_";

        /// <summary>
        /// Whether a skin's bones exist in the skeleton it will be mounted on.
        /// </summary>
        /// <remarks>
        /// The check that finds real bugs: 832 of the 835 skinned armour meshes whose
        /// race could be resolved name only bones that race's skeleton carries. Of
        /// the three that do not, one is a Falmer helmet weighted to human bones --
        /// which loads, and hangs in the air.
        /// </remarks>
        public static IReadOnlyList<MeshFinding> CheckSkin(
            IEnumerable<string> skinBones,
            IReadOnlyCollection<string> skeletonNodes)
        {
            ArgumentNullException.ThrowIfNull(skinBones);
            ArgumentNullException.ThrowIfNull(skeletonNodes);

            if (skeletonNodes.Count == 0)
                return [];

            var carried = new HashSet<string>(skeletonNodes, StringComparer.OrdinalIgnoreCase);

            var absent = skinBones
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(bone => !carried.Contains(bone))
                .ToList();

            if (absent.Count == 0)
                return [];

            return
            [
                new MeshFinding(
                    "skin-bone-absent",
                    FindingSeverity.Error,
                    $"the skin is weighted to {absent.Count} bone(s) the skeleton does not carry: {string.Join(", ", absent.Take(6))}")
            ];
        }

        /// <summary>
        /// Whether a skeleton mesh carries the bones its Havok rig animates.
        /// </summary>
        /// <remarks>
        /// An animation moves rig bones by name, so a bone the rig drives and the
        /// mesh lacks is a track that lands nowhere. 199 of the 204 races pass this
        /// once the <c>x_</c> handles are set aside; the five that do not are the
        /// werewolf, whose rig carries arm helpers its mesh does not, and four races
        /// whose skeleton mesh is not the one their project animates.
        ///
        /// The other direction is not a rule. A skeleton mesh carries nodes the rig
        /// never mentions -- weapon and shield mounts, magic nodes, camera
        /// attachments -- a median of four of them, and up to 67.
        /// </remarks>
        public static IReadOnlyList<MeshFinding> CheckRig(
            IEnumerable<string> rigBones,
            IReadOnlyCollection<string> meshNodes)
        {
            ArgumentNullException.ThrowIfNull(rigBones);
            ArgumentNullException.ThrowIfNull(meshNodes);

            var carried = new HashSet<string>(meshNodes, StringComparer.OrdinalIgnoreCase);

            var absent = rigBones
                .Where(bone => !bone.StartsWith(HavokOnlyBonePrefix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(bone => !carried.Contains(bone))
                .ToList();

            if (absent.Count == 0)
                return [];

            return
            [
                new MeshFinding(
                    "rig-bone-absent",
                    FindingSeverity.Warning,
                    $"the Havok rig animates {absent.Count} bone(s) the skeleton mesh does not carry: {string.Join(", ", absent.Take(6))}")
            ];
        }
    }
}
