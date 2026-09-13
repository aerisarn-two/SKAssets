using Xunit;

namespace SKAssets.Export.Tests
{
    /// <summary>
    /// The two halves of the shipped game, and whether they are to hand.
    /// </summary>
    /// <remarks>
    /// They come from different places and that is not a choice: the meshes are read
    /// out of the archives, while the animation cache is text the game ships inside a
    /// BSA and HKSK reads a folder, so the Havok side has to be extracted first.
    ///
    /// Neither can live in this repository or reach a runner, so a test that needs
    /// them skips. It must **skip** rather than return early: a test that returns is
    /// reported as passing, so a suite run with no game to check against reports the
    /// same count as one run against it, and the number stops meaning anything.
    /// </remarks>
    public static class Corpus
    {
        public const string DataVar = "SKASSETS_SKYRIM_DATA";

        public const string HavokVar = "SKASSETS_HAVOK_MESHES";

        /// <summary>The game's <c>Data</c> folder, or null.</summary>
        public static string? Data => Folder(DataVar);

        /// <summary>An extracted <c>meshes</c> folder holding the animation cache, or null.</summary>
        public static string? Havok => Folder(HavokVar);

        private static string? Folder(string variable)
        {
            string? value = Environment.GetEnvironmentVariable(variable);

            if (string.IsNullOrWhiteSpace(value))
                return null;

            string expanded = value.StartsWith("~/", StringComparison.Ordinal)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[2..])
                : value;

            // A variable pointing somewhere that is not there is worth saying out
            // loud rather than treating as absent: it is a typo, not a choice.
            Assert.True(Directory.Exists(expanded), $"{variable} is not a folder: {expanded}");

            return expanded;
        }
    }

    /// <summary>Skips a test when the game's archives are not to hand.</summary>
    public class MeshCorpusFactAttribute : FactAttribute
    {
        public MeshCorpusFactAttribute()
        {
            if (Corpus.Data is null)
                Skip = $"set {Corpus.DataVar} to the game's Data folder to run this";
        }
    }

    /// <summary>
    /// Skips a test that needs the animation cache as well, which is a separate
    /// extraction.
    /// </summary>
    public sealed class HavokCorpusFactAttribute : FactAttribute
    {
        public HavokCorpusFactAttribute()
        {
            if (Corpus.Data is null || Corpus.Havok is null)
                Skip = $"set {Corpus.DataVar} and {Corpus.HavokVar} to run this";
        }
    }
}
