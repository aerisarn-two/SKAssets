using Mutagen.Bethesda.Assets;
using SKAssets.Assets;

namespace SKAssets.Content.Assets
{
    /// <summary>
    /// Paths a mesh names, which are relative to <c>Meshes</c> like a record's are.
    /// </summary>
    internal static class AssetPathsForMeshes
    {
        private const string MeshesFolder = "Meshes";

        /// <summary>
        /// A path a mesh holds, as a path under the data folder.
        /// </summary>
        internal static DataRelativePath UnderMeshes(string? given) =>
            AssetPaths.Resolve(given, MeshesFolder);
    }
}
