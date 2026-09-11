namespace SKAssets.Content.Nif
{
    /// <summary>
    /// What one mesh is made of, as far as saying what it is requires.
    /// </summary>
    /// <remarks>
    /// A census of the block graph, not the graph itself. Everything here is a fact
    /// a reader can establish in one pass, chosen because it separates one kind of
    /// asset from another: whether the file animates, whether it collides, whether
    /// it is a skeleton or rides one, whether it hands its animation to Havok.
    ///
    /// It is a plain record on purpose. The classification and the checks over it
    /// are pure functions of these fields, so they can be tested without a mesh,
    /// and a profile can be built by hand to describe a case the game does not
    /// contain.
    /// </remarks>
    public sealed record NifProfile
    {
        /// <summary>The root block's type: <c>BSFadeNode</c>, <c>NiNode</c>, <c>BSLeafAnimNode</c>.</summary>
        public required string RootType { get; init; }

        /// <summary>The <c>BSXFlags</c> the file carries, or null when it has none.</summary>
        public uint? StoredBsxFlags { get; init; }

        /// <summary>
        /// What <c>BSXFlags</c> should be, calculated from the block graph.
        /// </summary>
        /// <remarks>
        /// NIFBX's calculation, which is ck-cmd's rules measured against the shipped
        /// game. Every bit is derived, so a file whose stored value differs from this
        /// is telling the engine something its own graph does not support.
        /// </remarks>
        public uint CalculatedBsxFlags { get; init; }

        /// <summary>Blocks deriving from <c>bhkCollisionObject</c>.</summary>
        public int Collisions { get; init; }

        /// <summary>Blocks deriving from <c>bhkSPCollisionObject</c> -- phantoms, which are not collisions.</summary>
        public int Phantoms { get; init; }

        /// <summary>Blocks deriving from <c>bhkConstraint</c>.</summary>
        public int Constraints { get; init; }

        /// <summary>
        /// Whether a <c>bhkBlendCollisionObject</c> is present, which is what makes a
        /// file a skeleton -- ragdoll constraints or not.
        /// </summary>
        public bool IsSkeleton { get; init; }

        /// <summary>Whether anything in the file is skinned.</summary>
        public bool IsSkinned { get; init; }

        /// <summary>Whether a skin is a <c>BSDismemberSkinInstance</c>, which carries body part flags.</summary>
        public bool IsDismembered { get; init; }

        /// <summary>
        /// Whether every bone the skin names lives outside this file: a body part
        /// meant to be mounted on somebody else's skeleton.
        /// </summary>
        public bool HasExternalSkeleton { get; init; }

        /// <summary>Geometry blocks -- shapes, of any of the NIF's several kinds.</summary>
        public int Shapes { get; init; }

        /// <summary>Blocks deriving from <c>NiTimeController</c>.</summary>
        public int TimeControllers { get; init; }

        /// <summary>
        /// Blocks deriving from <c>NiControllerManager</c>, which is what a file has
        /// instead of a single controller when it holds named sequences.
        /// </summary>
        public int ControllerManagers { get; init; }

        /// <summary>Named animation sequences, the things a controller manager holds.</summary>
        public int ControllerSequences { get; init; }

        /// <summary>Particle systems.</summary>
        public int ParticleSystems { get; init; }

        /// <summary>
        /// <c>BSValueNode</c> blocks, and nodes named for an addon: the attachment
        /// points another file's ADDN record is dropped into.
        /// </summary>
        public int AddonNodes { get; init; }

        /// <summary>Whether the file carries furniture markers.</summary>
        public bool HasFurnitureMarkers { get; init; }

        /// <summary>Whether an editor marker is present outside a switch branch.</summary>
        public bool HasEditorMarker { get; init; }

        /// <summary>
        /// The Havok graph the file hands its animation to, as the file spells it:
        /// <c>Dungeons\Nordic\Puzzles\PuzzlePillar01.hkx</c>, read relative to
        /// <c>Meshes</c>.
        /// </summary>
        /// <remarks>
        /// From <c>BSBehaviorGraphExtraData</c>. In the shipped game this always
        /// names a <em>prop</em> project's own file -- never an actor's, which a
        /// mesh never names and a RACE record does.
        /// </remarks>
        public string? BehaviorGraph { get; init; }

        /// <summary>
        /// The node in another file this one attaches to, from the <c>Prn</c> string
        /// extra data.
        /// </summary>
        public string? AttachParent { get; init; }

        /// <summary>Every <c>NiNode</c> name in the file, which for a skeleton is its bones.</summary>
        public IReadOnlyList<string> Nodes { get; init; } = [];

        /// <summary>Every distinct bone a skin names.</summary>
        public IReadOnlyList<string> SkinBones { get; init; } = [];

        /// <summary>Whether the stored <c>BSXFlags</c> say what the graph says.</summary>
        public bool BsxFlagsAgree => StoredBsxFlags is null || StoredBsxFlags == CalculatedBsxFlags;
    }
}
