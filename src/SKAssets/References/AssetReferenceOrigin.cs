namespace SKAssets.References
{
    /// <summary>
    /// How a reference was found, which is also how far it can be trusted.
    /// </summary>
    public enum AssetReferenceOrigin
    {
        /// <summary>
        /// A path the record states. The subrecord exists to hold a filename and
        /// this is what it holds -- a model on a STAT, a texture on a TXST, the
        /// behaviour graph on a RACE. Nothing is guessed.
        /// </summary>
        Listed,

        /// <summary>
        /// A path derived from something that is not one. A script entry names
        /// <c>MyScript</c> and the file is <c>Scripts/MyScript.pex</c>; the record
        /// never spells that out, the convention does.
        /// </summary>
        /// <remarks>
        /// Worth separating because the convention can point at files that were
        /// never shipped. Every compiled script implies a <c>.psc</c> beside it in
        /// <c>Scripts/Source</c>, and the game ships almost none of them: a sweep
        /// that treats those as missing assets is reporting on Bethesda's decision
        /// not to ship source, not on a broken plugin.
        /// </remarks>
        Inferred,

        /// <summary>
        /// A path from a field that holds one without being typed as holding one.
        /// The game reads it as a filename; the record schema calls it a string.
        /// </summary>
        /// <seealso cref="SKAssets.Plugins.UntypedPathFields"/>
        Untyped,

        /// <summary>
        /// A string that looks like a path, found by sweeping every field of a
        /// record rather than the ones known to hold one. Discovery, not inventory:
        /// it is how a field nobody has catalogued yet turns up, and it is the only
        /// origin that reports things which are not files at all.
        /// </summary>
        Scanned,
    }
}
