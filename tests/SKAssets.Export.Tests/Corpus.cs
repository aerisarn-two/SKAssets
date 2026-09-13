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

        /// <summary>
        /// Every folder of the extracted tree holding both of a creature's skeleton
        /// files, or nothing when there is no such tree.
        /// </summary>
        /// <remarks>
        /// The archives are one way to get a creature and an extracted folder is
        /// another, and the second is worth preferring where it exists: the two
        /// halves of a creature live in different archives -- the mesh in
        /// <c>Meshes0</c>, the rig in <c>Animations</c> -- so reading them from
        /// files means the corpus is one folder rather than a game install, and it
        /// keeps working when the drive holding the game is not plugged in.
        ///
        /// Only folders with both. A skeleton.hkx with no mesh beside it is a prop
        /// or a piece of furniture, and the game has far more of those than it has
        /// creatures.
        /// </remarks>
        public static IReadOnlyList<string> ExtractedCreatures()
        {
            if (Havok is not { } root) return [];

            string actors = Path.Combine(root, "actors");

            if (!Directory.Exists(actors)) return [];

            return [.. Directory
                .EnumerateFiles(actors, "skeleton.nif", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .OfType<string>()
                .Where(folder => File.Exists(Path.Combine(folder, "skeleton.hkx")))
                .OrderBy(folder => folder, StringComparer.Ordinal)];
        }

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

    /// <summary>Skips a test when no creature can be read, from either source.</summary>
    public class MeshCorpusFactAttribute : FactAttribute
    {
        public MeshCorpusFactAttribute()
        {
            if (Corpus.ExtractedCreatures().Count == 0 && Corpus.Data is null)
                Skip = $"set {Corpus.HavokVar} to an extracted meshes folder holding "
                    + $"the creatures' skeleton.nif, or {Corpus.DataVar} to the game's Data folder";
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
            if (Corpus.Havok is null)
                Skip = $"set {Corpus.HavokVar} to an extracted meshes folder to run this";
            else if (Corpus.ExtractedCreatures().Count == 0 && Corpus.Data is null)
                Skip = $"the tree at {Corpus.HavokVar} has no creature skeleton.nif; "
                    + $"extract them into it or set {Corpus.DataVar} to the game's Data folder";
        }
    }

    /// <summary>
    /// Skips a test that needs the Havok half and Havok's own codec, but no mesh.
    /// </summary>
    /// <remarks>
    /// A creature's animations live entirely on that side -- the cache, the
    /// project, the packfiles and the rig in skeleton.hkx -- so a clip test needs
    /// no archive and should not ask for one: the extracted folder is a great deal
    /// easier to have than a copy of the game.
    ///
    /// Compressing a clip is Havok's own spline encoder through mopper.exe, a
    /// Win32 binary, so off Windows it also wants Wine. The file being present says
    /// nothing -- Mopper.Native copies it to the output on every platform,
    /// including ones that cannot run it.
    /// </remarks>
    public sealed class ClipCorpusFactAttribute : FactAttribute
    {
        public ClipCorpusFactAttribute()
        {
            if (Corpus.Havok is null) Skip = $"set {Corpus.HavokVar} to an extracted meshes folder to run this";
            else if (!Mopper.Available) Skip = "mopper.exe cannot be run here (not found, or no Wine off Windows)";
        }
    }

    /// <summary>Whether Havok's spline codec can actually be run here.</summary>
    internal static class Mopper
    {
        public static bool Available { get; } = Find() is not null && CanExecute();

        private static string? Find()
        {
            string beside = Path.Combine(AppContext.BaseDirectory, "mopper.exe");
            if (File.Exists(beside)) return beside;

            return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator)
                .Select(folder => Path.Combine(folder, "mopper.exe"))
                .FirstOrDefault(File.Exists);
        }

        // Windows runs the 32-bit binary through WOW64; everything else needs Wine.
        private static bool CanExecute() =>
            OperatingSystem.IsWindows() ||
            (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator)
                .Any(folder => File.Exists(Path.Combine(folder, "wine")));
    }
}
