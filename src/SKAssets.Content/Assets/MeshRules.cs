using SKAssets.Content.Nif;

namespace SKAssets.Content.Assets
{
    /// <summary>
    /// What a record requires of the mesh it names.
    /// </summary>
    /// <remarks>
    /// Every rule here was measured against the shipped game before it was written,
    /// and each carries what it scored. That is the difference between a rule and an
    /// opinion: a check that vanilla fails in quantity is describing the checker's
    /// expectations rather than the format, and the ones that survived are the ones
    /// the game itself keeps.
    ///
    /// The counts are over the meshes the five masters and the resource pack name,
    /// 17,670 of them, read out of the archives.
    /// </remarks>
    public static class MeshRules
    {
        /// <summary>
        /// Check a mesh against the record type that names it.
        /// </summary>
        /// <param name="recordType">
        /// The record's type as Mutagen names it: <c>ArmorAddon</c>, <c>HeadPart</c>,
        /// <c>AddonNode</c>, <c>Race</c>. Anything unrecognised is checked only by
        /// the rules that apply to every mesh.
        /// </param>
        /// <param name="profile">The mesh's census.</param>
        public static IReadOnlyList<MeshFinding> Check(string? recordType, NifProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);

            List<MeshFinding> findings = [];

            // True of every mesh: BSXFlags is derived, so a stored value the graph
            // does not support is the file describing itself wrongly to the engine.
            // 12,491 of the 12,509 that carry one agree; the 18 that do not are
            // known outliers, most of them Creation Club content.
            if (!profile.BsxFlagsAgree)
            {
                findings.Add(new MeshFinding(
                    "bsx-flags",
                    FindingSeverity.Warning,
                    $"BSXFlags says 0x{profile.StoredBsxFlags:X} where the block graph says 0x{profile.CalculatedBsxFlags:X}"));
            }

            switch (recordType)
            {
                // 474 of 474 head parts are a skin over a skeleton they do not carry.
                case "HeadPart":
                    if (!profile.IsSkinned)
                        findings.Add(Missing("headpart-skinned", "a head part has to be skinned to the skeleton it is worn on"));
                    else if (!profile.HasExternalSkeleton)
                        findings.Add(Missing("headpart-external-skeleton", "a head part's bones belong to the actor's skeleton, not to the mesh"));
                    break;

                // Every one of the 2,722 skinned armour meshes in the game skins
                // externally. The other 39 an ARMA names are not skinned at all --
                // props and static pieces -- which is why the rule waits for a skin
                // rather than demanding one.
                case "ArmorAddon":
                    if (profile.IsSkinned && !profile.HasExternalSkeleton)
                        findings.Add(Missing("armor-external-skeleton", "an armour mesh skins to the actor's skeleton, not to bones of its own"));
                    break;

                // 52 of the 54 meshes a RACE names are skeletons, and all 43 a BPTD
                // names are. The two exceptions are a draugr body called
                // SkeletonWarrior, which is a skin rather than a rig, and
                // FXEmptyObject, the placeholder eight other record types name too.
                case "Race":
                case "BodyPartData":
                    if (!profile.IsSkeleton)
                        findings.Add(Missing("skeleton-expected", "the mesh has no blend collision object, so the engine will not treat it as a skeleton"));
                    if (profile.Shapes > 0)
                        findings.Add(new MeshFinding("skeleton-carries-geometry", FindingSeverity.Note,
                            $"a skeleton usually carries no geometry, and this has {profile.Shapes} shapes"));
                    break;

                // 91 of 92: an addon is an effect dropped into another mesh, and
                // geometry in one is geometry nothing positions.
                case "AddonNode":
                    if (profile.Shapes > 0)
                        findings.Add(new MeshFinding("addon-carries-geometry", FindingSeverity.Warning,
                            $"an addon node's mesh is an effect, and this carries {profile.Shapes} shapes"));
                    break;

                // 76 of 76.
                case "CameraShot":
                    if (profile.Shapes > 0)
                        findings.Add(new MeshFinding("camera-carries-geometry", FindingSeverity.Warning,
                            "a camera path is nodes and controllers; geometry in one is drawn in front of the camera"));
                    break;
            }

            return findings;
        }

        /// <summary>
        /// Rules that need the file the mesh points at, rather than the mesh alone.
        /// </summary>
        /// <remarks>
        /// A mesh that names a behaviour graph is not complete without it: the
        /// project beside that graph owns the skeleton, the behaviour and every clip.
        /// 1,086 of the 1,102 meshes that name one resolve; the 16 that do not name
        /// Creation Club content this install does not carry, which is what a missing
        /// project usually means.
        /// </remarks>
        public static MeshFinding? CheckBehaviorGraph(NifProfile profile, bool graphExists, bool isRegisteredProject)
        {
            ArgumentNullException.ThrowIfNull(profile);

            if (profile.BehaviorGraph is not { } graph)
                return null;

            if (!graphExists)
                return new MeshFinding("behavior-graph-missing", FindingSeverity.Error,
                    $"the mesh hands its animation to {graph}, which is not there");

            if (!isRegisteredProject)
                return new MeshFinding("behavior-graph-unregistered", FindingSeverity.Warning,
                    $"{graph} exists but the animation cache lists no project by that name, so the game has no clips for it");

            return null;
        }

        private static MeshFinding Missing(string rule, string message) =>
            new(rule, FindingSeverity.Error, message);
    }
}
