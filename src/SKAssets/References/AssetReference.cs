using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Plugins;
using SKAssets.Assets;

namespace SKAssets.References
{
    /// <summary>
    /// One external file, and the record that asked for it.
    /// </summary>
    /// <remarks>
    /// A sweep yields one of these per reference, not per file: a texture used by
    /// four hundred records is four hundred references. <see cref="AssetInventory"/>
    /// is the other way round.
    /// </remarks>
    public sealed record AssetReference
    {
        /// <summary>
        /// The file, relative to the data folder, with forward slashes and its base
        /// folder present: <c>Meshes/Clutter/Apple.nif</c>.
        /// </summary>
        /// <remarks>
        /// This is the form to compare and to look up in an archive. A record says
        /// <c>Clutter\Apple.nif</c> and means that, because the field it sits in
        /// implies <c>Meshes</c>; two records naming the same file with different
        /// slashes, different casing or a <c>Data\</c> prefix all normalise here.
        /// </remarks>
        public required DataRelativePath Path { get; init; }

        /// <summary>
        /// The string as the record holds it, before any of that normalising.
        /// </summary>
        /// <remarks>
        /// Kept because it is what you edit if you are rewriting the record, and
        /// because the difference between it and <see cref="Path"/> is occasionally
        /// the bug -- a stray <c>Data\</c> prefix, an absolute path, a file named
        /// with the wrong base folder.
        /// </remarks>
        public required string GivenPath { get; init; }

        /// <summary>What the file is, by extension and folder.</summary>
        public required AssetMediaType MediaType { get; init; }

        /// <summary>How the reference was found.</summary>
        public required AssetReferenceOrigin Origin { get; init; }

        /// <summary>The plugin the reference was read from.</summary>
        public required ModKey Source { get; init; }

        /// <summary>The record that refers to the file.</summary>
        public required FormKey Record { get; init; }

        /// <summary>The record's type, as the game names it: <c>Static</c>, <c>Race</c>.</summary>
        public required string RecordType { get; init; }

        /// <summary>The record's editor ID, where it has one.</summary>
        public string? EditorId { get; init; }

        /// <summary>
        /// What the plugin format says the field is for -- <c>Model</c>,
        /// <c>Texture</c>, <c>Behavior</c> -- or null when nothing declared it.
        /// </summary>
        /// <remarks>
        /// Not the same claim as <see cref="MediaType"/>, and the two disagree often
        /// enough to be worth keeping apart. A RACE holds its skeleton and its
        /// FaceGen morphs in model fields, so a <c>.hkx</c> and an <c>.egt</c> both
        /// arrive declared <c>Model</c>. The declaration says which field it came
        /// from; the media type says what the file is.
        /// </remarks>
        public string? DeclaredType { get; init; }

        /// <summary>
        /// The field it came from, for references that are not typed asset links.
        /// Null for <see cref="AssetReferenceOrigin.Listed"/> and
        /// <see cref="AssetReferenceOrigin.Inferred"/>, where the asset type says it.
        /// </summary>
        public string? Field { get; init; }

        /// <inheritdoc />
        public override string ToString() => $"{Path} ({MediaType.Mime}) <- {RecordType} {Record}";
    }
}
