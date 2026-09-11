using HKSK.Model;
using Mutagen.Bethesda.Assets;

namespace SKAssets.Content.Havok
{
    /// <summary>Whether a project animates an actor or a prop.</summary>
    public enum ProjectKind
    {
        /// <summary>
        /// A creature or a person: a behaviour graph, a rig, a ragdoll and a cache of
        /// clips with root motion. 49 of them in the game, and a mesh never names one
        /// -- a RACE record does.
        /// </summary>
        Actor,

        /// <summary>
        /// A door, a windmill, a puzzle pillar. A file list and no clip cache, and it
        /// is what a mesh's behaviour graph always points at. 380 of them.
        /// </summary>
        Prop,
    }

    /// <summary>
    /// One Havok project, and the files it is made of.
    /// </summary>
    /// <param name="Name">The project's name, which is also the stem of its own file.</param>
    /// <param name="Kind">Actor or prop.</param>
    /// <param name="Files">
    /// The files the project declares, as it spells them -- relative to the folder
    /// the project file sits in.
    /// </param>
    public sealed record HavokProjectInfo(string Name, ProjectKind Kind, IReadOnlyList<string> Files);

    /// <summary>
    /// The animation cache, indexed by what a mesh can name.
    /// </summary>
    /// <remarks>
    /// A mesh names a Havok graph as a path: <c>Dungeons\Nordic\Puzzles\
    /// PuzzlePillar01.hkx</c>, read relative to <c>Meshes</c>. The project of the
    /// same name owns it, so resolving one to the other is a lookup on the file's
    /// stem -- which holds for 1,086 of the 1,102 meshes in the game that name a
    /// graph.
    ///
    /// The project list has to come from <c>animationdatasinglefile.txt</c>, which is
    /// what HKSK reads. The per-project files under <c>animationdata/</c> are a
    /// pre-DLC snapshot: they list 328 projects where the merged file has 429, and
    /// every Dragonborn prop is missing from them. Building an index from the folder
    /// listing instead reports perfectly good Dragonborn meshes as pointing at
    /// nothing.
    /// </remarks>
    public sealed class HavokProjectIndex
    {
        private readonly Dictionary<string, HavokProjectInfo> _byName;

        private HavokProjectIndex(Dictionary<string, HavokProjectInfo> byName)
        {
            _byName = byName;
        }

        /// <summary>
        /// Index a cache HKSK has already loaded.
        /// </summary>
        public static HavokProjectIndex From(SkyrimCache cache)
        {
            ArgumentNullException.ThrowIfNull(cache);

            Dictionary<string, HavokProjectInfo> byName = new(StringComparer.OrdinalIgnoreCase);

            foreach (CacheProject project in cache.OpenAll())
            {
                // HKSK makes the distinction for us, and it is not a nullable field
                // on one type: an actor carries animations, clips and root motion, a
                // prop carries a file list and nothing else. 49 against 380.
                byName[project.Name] = new HavokProjectInfo(
                    project.Name,
                    project is ActorProject ? ProjectKind.Actor : ProjectKind.Prop,
                    project.Files);
            }

            return new HavokProjectIndex(byName);
        }

        /// <summary>
        /// Index a list of projects that came from somewhere else.
        /// </summary>
        /// <remarks>
        /// The cache is not the only way to learn what projects exist -- a caller
        /// with its own index, or a test with three projects and no game, has the
        /// same question to answer.
        /// </remarks>
        public static HavokProjectIndex From(IEnumerable<HavokProjectInfo> projects)
        {
            ArgumentNullException.ThrowIfNull(projects);

            Dictionary<string, HavokProjectInfo> byName = new(StringComparer.OrdinalIgnoreCase);

            foreach (var project in projects)
                byName[project.Name] = project;

            return new HavokProjectIndex(byName);
        }

        /// <summary>
        /// Index the cache under a data folder's <c>Meshes</c>.
        /// </summary>
        /// <remarks>
        /// The cache is loose text and Havok files, so this wants a folder rather
        /// than an archive. Where the game ships them inside a BSA they have to be
        /// extracted first, which is the caller's business -- this library never
        /// writes anything.
        /// </remarks>
        public static HavokProjectIndex Load(string meshesFolder) =>
            From(SkyrimCache.Load(meshesFolder));

        /// <summary>Every project the cache lists.</summary>
        public IReadOnlyCollection<HavokProjectInfo> Projects => _byName.Values;

        /// <summary>
        /// The project a mesh's behaviour graph belongs to, or null if the cache
        /// lists none by that name.
        /// </summary>
        /// <param name="behaviorGraph">
        /// The path as the mesh spells it, with or without its folders.
        /// </param>
        public HavokProjectInfo? Resolve(string? behaviorGraph)
        {
            if (string.IsNullOrWhiteSpace(behaviorGraph))
                return null;

            string stem = Path.GetFileNameWithoutExtension(behaviorGraph.Replace('\\', '/'));

            return _byName.GetValueOrDefault(stem);
        }

        /// <summary>
        /// Where a mesh's behaviour graph sits under the data folder.
        /// </summary>
        /// <remarks>
        /// A mesh spells its graph relative to <c>Meshes</c>, the same way a record
        /// spells a model, so the data-relative path is the one with that folder put
        /// back on the front.
        /// </remarks>
        public static DataRelativePath GraphPath(string behaviorGraph) =>
            Assets.AssetPathsForMeshes.UnderMeshes(behaviorGraph);
    }
}
