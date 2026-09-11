namespace SKAssets.Assets
{
    /// <summary>
    /// A media type and category for one kind of asset file.
    /// </summary>
    /// <param name="Extension">
    /// The extension it was recognised by, lowercase and carrying its dot, or the
    /// empty string when the reference had none.
    /// </param>
    /// <param name="Mime">The media type. See the remarks for what is registered.</param>
    /// <param name="Category">What the file is for.</param>
    /// <param name="Description">What the format is, in a few words.</param>
    /// <remarks>
    /// Almost nothing Bethesda ships has a registered media type. Where one exists
    /// the catalogue uses it -- <c>image/png</c>, <c>image/vnd.ms-dds</c>,
    /// <c>application/vnd.adobe.flash.movie</c> -- and where none does it names a
    /// vendor type under <c>vnd.bethesda</c> or <c>vnd.havok</c> rather than
    /// flattening the format to <c>application/octet-stream</c>. Those are ours, not
    /// IANA's: they are stable identifiers to sort and filter on, and they say which
    /// format a path claims to be. They are not a promise about the bytes, which a
    /// sweep of a plugin never reads.
    /// </remarks>
    public sealed record AssetMediaType(
        string Extension,
        string Mime,
        AssetCategory Category,
        string Description)
    {
        /// <summary>
        /// Whether the catalogue recognised the extension.
        /// </summary>
        public bool IsKnown => Category != AssetCategory.Unknown;

        /// <inheritdoc />
        public override string ToString() => Mime;
    }
}
