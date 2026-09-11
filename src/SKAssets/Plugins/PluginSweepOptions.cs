using Mutagen.Bethesda.Skyrim;

namespace SKAssets.Plugins
{
    /// <summary>
    /// What a sweep looks for.
    /// </summary>
    public sealed record PluginSweepOptions
    {
        /// <summary>
        /// Which Skyrim the plugins are for. Special Edition by default.
        /// </summary>
        public SkyrimRelease Release { get; init; } = SkyrimRelease.SkyrimSE;

        /// <summary>
        /// Include paths derived by convention rather than stated outright --
        /// scripts, above all.
        /// </summary>
        /// <remarks>
        /// On by default, because without it a sweep of Skyrim.esm finds no scripts:
        /// every one of the fifty thousand Papyrus references in the masters is
        /// inferred from a script name. Filter on
        /// <see cref="References.AssetReferenceOrigin"/> afterwards rather than
        /// turning this off, unless the cost matters -- it roughly doubles the work.
        /// </remarks>
        public bool IncludeInferred { get; init; } = true;

        /// <summary>
        /// Include the fields that hold a path without being typed as holding one.
        /// </summary>
        /// <seealso cref="UntypedPathFields"/>
        public bool IncludeUntyped { get; init; } = true;

        /// <summary>
        /// Walk every field of every record looking for strings that look like
        /// paths, instead of only the fields known to hold them.
        /// </summary>
        /// <remarks>
        /// Off by default, and it is not a better sweep -- it is a different job. It
        /// costs about ten times a normal sweep (Skyrim.esm goes from half a minute
        /// to the better part of an hour) and it reports things that are not files:
        /// quest filter folders, and the node names in BODY that happen to be spelled
        /// like a skeleton path. Turn it on to find out whether a plugin puts a
        /// filename somewhere nobody has catalogued yet, then catalogue it.
        /// </remarks>
        public bool DeepScan { get; init; } = false;
    }
}
