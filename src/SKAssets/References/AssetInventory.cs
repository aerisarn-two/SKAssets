using Mutagen.Bethesda.Assets;
using SKAssets.Assets;

namespace SKAssets.References
{
    /// <summary>
    /// A sweep turned round: one entry per file rather than per reference.
    /// </summary>
    /// <remarks>
    /// A sweep of the masters yields sixty thousand listed references to nineteen
    /// thousand distinct files, because a texture used by four hundred records is
    /// reported four hundred times. This is the form to answer "what does this
    /// plugin need" with, and the form a later pass over the archives will be given
    /// to answer "and what of it is actually there".
    /// </remarks>
    public sealed class AssetInventory
    {
        private readonly Dictionary<DataRelativePath, AssetInventoryEntry> _entries;

        private AssetInventory(Dictionary<DataRelativePath, AssetInventoryEntry> entries)
        {
            _entries = entries;
        }

        /// <summary>
        /// Collapse a sweep into one entry per distinct path.
        /// </summary>
        /// <remarks>
        /// Paths compare the way the game reads them, which is without regard to
        /// case: <c>Clutter\Apple.nif</c> and <c>clutter/apple.nif</c> are one file
        /// with two spellings, and both spellings are kept on the entry.
        /// </remarks>
        public static AssetInventory Build(IEnumerable<AssetReference> references)
        {
            ArgumentNullException.ThrowIfNull(references);

            Dictionary<DataRelativePath, AssetInventoryEntry> entries = [];

            foreach (var reference in references)
            {
                if (!entries.TryGetValue(reference.Path, out var entry))
                    entries[reference.Path] = entry = new AssetInventoryEntry(reference.Path, reference.MediaType);

                entry.Add(reference);
            }

            return new AssetInventory(entries);
        }

        /// <summary>Every distinct file, in no particular order.</summary>
        public IReadOnlyCollection<AssetInventoryEntry> Entries => _entries.Values;

        /// <summary>How many distinct files the sweep found.</summary>
        public int Count => _entries.Count;

        /// <summary>The entry for a path, or null if the sweep never saw it.</summary>
        public AssetInventoryEntry? Find(DataRelativePath path) =>
            _entries.GetValueOrDefault(path);

        /// <summary>How many distinct files fall in each category, commonest first.</summary>
        public IReadOnlyList<KeyValuePair<AssetCategory, int>> CountByCategory() =>
            _entries.Values
                .GroupBy(entry => entry.MediaType.Category)
                .Select(group => new KeyValuePair<AssetCategory, int>(group.Key, group.Count()))
                .OrderByDescending(pair => pair.Value)
                .ToList();

        /// <summary>How many distinct files fall under each media type, commonest first.</summary>
        public IReadOnlyList<KeyValuePair<string, int>> CountByMediaType() =>
            _entries.Values
                .GroupBy(entry => entry.MediaType.Mime)
                .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
                .OrderByDescending(pair => pair.Value)
                .ToList();
    }

    /// <summary>
    /// One file, and everything that asked for it.
    /// </summary>
    public sealed class AssetInventoryEntry
    {
        private readonly List<AssetReference> _references = [];
        private readonly HashSet<string> _spellings = new(StringComparer.OrdinalIgnoreCase);

        internal AssetInventoryEntry(DataRelativePath path, AssetMediaType mediaType)
        {
            Path = path;
            MediaType = mediaType;
        }

        /// <summary>The file, normalised.</summary>
        public DataRelativePath Path { get; }

        /// <summary>What the file is.</summary>
        public AssetMediaType MediaType { get; }

        /// <summary>Every reference to it.</summary>
        public IReadOnlyList<AssetReference> References => _references;

        /// <summary>How many records refer to it.</summary>
        public int ReferenceCount => _references.Count;

        /// <summary>
        /// The distinct strings records used to name it, which is more than one when
        /// they disagree about slashes or case.
        /// </summary>
        public IReadOnlyCollection<string> Spellings => _spellings;

        /// <summary>
        /// How the references to it were found. A file that is only ever inferred is
        /// a file no record actually names.
        /// </summary>
        public IEnumerable<AssetReferenceOrigin> Origins =>
            _references.Select(reference => reference.Origin).Distinct();

        internal void Add(AssetReference reference)
        {
            _references.Add(reference);
            _spellings.Add(reference.GivenPath);
        }

        /// <inheritdoc />
        public override string ToString() => $"{Path} ({MediaType.Mime}) x{ReferenceCount}";
    }
}
