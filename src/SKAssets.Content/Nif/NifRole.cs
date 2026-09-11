namespace SKAssets.Content.Nif
{
    /// <summary>
    /// What kind of thing a mesh is.
    /// </summary>
    /// <remarks>
    /// Skyrim's meshes are not one kind of file with optional parts. They fall into
    /// a handful of shapes that are built differently, animate differently, and drag
    /// different companion files behind them -- and a converter that treats them
    /// alike gets the ones that are not static wrong.
    ///
    /// The names here describe the mesh, not the record that names it. The two are
    /// tightly related and not the same: a RACE names a skeleton, but so does a
    /// BPTD; an ARMA usually names a skin and sometimes names a prop.
    /// </remarks>
    public enum NifRole
    {
        /// <summary>Nothing in the file says what it is.</summary>
        Unknown = 0,

        /// <summary>
        /// Geometry, with collision or without. Most of the game: 9,244 of the 9,814
        /// meshes STAT records name.
        /// </summary>
        StaticGeometry,

        /// <summary>
        /// Animation in the file itself, driven by Gamebryo controllers rather than
        /// by Havok. Doors are the clearest case -- 183 of the 222 the game's DOOR
        /// records name hold a controller manager and no behaviour graph.
        /// </summary>
        AnimatedMesh,

        /// <summary>
        /// A mesh that hands its animation to a Havok project: it names a behaviour
        /// graph, and the project beside that graph owns the skeleton, the behaviour
        /// and the clips. Windmills, puzzle pillars, retractable bridges.
        /// </summary>
        /// <remarks>
        /// In the shipped game a mesh's graph always names a <em>prop</em> project --
        /// 1,086 of the 1,102 meshes that name one, with the rest pointing at
        /// Creation Club content absent from the install. An actor's project is never
        /// named by a mesh; a RACE record names it.
        /// </remarks>
        HavokProp,

        /// <summary>
        /// A skeleton: a tree of bones with blend collision objects and, usually,
        /// ragdoll constraints, carrying no geometry of its own. The 52 in the game
        /// are named by RACE and BPTD records, and every one of them has an actor
        /// project behind it.
        /// </summary>
        ActorSkeleton,

        /// <summary>
        /// A skin mounted on a skeleton the file does not contain -- armour, head
        /// parts, anything worn. 3,201 meshes, and the only kind whose bones have to
        /// be checked against something else to know they are right.
        /// </summary>
        SkinnedAttachment,

        /// <summary>
        /// Skinned to its own bones: a tree bending, a book opening, a chain. The
        /// skeleton is in the file, so the mesh is self-contained.
        /// </summary>
        SkinnedMesh,

        /// <summary>
        /// Particles and controllers with little or no geometry. What an ADDN record
        /// names -- 91 of 92 carry no geometry at all -- and what impacts,
        /// explosions and projectiles are largely made of.
        /// </summary>
        Effect,

        /// <summary>
        /// Controllers over nodes, no geometry, no particles: a path for a camera to
        /// travel. All 76 meshes named by CAMS records are this and nothing else.
        /// </summary>
        CameraPath,
    }
}
