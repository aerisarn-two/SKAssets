using Mutagen.Bethesda.Assets;

namespace SKAssets.Assets
{
    /// <summary>
    /// Turning what a record says into the path an archive can be asked for.
    /// </summary>
    public static class AssetPaths
    {
        /// <summary>
        /// Normalise a path a record holds, putting it under the folder the field is
        /// read relative to.
        /// </summary>
        /// <param name="givenPath">The string as the record holds it.</param>
        /// <param name="baseFolder">
        /// The folder the game reads that field relative to -- <c>Textures</c>,
        /// <c>Meshes</c> -- or null to leave the path where it is.
        /// </param>
        /// <remarks>
        /// Mutagen's own <see cref="DataRelativePath"/> does most of it: backslashes
        /// become forward slashes, a leading separator goes, and a <c>Data\</c>
        /// prefix is dropped. What it does not do is know which folder an untyped
        /// field means, so that is added here -- and only when it is missing, since
        /// the water fields in the masters spell theirs out in full.
        /// </remarks>
        public static DataRelativePath Resolve(string? givenPath, string? baseFolder)
        {
            if (string.IsNullOrWhiteSpace(givenPath))
                return new DataRelativePath(DataRelativePath.NullPath);

            var path = new DataRelativePath(givenPath.Trim());

            if (string.IsNullOrEmpty(baseFolder) || path.IsNull)
                return path;

            if (path.Path.StartsWith(baseFolder + '/', DataRelativePath.PathComparison))
                return path;

            return new DataRelativePath($"{baseFolder}/{path.Path}");
        }
    }
}
