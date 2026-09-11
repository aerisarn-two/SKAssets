namespace SKAssets.Plugins
{
    /// <summary>
    /// Makes Mutagen safe to read a localised plugin with on a machine that is not
    /// Windows.
    /// </summary>
    /// <remarks>
    /// A localised plugin holds its text in <c>.strings</c> files rather than in the
    /// record, so parsing one -- a PERK's effects, an ARMO's name -- sends Mutagen
    /// looking for them. That lookup asks where the game's archives are, which asks
    /// for the load order, which asks for <c>Plugins.txt</c>, which lives under
    /// <c>LocalAppData</c>. There is no such variable on Linux, and the null comes
    /// back out as <c>ArgumentNullException: Value cannot be null. (Parameter
    /// 'path1')</c> from somewhere that looks nothing like the cause.
    ///
    /// Pointing the variable at a folder is enough. It does not have to contain a
    /// <c>Plugins.txt</c> -- a missing file is handled, a missing path is not -- and
    /// this sweep never wants the strings anyway: a translated name is not a file
    /// reference. The variable is only ever set when it is unset, so a real one is
    /// left alone, and on Windows this does nothing at all.
    /// </remarks>
    public static class MutagenRuntime
    {
        private static readonly Lock Gate = new();
        private static bool _prepared;

        /// <summary>
        /// Give Mutagen a <c>LocalAppData</c> to find, if it has none. Idempotent,
        /// and called by <see cref="PluginAssetSweeper"/> before it opens anything.
        /// </summary>
        public static void Prepare()
        {
            if (_prepared)
                return;

            lock (Gate)
            {
                if (_prepared)
                    return;

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LocalAppData")))
                {
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                    // GetFolderPath can itself come back empty on a stripped-down
                    // container. The home directory is the last thing that will not.
                    if (string.IsNullOrEmpty(local))
                        local = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                    Environment.SetEnvironmentVariable("LocalAppData", Path.Combine(local, "SKAssets"));
                }

                _prepared = true;
            }
        }
    }
}
