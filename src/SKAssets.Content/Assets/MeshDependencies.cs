using Mutagen.Bethesda.Assets;
using SKAssets.Content.Havok;
using SKAssets.Content.Nif;

namespace SKAssets.Content.Assets
{
    /// <summary>What a file is to the mesh that needs it.</summary>
    public enum DependencyKind
    {
        /// <summary>The Havok project the mesh hands its animation to.</summary>
        BehaviorProject,

        /// <summary>A file that project declares: a behaviour, a character, a skeleton, an animation.</summary>
        ProjectFile,
    }

    /// <summary>
    /// A file a mesh cannot do without.
    /// </summary>
    /// <param name="Path">Where it is, relative to the data folder.</param>
    /// <param name="Kind">What it is to the mesh.</param>
    /// <param name="Reason">Why it is needed, in a few words.</param>
    public sealed record MeshDependency(DataRelativePath Path, DependencyKind Kind, string Reason);

    /// <summary>
    /// Everything a mesh needs beyond itself.
    /// </summary>
    /// <remarks>
    /// Only what the mesh itself can tell you. A mesh that names a behaviour graph
    /// names the whole project with it, and that resolves here; a skin names bones
    /// and not the skeleton they live in, and nothing in the file says which actor
    /// wears it. That half comes from the record -- the RACE an ARMA points at --
    /// and belongs to the caller, which is the half that has the plugin open.
    /// </remarks>
    public static class MeshDependencies
    {
        /// <summary>
        /// The files behind a mesh's behaviour graph.
        /// </summary>
        /// <param name="profile">The mesh's census.</param>
        /// <param name="projects">
        /// The animation cache, or null to report the graph alone without the files
        /// the project declares.
        /// </param>
        /// <remarks>
        /// A project's file list is written relative to the folder the project file
        /// sits in -- <c>Behaviors\Behavior00.hkx</c> beside
        /// <c>FarmhouseWindMill.hkx</c> -- and it has to be, because
        /// <c>character assets/skeleton.hkx</c> names 188 different files across the
        /// 429 projects. The folder is what tells them apart.
        /// </remarks>
        public static IReadOnlyList<MeshDependency> Of(NifProfile profile, HavokProjectIndex? projects)
        {
            ArgumentNullException.ThrowIfNull(profile);

            if (profile.BehaviorGraph is not { } graph)
                return [];

            var graphPath = AssetPathsForMeshes.UnderMeshes(graph);

            List<MeshDependency> dependencies =
            [
                new MeshDependency(graphPath, DependencyKind.BehaviorProject, "the project the mesh hands its animation to")
            ];

            if (projects?.Resolve(graph) is not { } project)
                return dependencies;

            string folder = FolderOf(graphPath.Path);

            foreach (string file in project.Files)
            {
                string relative = file.Replace('\\', '/').TrimStart('/');

                dependencies.Add(new MeshDependency(
                    new DataRelativePath($"{folder}/{relative}"),
                    DependencyKind.ProjectFile,
                    $"declared by the {project.Name} project"));
            }

            return dependencies;
        }

        private static string FolderOf(string path)
        {
            int slash = path.LastIndexOf('/');

            return slash < 0 ? path : path[..slash];
        }
    }
}
