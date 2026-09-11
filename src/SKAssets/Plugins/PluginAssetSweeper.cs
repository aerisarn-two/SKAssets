using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using SKAssets.Assets;
using SKAssets.References;

namespace SKAssets.Plugins
{
    /// <summary>
    /// Every external file a plugin refers to.
    /// </summary>
    /// <remarks>
    /// Reads an ESM, ESP or ESL and yields one <see cref="AssetReference"/> per
    /// reference: what the file is, where the reference is normalised to, which
    /// record made it and how it was found. Nothing is opened but the plugin -- the
    /// sweep never asks whether the file exists, which is why it works against a
    /// folder of plugins with no game installed.
    ///
    /// <code>
    /// var sweeper = new PluginAssetSweeper();
    ///
    /// foreach (var reference in sweeper.Sweep("Skyrim.esm"))
    ///     Console.WriteLine($"{reference.Path}\t{reference.MediaType.Mime}");
    /// </code>
    ///
    /// Records are read as a binary overlay, so the whole of Skyrim.esm sweeps in
    /// about half a minute without loading it into memory.
    /// </remarks>
    public sealed class PluginAssetSweeper
    {
        private readonly PluginSweepOptions _options;

        /// <summary>
        /// A sweeper with the given options, or the defaults.
        /// </summary>
        public PluginAssetSweeper(PluginSweepOptions? options = null)
        {
            _options = options ?? new PluginSweepOptions();
        }

        /// <summary>
        /// Sweep a plugin file.
        /// </summary>
        /// <param name="pluginPath">Path to an .esm, .esp or .esl.</param>
        /// <remarks>
        /// The plugin stays open for as long as the results are being read and is
        /// closed when they are finished with, so enumerate before disposing of
        /// anything -- <c>ToList()</c> if the results outlive the loop.
        /// </remarks>
        public IEnumerable<AssetReference> Sweep(string pluginPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);

            MutagenRuntime.Prepare();

            using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, _options.Release);

            foreach (var reference in Sweep(mod))
                yield return reference;
        }

        /// <summary>
        /// Sweep a plugin that is already open.
        /// </summary>
        public IEnumerable<AssetReference> Sweep(ISkyrimModGetter mod)
        {
            ArgumentNullException.ThrowIfNull(mod);

            MutagenRuntime.Prepare();

            // Every record in the file, including the ones inside cells and worldspaces.
            foreach (var record in mod.EnumerateMajorRecords())
                foreach (var reference in SweepRecord(record, mod.ModKey))
                    yield return reference;
        }

        private IEnumerable<AssetReference> SweepRecord(IMajorRecordGetter record, ModKey source)
        {
            string recordType = RecordTypeName(record);

            // Only needed to keep the deep scan from repeating what the passes above
            // it already found, so it is not built unless that pass runs.
            HashSet<string>? reported = _options.DeepScan
                ? new HashSet<string>(DataRelativePath.PathComparer)
                : null;

            var listed = Links(record, AssetLinkQuery.Listed);
            var inferred = _options.IncludeInferred ? Links(record, AssetLinkQuery.Inferred) : [];

            KeepOnlyItsOwn(record, listed, inferred);

            foreach (var link in listed)
            {
                var reference = FromLink(link, AssetReferenceOrigin.Listed, record, recordType, source);
                if (reference is null)
                    continue;

                reported?.Add(reference.Path.Path);
                yield return reference;
            }

            foreach (var link in inferred)
            {
                var reference = FromLink(link, AssetReferenceOrigin.Inferred, record, recordType, source);
                if (reference is null)
                    continue;

                reported?.Add(reference.Path.Path);
                yield return reference;
            }

            if (_options.IncludeUntyped)
            {
                foreach ((string field, string baseFolder, string value) in UntypedPathFields.Extract(record))
                {
                    var path = AssetPaths.Resolve(value, baseFolder);
                    if (path.IsNull)
                        continue;

                    reported?.Add(path.Path);

                    yield return new AssetReference
                    {
                        Path = path,
                        GivenPath = value,
                        MediaType = AssetMediaTypes.ForPath(path.Path),
                        Origin = AssetReferenceOrigin.Untyped,
                        Source = source,
                        Record = record.FormKey,
                        RecordType = recordType,
                        EditorId = record.EditorID,
                        Field = field,
                    };
                }
            }

            if (!_options.DeepScan)
                yield break;

            foreach ((string field, string value) in UntypedPathScanner.Scan(record))
            {
                var path = AssetPaths.Resolve(value, baseFolder: null);

                if (path.IsNull || !reported!.Add(path.Path))
                    continue;

                yield return new AssetReference
                {
                    Path = path,
                    GivenPath = value,
                    MediaType = AssetMediaTypes.ForPath(path.Path),
                    Origin = AssetReferenceOrigin.Scanned,
                    Source = source,
                    Record = record.FormKey,
                    RecordType = recordType,
                    EditorId = record.EditorID,
                    Field = field,
                };
            }
        }

        /// <summary>
        /// Drop the links a record only reports because of what is inside it.
        /// </summary>
        /// <remarks>
        /// Asking a record for its asset links asks its contained records too: a
        /// DialogTopic hands back every script on its responses, a cell every script
        /// on the objects placed in it. Since the sweep visits those records in their
        /// own right -- and they are the honest answer to "who refers to this file" --
        /// counting the container as well reports each of them twice. In Skyrim.esm
        /// that is 11,106 duplicated script references, one for every INFO.
        ///
        /// What is left after the subtraction is what the record itself holds, which
        /// is not always nothing: a worldspace carries its own model and its cells'
        /// contents in the same answer.
        ///
        /// Paying for this only when a record both has links and contains something
        /// keeps it off the millions of records that contain nothing.
        /// </remarks>
        private void KeepOnlyItsOwn(IMajorRecordGetter record, List<IAssetLinkGetter> listed, List<IAssetLinkGetter> inferred)
        {
            if (listed.Count == 0 && inferred.Count == 0)
                return;

            Dictionary<string, int>? containedListed = null;
            Dictionary<string, int>? containedInferred = null;

            foreach (var contained in ContainedRecords(record))
            {
                containedListed ??= new Dictionary<string, int>(DataRelativePath.PathComparer);
                containedInferred ??= new Dictionary<string, int>(DataRelativePath.PathComparer);

                Count(Links(contained, AssetLinkQuery.Listed), containedListed);

                if (_options.IncludeInferred)
                    Count(Links(contained, AssetLinkQuery.Inferred), containedInferred);
            }

            if (containedListed is null)
                return;

            Subtract(listed, containedListed);
            Subtract(inferred, containedInferred!);

            static void Count(List<IAssetLinkGetter> links, Dictionary<string, int> into)
            {
                foreach (var link in links)
                {
                    string path = link.DataRelativePath.Path;
                    into[path] = into.GetValueOrDefault(path) + 1;
                }
            }

            static void Subtract(List<IAssetLinkGetter> links, Dictionary<string, int> contained)
            {
                if (contained.Count == 0)
                    return;

                links.RemoveAll(link =>
                {
                    string path = link.DataRelativePath.Path;

                    if (!contained.TryGetValue(path, out int remaining) || remaining == 0)
                        return false;

                    contained[path] = remaining - 1;
                    return true;
                });
            }
        }

        /// <summary>
        /// The records a record holds, which are swept in their own right.
        /// </summary>
        /// <remarks>
        /// The whole of Skyrim's containment: a worldspace holds cells, a cell holds
        /// what is placed in it and its navigation meshes, a dialogue topic holds its
        /// responses. Quests do not hold their topics -- those are records of their
        /// own -- and nothing else nests at all.
        /// </remarks>
        private static IEnumerable<IMajorRecordGetter> ContainedRecords(IMajorRecordGetter record)
        {
            switch (record)
            {
                case IDialogTopicGetter topic:
                    foreach (var response in topic.Responses)
                        yield return response;
                    break;

                case ICellGetter cell:
                    foreach (var placed in cell.Persistent)
                        yield return placed;
                    foreach (var placed in cell.Temporary)
                        yield return placed;
                    foreach (var navigationMesh in cell.NavigationMeshes)
                        yield return navigationMesh;
                    break;

                case IWorldspaceGetter worldspace:
                    if (worldspace.TopCell is not null)
                        yield return worldspace.TopCell;
                    foreach (var block in worldspace.SubCells)
                        foreach (var subBlock in block.Items)
                            foreach (var cell in subBlock.Items)
                                yield return cell;
                    break;
            }
        }

        /// <summary>
        /// One record's links, with a failure to read them reported as no links
        /// rather than as the end of the sweep.
        /// </summary>
        /// <remarks>
        /// A malformed subrecord in one record of a hundred thousand should cost that
        /// record, not the file. The enumeration has to be drained here rather than
        /// yielded lazily, because a <c>try</c> cannot wrap a <c>yield</c>.
        /// </remarks>
        private static List<IAssetLinkGetter> Links(IMajorRecordGetter record, AssetLinkQuery query)
        {
            try
            {
                return record.EnumerateAssetLinks(query).ToList();
            }
            catch
            {
                return [];
            }
        }

        private static AssetReference? FromLink(
            IAssetLinkGetter link,
            AssetReferenceOrigin origin,
            IMajorRecordGetter record,
            string recordType,
            ModKey source)
        {
            // An empty model or texture field is a record that has one and does not
            // use it. Two thousand of them in the masters, referring to nothing.
            if (link.IsNull || string.IsNullOrWhiteSpace(link.GivenPath))
                return null;

            return new AssetReference
            {
                Path = link.DataRelativePath,
                GivenPath = link.GivenPath,
                MediaType = AssetMediaTypes.ForPath(link.DataRelativePath.Path),
                Origin = origin,
                Source = source,
                Record = record.FormKey,
                RecordType = recordType,
                EditorId = record.EditorID,
                DeclaredType = AssetTypeName(link.Type),
            };
        }

        /// <summary>
        /// <c>Static</c> rather than <c>StaticBinaryOverlay</c>: the same name
        /// whether the plugin was overlaid or loaded.
        /// </summary>
        private static string RecordTypeName(IMajorRecordGetter record)
        {
            string name = record.GetType().Name;

            return name.EndsWith("BinaryOverlay", StringComparison.Ordinal)
                ? name[..^"BinaryOverlay".Length]
                : name;
        }

        /// <summary>
        /// <c>SkyrimModelAssetType</c> as the format names it: <c>Model</c>.
        /// </summary>
        private static string AssetTypeName(IAssetType type)
        {
            string name = type.GetType().Name;

            if (name.StartsWith("Skyrim", StringComparison.Ordinal))
                name = name["Skyrim".Length..];

            return name.EndsWith("AssetType", StringComparison.Ordinal)
                ? name[..^"AssetType".Length]
                : name;
        }
    }
}
