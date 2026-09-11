using System.Text;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SKAssets.Plugins
{
    /// <summary>
    /// The fields that hold a filename while the record schema calls them strings.
    /// </summary>
    /// <remarks>
    /// Mutagen types most path fields as asset links, and those are what a sweep
    /// reads first. A few are not, and they are invisible to anything that only
    /// follows links. Two of them are used by the vanilla masters -- a cell's water
    /// cubemap, ninety-four times, and a water record's noise maps, ninety-three --
    /// and both name a texture with a <c>Data\</c> prefix the link fields never
    /// carry.
    ///
    /// Two of them are not strings at all. Where a field is legacy -- superseded by
    /// another and left undefined -- Mutagen hands back the subrecord's raw bytes
    /// rather than parsing them, and a path sitting in one is invisible to anything
    /// that reads properties. UESP documents both as zstrings, and the masters fill
    /// both: 161 sound markers still carry the filename the sound descriptor
    /// replaced, and one weather record its cloud layers in the pre-Skyrim form.
    ///
    /// The list comes from reading every plain string field in the Skyrim record
    /// schema, every raw subrecord that decodes to something path-shaped, and
    /// UESP's field tables for the records those two passes did not reach, then
    /// keeping the ones that name a file. The ones deliberately left out
    /// are worth saying, because they look like candidates and are not:
    ///
    /// <list type="bullet">
    /// <item><c>BodyPartData.Parts.PartNode</c>, <c>IkStartNode</c>,
    /// <c>VatsTarget</c> and <c>GoreTargetBone</c> are node names inside a skeleton.
    /// Some are spelled <c>BASE Meshes\Actors\...\skeleton.nif</c>, which is a node
    /// named after the file it belongs to, not a reference to it.</item>
    /// <item><c>Quest.Filter</c> is the folder the Creation Kit files the quest
    /// under. It is a path with no file at the end of it.</item>
    /// <item><c>IdleAnimation.AnimationEvent</c>, <c>Npc.Attacks.AttackEvent</c> and
    /// <c>Race.MovementTypeNames</c> name events inside a behaviour graph, which is
    /// how they reach an animation. The graph is the reference; these are not.</item>
    /// <item>Two in five of the legacy sound values name a folder rather than a
    /// file -- the marker plays whatever is inside it -- so only the ones carrying
    /// an extension are reported.</item>
    /// <item>Script and fragment names are covered already, as inferred links.</item>
    /// </list>
    ///
    /// Thirty-four files in the masters are named by one of these fields and by
    /// nothing else: twenty-one sounds and thirteen water cubemaps. That is what a
    /// sweep misses without them.
    /// </remarks>
    public static class UntypedPathFields
    {
        /// <summary>
        /// One field that holds a path without being typed as one.
        /// </summary>
        /// <param name="RecordType">The record it is on.</param>
        /// <param name="Field">Its name, as the schema spells it.</param>
        /// <param name="BaseFolder">
        /// The folder the game reads it relative to, supplied when the value does not
        /// already start with it.
        /// </param>
        /// <param name="Note">Why it is in the list.</param>
        public sealed record Entry(string RecordType, string Field, string BaseFolder, string Note);

        /// <summary>
        /// Every field the sweep reads directly. Documentation as much as data: the
        /// sweep itself reads them through typed getters, a few lines below.
        /// </summary>
        public static IReadOnlyList<Entry> All { get; } =
        [
            new("Cell", "WaterEnvironmentMap", "Textures", "Cubemap reflected in the cell's water. Used by the masters."),
            new("Cell", "WaterNoiseTexture", "Textures", "Noise map for the cell's water. Empty throughout the masters."),
            new("Water", "UnusedNoisemaps", "Textures", "Noise maps on a water type. Named unused, filled anyway."),
            new("BodyPartData", "Parts.LimbReplacementModel", "Meshes", "Model swapped in when the limb comes off."),
            new("Class", "Icon", "Textures", "Class icon, from before Skyrim dropped them. Empty in the masters."),
            new("Faction", "Ranks.Insignia", "Textures", "Rank insignia, likewise. Empty in the masters."),
            new("SoundMarker", "FNAM", "Sound", "Legacy sound filename, superseded by the sound descriptor. 161 in the masters."),
            new("Weather", "DNAM", "Textures", "Cloud texture layer 0, in the form that predates the texture fields."),
            new("Weather", "CNAM", "Textures", "Cloud texture layer 1, likewise."),
            new("Weather", "ANAM", "Textures", "Cloud texture layer 2, likewise."),
            new("Weather", "BNAM", "Textures", "Cloud texture layer 3, likewise."),
        ];

        /// <summary>
        /// The paths one record holds in those fields.
        /// </summary>
        internal static IEnumerable<(string Field, string BaseFolder, string Value)> Extract(IMajorRecordGetter record)
        {
            switch (record)
            {
                case ICellGetter cell:
                    if (Filled(cell.WaterEnvironmentMap))
                        yield return ("WaterEnvironmentMap", "Textures", cell.WaterEnvironmentMap!);
                    if (Filled(cell.WaterNoiseTexture))
                        yield return ("WaterNoiseTexture", "Textures", cell.WaterNoiseTexture!);
                    break;

                case IWaterGetter water:
                    foreach (string noisemap in water.UnusedNoisemaps ?? [])
                        if (Filled(noisemap))
                            yield return ("UnusedNoisemaps", "Textures", noisemap);
                    break;

                case IBodyPartDataGetter body:
                    foreach (var part in body.Parts)
                        if (Filled(part.LimbReplacementModel))
                            yield return ("Parts.LimbReplacementModel", "Meshes", part.LimbReplacementModel!);
                    break;

                case IClassGetter @class:
                    if (Filled(@class.Icon))
                        yield return ("Icon", "Textures", @class.Icon!);
                    break;

                case IFactionGetter faction:
                    foreach (var rank in faction.Ranks)
                        if (Filled(rank.Insignia))
                            yield return ("Ranks.Insignia", "Textures", rank.Insignia!);
                    break;

                // Legacy, and raw: the sound descriptor replaced this, so Mutagen
                // leaves the subrecord unparsed. Two in five of the values are a
                // folder rather than a file -- the marker picks any sound in it --
                // and a folder is not an asset, so only the files are reported.
                case ISoundMarkerGetter sound:
                    if (FileFromZString(sound.FNAM) is { } soundFile)
                        yield return ("FNAM", "Sound", soundFile);
                    break;

                case IWeatherGetter weather:
                    if (FileFromZString(weather.DNAM) is { } cloudLayer0)
                        yield return ("DNAM", "Textures", cloudLayer0);
                    if (FileFromZString(weather.CNAM) is { } cloudLayer1)
                        yield return ("CNAM", "Textures", cloudLayer1);
                    if (FileFromZString(weather.ANAM) is { } cloudLayer2)
                        yield return ("ANAM", "Textures", cloudLayer2);
                    if (FileFromZString(weather.BNAM) is { } cloudLayer3)
                        yield return ("BNAM", "Textures", cloudLayer3);
                    break;
            }
        }

        private static bool Filled(string? value) => !string.IsNullOrWhiteSpace(value);

        /// <summary>
        /// A filename out of an unparsed subrecord, or null if it does not hold one.
        /// </summary>
        /// <remarks>
        /// The bytes are a zstring. Latin-1 decodes the format's single-byte
        /// characters without throwing on any of them, which matters here because
        /// these fields are undefined: the same subrecord in another record may hold
        /// a struct, and reading one has to come back empty-handed rather than fail.
        /// Requiring an extension is what tells a filename from both.
        /// </remarks>
        private static string? FileFromZString(ReadOnlyMemorySlice<byte>? raw)
        {
            if (raw is not { } bytes || bytes.Length == 0)
                return null;

            string text = Encoding.Latin1.GetString(bytes).TrimEnd('\0').Trim();

            if (text.Length == 0 || text.Any(character => character is < ' ' or > '~'))
                return null;

            // A folder, which several sound markers hold: the marker plays whatever
            // is in it, and no one file is being referred to.
            string extension = Path.GetExtension(text);

            return extension.Length is > 1 and <= 6 ? text : null;
        }
    }
}
