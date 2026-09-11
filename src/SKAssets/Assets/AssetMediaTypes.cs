using System.Collections.Frozen;

namespace SKAssets.Assets
{
    /// <summary>
    /// What each extension the game loads means: its media type and its category.
    /// </summary>
    /// <remarks>
    /// The table is deliberately wider than a plugin sweep needs. A plugin only ever
    /// names a handful of extensions -- across every vanilla master it is
    /// <c>.nif</c>, <c>.dds</c>, <c>.wav</c>, <c>.hkx</c>, <c>.tri</c>, <c>.egt</c>,
    /// plus the <c>.pex</c>, <c>.psc</c>, <c>.seq</c> and <c>.png</c> that come from
    /// inferred links -- but the same catalogue has to classify what a BSA holds,
    /// which is everything.
    /// </remarks>
    public static class AssetMediaTypes
    {
        /// <summary>
        /// What an unrecognised file is taken to be.
        /// </summary>
        public static readonly AssetMediaType Unknown =
            new("", "application/octet-stream", AssetCategory.Unknown, "Unrecognised");

        private static readonly FrozenDictionary<string, AssetMediaType> ByExtension = new[]
        {
            // Geometry. The LOD forms are NIFs too -- same format, different job --
            // so they carry the NIF media type and are told apart by extension.
            Entry(".nif", "application/vnd.bethesda.nif", AssetCategory.Model, "NetImmerse/Gamebryo model"),
            Entry(".btr", "application/vnd.bethesda.nif", AssetCategory.Model, "Terrain LOD model"),
            Entry(".bto", "application/vnd.bethesda.nif", AssetCategory.Model, "Object LOD model"),
            Entry(".tri", "application/vnd.bethesda.tri", AssetCategory.Model, "Morph and deformation data"),
            Entry(".egm", "application/vnd.bethesda.egm", AssetCategory.Model, "FaceGen geometry morphs"),
            Entry(".egt", "application/vnd.bethesda.egt", AssetCategory.Model, "FaceGen texture morphs"),

            Entry(".dds", "image/vnd.ms-dds", AssetCategory.Texture, "DirectDraw surface"),
            Entry(".png", "image/png", AssetCategory.Texture, "PNG image"),
            Entry(".tga", "image/x-tga", AssetCategory.Texture, "Targa image"),
            Entry(".jpg", "image/jpeg", AssetCategory.Texture, "JPEG image"),
            Entry(".jpeg", "image/jpeg", AssetCategory.Texture, "JPEG image"),
            Entry(".bmp", "image/bmp", AssetCategory.Texture, "Bitmap image"),

            // One container for skeletons, behaviour graphs, animations and ragdolls
            // alike. Which of those a file holds is inside it, not in its name.
            Entry(".hkx", "application/vnd.havok.hkx", AssetCategory.Animation, "Havok behaviour, skeleton or animation"),

            Entry(".wav", "audio/wav", AssetCategory.Sound, "Waveform audio"),
            Entry(".xwm", "audio/vnd.bethesda.xwm", AssetCategory.Sound, "xWMA audio"),
            Entry(".mp3", "audio/mpeg", AssetCategory.Sound, "MPEG audio"),

            // Voice regardless of where it sits: a fuz is an xWMA line with its lip
            // sync stapled to the front, and neither is used for anything else.
            Entry(".fuz", "audio/vnd.bethesda.fuz", AssetCategory.Voice, "Fuz voice, lip sync and audio"),
            Entry(".lip", "application/vnd.bethesda.lip", AssetCategory.Voice, "Lip sync"),

            Entry(".pex", "application/vnd.bethesda.papyrus", AssetCategory.Script, "Compiled Papyrus script"),
            Entry(".psc", "text/vnd.bethesda.papyrus", AssetCategory.Script, "Papyrus source"),

            Entry(".swf", "application/vnd.adobe.flash.movie", AssetCategory.Interface, "Scaleform movie"),
            Entry(".gfx", "application/vnd.scaleform.gfx", AssetCategory.Interface, "Scaleform movie"),

            Entry(".strings", "application/vnd.bethesda.strings", AssetCategory.Translation, "Localised strings"),
            Entry(".dlstrings", "application/vnd.bethesda.strings", AssetCategory.Translation, "Localised strings, long"),
            Entry(".ilstrings", "application/vnd.bethesda.strings", AssetCategory.Translation, "Localised strings, indexed"),

            Entry(".bik", "video/vnd.radgamettools.bink", AssetCategory.Video, "Bink video"),
            Entry(".fxp", "application/vnd.bethesda.shader-package", AssetCategory.Shader, "Compiled shader package"),

            Entry(".txt", "text/plain", AssetCategory.Text, "Plain text"),
            Entry(".ini", "text/plain", AssetCategory.Text, "Configuration"),
            Entry(".csv", "text/csv", AssetCategory.Text, "Comma-separated values"),
            Entry(".xml", "application/xml", AssetCategory.Text, "XML"),
            Entry(".json", "application/json", AssetCategory.Text, "JSON"),
            Entry(".html", "text/html", AssetCategory.Text, "HTML"),
            Entry(".htm", "text/html", AssetCategory.Text, "HTML"),

            Entry(".seq", "application/vnd.bethesda.seq", AssetCategory.Data, "Quest start-up list"),
            Entry(".lod", "application/vnd.bethesda.lod", AssetCategory.Data, "LOD settings"),

            Entry(".bsa", "application/vnd.bethesda.bsa", AssetCategory.Archive, "Bethesda archive"),

            Entry(".esm", "application/vnd.bethesda.plugin", AssetCategory.Plugin, "Master plugin"),
            Entry(".esp", "application/vnd.bethesda.plugin", AssetCategory.Plugin, "Plugin"),
            Entry(".esl", "application/vnd.bethesda.plugin", AssetCategory.Plugin, "Light plugin"),
        }.ToFrozenDictionary(x => x.Extension, StringComparer.OrdinalIgnoreCase);

        private static AssetMediaType Entry(string extension, string mime, AssetCategory category, string description) =>
            new(extension, mime, category, description);

        /// <summary>
        /// Every extension the catalogue knows.
        /// </summary>
        public static IReadOnlyCollection<AssetMediaType> Known => ByExtension.Values;

        /// <summary>
        /// The media type for an extension, with or without its dot.
        /// </summary>
        /// <remarks>
        /// An extension that is not in the table still comes back carrying itself, so
        /// a caller grouping a sweep by media type sees <c>.foo</c> and <c>.bar</c>
        /// as two unknowns rather than one.
        /// </remarks>
        public static AssetMediaType ForExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return Unknown;

            string normalised = extension[0] == '.' ? extension : "." + extension;
            normalised = normalised.ToLowerInvariant();

            return ByExtension.TryGetValue(normalised, out var type)
                ? type
                : Unknown with { Extension = normalised };
        }

        /// <summary>
        /// The media type for a path, refined by the folder it sits in.
        /// </summary>
        /// <remarks>
        /// Audio is the reason this exists. A <c>.wav</c> is a sound effect, a line
        /// of dialogue or a music track depending only on where it lives, and the
        /// three are not interchangeable to anyone reading a sweep. The extension
        /// settles the format; <c>Sound/Voice</c> and <c>Music</c> settle the job.
        /// </remarks>
        public static AssetMediaType ForPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Unknown;

            var type = ForExtension(Path.GetExtension(path));

            if (type.Category is not (AssetCategory.Sound or AssetCategory.Music))
                return type;

            string folders = path.Replace('\\', '/');

            if (folders.Contains("sound/voice/", StringComparison.OrdinalIgnoreCase)
                || folders.StartsWith("voice/", StringComparison.OrdinalIgnoreCase))
                return type with { Category = AssetCategory.Voice };

            if (folders.StartsWith("music/", StringComparison.OrdinalIgnoreCase)
                || folders.Contains("/music/", StringComparison.OrdinalIgnoreCase))
                return type with { Category = AssetCategory.Music };

            return type;
        }
    }
}
