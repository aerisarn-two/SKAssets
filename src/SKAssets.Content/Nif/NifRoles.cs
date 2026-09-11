namespace SKAssets.Content.Nif
{
    /// <summary>
    /// Reading a role off a mesh's census.
    /// </summary>
    public static class NifRoles
    {
        /// <summary>
        /// What this mesh is, from what it contains.
        /// </summary>
        /// <remarks>
        /// The tests overlap, so the order is the answer. It runs from the most
        /// particular kind of file to the most ordinary, and each step is the fact
        /// that most changes how the file has to be handled:
        ///
        /// <list type="number">
        /// <item>a skeleton is a skeleton whatever else it holds;</item>
        /// <item>a skin over bones it does not carry cannot be read without them,
        /// which matters more than what animates it -- 100 armour meshes are both
        /// this and a Havok prop, and the project is still reachable through
        /// <see cref="NifProfile.BehaviorGraph"/>;</item>
        /// <item>a behaviour graph means the animation is not in the file at all;</item>
        /// <item>then geometry decides: none plus particles is an effect, none plus
        /// controllers alone is a camera path;</item>
        /// <item>then what drives it: controllers, an internal skin, or nothing.</item>
        /// </list>
        /// </remarks>
        public static NifRole Of(NifProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);

            if (profile.IsSkeleton)
                return NifRole.ActorSkeleton;

            if (profile.HasExternalSkeleton)
                return NifRole.SkinnedAttachment;

            if (profile.BehaviorGraph is not null)
                return NifRole.HavokProp;

            if (profile.Shapes == 0)
            {
                if (profile.ParticleSystems > 0)
                    return NifRole.Effect;

                if (profile.TimeControllers > 0 || profile.ControllerManagers > 0)
                    return NifRole.CameraPath;

                return NifRole.Unknown;
            }

            if (profile.ParticleSystems > 0)
                return NifRole.Effect;

            if (profile.IsSkinned)
                return NifRole.SkinnedMesh;

            if (profile.ControllerManagers > 0 || profile.TimeControllers > 0)
                return NifRole.AnimatedMesh;

            return NifRole.StaticGeometry;
        }

        /// <summary>
        /// Whether a mesh of this role needs files beyond itself and its textures to
        /// be read, animated or exported correctly.
        /// </summary>
        /// <remarks>
        /// The three that do are the three an exporter gets wrong on its own: a skin
        /// whose bones are in another file, a skeleton whose rig and ragdoll are in a
        /// Havok project, and a prop whose animation is entirely outside the mesh.
        /// </remarks>
        public static bool NeedsCompanionFiles(NifRole role) =>
            role is NifRole.SkinnedAttachment or NifRole.ActorSkeleton or NifRole.HavokProp;
    }
}
