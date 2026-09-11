using System.Collections.Concurrent;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using SKAssets.Assets;
using SKAssets.Content.Assets;
using SKAssets.Content.Nif;
using SKAssets.Plugins;
using SKAssets.References;
using Xunit;

namespace SKAssets.Content.Tests
{
    /// <summary>
    /// The classification against every mesh the game's records name.
    /// </summary>
    /// <remarks>
    /// The rules in <see cref="MeshRules"/> were measured here before they were
    /// written, and this is what holds them to it. Each assertion is the count that
    /// measurement produced, so a Mutagen or NIFBX upgrade that changes what a mesh
    /// looks like fails rather than quietly reclassifying the game.
    ///
    /// <b>Nothing is extracted.</b> The archives are read in place.
    ///
    /// <b>It does not run unless asked.</b> Without <c>SKASSETS_SKYRIM_DATA</c> every
    /// test here returns. The sweep reads 17,670 meshes and takes about twenty
    /// minutes; <c>SKASSETS_NIF_SAMPLE=N</c> checks a subset, which is enough for the
    /// invariants but not for the counts, so those are skipped when it is set.
    ///
    /// <code>
    /// SKASSETS_SKYRIM_DATA="/path/to/Data" dotnet test --filter "FullyQualifiedName~MeshCorpus"
    /// </code>
    /// </remarks>
    [Trait("Category", "Corpus")]
    public class MeshCorpusTests
    {
        /// <summary>
        /// What one mesh came to, kept instead of the mesh.
        /// </summary>
        /// <remarks>
        /// The profile of a mesh carries every node name in it, which for 17,670
        /// meshes is most of a gigabyte and slows the sweep down more than it costs
        /// to classify twice. Everything the assertions ask about is decided here,
        /// while the profile is still in hand, and the profile is then dropped.
        /// </remarks>
        private sealed record Classified(
            string Path,
            NifRole Role,
            IReadOnlyList<string> Records,
            IReadOnlyList<MeshFinding> Findings,
            string? BehaviorGraph);

        private sealed record Sweep(IReadOnlyList<Classified> Meshes, bool Sampled);

        private static Sweep? _cached;
        private static readonly Lock Gate = new();

        private static string? DataFolder()
        {
            string? configured = Environment.GetEnvironmentVariable("SKASSETS_SKYRIM_DATA");

            if (string.IsNullOrWhiteSpace(configured))
                return null;

            Assert.True(Directory.Exists(configured), $"SKASSETS_SKYRIM_DATA is not a folder: {configured}");

            return configured;
        }

        /// <summary>
        /// Every mesh the masters name, read once and shared: the sweep is far too
        /// slow to repeat per test.
        /// </summary>
        private static Sweep? Meshes()
        {
            if (DataFolder() is not { } data)
                return null;

            lock (Gate)
            {
                if (_cached is not null)
                    return _cached;

                // Which records name which meshes.
                var sweeper = new PluginAssetSweeper();
                Dictionary<string, HashSet<string>> named = new(StringComparer.OrdinalIgnoreCase);

                foreach (string plugin in new[]
                         {
                             "Skyrim.esm", "Update.esm", "Dawnguard.esm",
                             "HearthFires.esm", "Dragonborn.esm", "_ResourcePack.esl",
                         })
                {
                    string path = Path.Combine(data, plugin);

                    if (!File.Exists(path))
                        continue;

                    foreach (AssetReference reference in sweeper.Sweep(path))
                    {
                        if (!reference.Path.Path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!named.TryGetValue(reference.Path.Path, out var records))
                            named[reference.Path.Path] = records = [];

                        records.Add(reference.RecordType);
                    }
                }

                // Where those meshes are.
                Dictionary<string, IArchiveFile> located = new(StringComparer.OrdinalIgnoreCase);

                foreach (string archive in Directory.GetFiles(data, "*.bsa"))
                    foreach (var file in Archive.CreateReader(GameRelease.SkyrimSE, archive).Files)
                        if (file.Path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                            located[file.Path.Replace('\\', '/')] = file;

                var wanted = named.Keys.Where(located.ContainsKey).ToList();
                bool sampled = false;

                if (int.TryParse(Environment.GetEnvironmentVariable("SKASSETS_NIF_SAMPLE"), out int sample)
                    && sample > 0 && sample < wanted.Count)
                {
                    wanted = wanted.OrderBy(path => path, StringComparer.Ordinal).Take(sample).ToList();
                    sampled = true;
                }

                var reader = new NifProfileReader();
                ConcurrentBag<Classified> classified = [];

                Parallel.ForEach(wanted, path =>
                {
                    using var stream = new MemoryStream(located[path].GetBytes());

                    NifProfile profile = reader.Read(stream);
                    var records = named[path].Order().ToList();

                    classified.Add(new Classified(
                        path,
                        NifRoles.Of(profile),
                        records,
                        records.SelectMany(record => MeshRules.Check(record, profile)).ToList(),
                        profile.BehaviorGraph));
                });

                Assert.NotEmpty(classified);

                return _cached = new Sweep(classified.ToList(), sampled);
            }
        }

        /// <summary>
        /// Every mesh the game names reads, and every one of them classifies as
        /// something. A file that reads and says nothing about itself is the case the
        /// classification would be silently useless for.
        /// </summary>
        [Fact]
        public void EveryMeshTheRecordsNameClassifies()
        {
            if (Meshes() is not { } sweep)
                return;

            var unknown = sweep.Meshes.Where(mesh => mesh.Role == NifRole.Unknown).ToList();

            // 25 meshes carry neither geometry, particles nor a controller: markers
            // and empty parents. They are named by STAT and MSTT records.
            Assert.True(unknown.Count < sweep.Meshes.Count / 400,
                $"{unknown.Count} of {sweep.Meshes.Count} meshes classify as nothing, e.g. {string.Join(", ", unknown.Take(5).Select(m => m.Path))}");
        }

        /// <summary>
        /// The shape of the game, by role. These are the counts the taxonomy was
        /// drawn from.
        /// </summary>
        [Fact]
        public void TheGameHasTheShapeTheTaxonomyDescribes()
        {
            if (Meshes() is not { } sweep || sweep.Sampled)
                return;

            var roles = sweep.Meshes
                .GroupBy(mesh => mesh.Role)
                .ToDictionary(group => group.Key, group => group.Count());

            Assert.Equal(17_670, sweep.Meshes.Count);

            // Most of the game is geometry, and the interesting parts are not.
            Assert.True(roles[NifRole.StaticGeometry] > 9_000, $"{roles[NifRole.StaticGeometry]} static meshes");
            Assert.Equal(3_201, roles[NifRole.SkinnedAttachment]);
            Assert.Equal(52, roles[NifRole.ActorSkeleton]);
            Assert.True(roles[NifRole.HavokProp] is > 900 and < 1_100, $"{roles[NifRole.HavokProp]} havok props");
        }

        /// <summary>
        /// What each record type names, which is the join the whole component is for.
        /// </summary>
        [Fact]
        public void EachRecordNamesTheKindOfMeshItsRulesAssume()
        {
            if (Meshes() is not { } sweep || sweep.Sampled)
                return;

            // Every head part in the game is a skin over somebody else's skeleton.
            AssertAll("HeadPart", NifRole.SkinnedAttachment, 1.0);

            // Armour almost always is; the rest are props and static pieces.
            AssertAll("ArmorAddon", NifRole.SkinnedAttachment, 0.90);

            // A race names a skeleton, and so does the ragdoll data beside it.
            AssertAll("Race", NifRole.ActorSkeleton, 0.95);
            AssertAll("BodyPartData", NifRole.ActorSkeleton, 1.0);

            // A camera shot is a path with nothing drawn on it.
            AssertAll("CameraShot", NifRole.CameraPath, 1.0);

            // An addon node is an effect dropped into another mesh.
            AssertAll("AddonNode", NifRole.Effect, 0.98);

            void AssertAll(string record, NifRole role, double share)
            {
                var named = sweep.Meshes.Where(mesh => mesh.Records.Contains(record)).ToList();

                Assert.NotEmpty(named);

                int matching = named.Count(mesh => mesh.Role == role);

                Assert.True(matching >= named.Count * share,
                    $"{record}: {matching} of {named.Count} are {role}, expected at least {share:P0}");
            }
        }

        /// <summary>
        /// The rules find little in the game, which is the point: a rule vanilla
        /// breaks in quantity is describing the checker rather than the format.
        /// </summary>
        [Fact]
        public void TheGameItselfPassesItsOwnRules()
        {
            if (Meshes() is not { } sweep || sweep.Sampled)
                return;

            var byRule = sweep.Meshes
                .SelectMany(mesh => mesh.Findings)
                .GroupBy(finding => finding.Rule)
                .ToDictionary(group => group.Key, group => group.Count());

            // BSXFlags: 18 of the 12,509 meshes that carry one disagree with their own
            // graph, and NIFBX's corpus ran each of them down.
            Assert.True(byRule.GetValueOrDefault("bsx-flags") < 25,
                $"{byRule.GetValueOrDefault("bsx-flags")} meshes disagree with their calculated BSXFlags");

            // No head part in the game breaks its rules at all.
            Assert.Equal(0, byRule.GetValueOrDefault("headpart-skinned"));
            Assert.Equal(0, byRule.GetValueOrDefault("headpart-external-skeleton"));

            // Two of the 54 meshes a RACE names are not skeletons.
            Assert.True(byRule.GetValueOrDefault("skeleton-expected") <= 2,
                $"{byRule.GetValueOrDefault("skeleton-expected")} race or ragdoll meshes are not skeletons");

            Assert.True(byRule.GetValueOrDefault("armor-external-skeleton") < 40,
                $"{byRule.GetValueOrDefault("armor-external-skeleton")} armour meshes skin to their own bones");
        }

        /// <summary>
        /// A mesh names a Havok graph, and the graph belongs to a prop project: never
        /// to an actor's, which is reached from a RACE record instead.
        /// </summary>
        [Fact]
        public void EveryBehaviourGraphAMeshNamesIsAPropProject()
        {
            if (Meshes() is not { } sweep || sweep.Sampled)
                return;

            var withGraph = sweep.Meshes.Where(mesh => mesh.BehaviorGraph is not null).ToList();

            Assert.True(withGraph.Count is > 1_000 and < 1_200, $"{withGraph.Count} meshes name a behaviour graph");

            // Where the Havok side is available as loose files, resolve them.
            string? havok = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES");

            if (string.IsNullOrWhiteSpace(havok))
                return;

            var index = SKAssets.Content.Havok.HavokProjectIndex.Load(havok);

            var resolved = withGraph
                .Select(mesh => index.Resolve(mesh.BehaviorGraph))
                .Where(project => project is not null)
                .ToList();

            Assert.True(resolved.Count >= withGraph.Count * 0.95,
                $"only {resolved.Count} of {withGraph.Count} behaviour graphs resolve to a project");

            Assert.All(resolved, project =>
                Assert.Equal(SKAssets.Content.Havok.ProjectKind.Prop, project!.Kind));
        }
    }
}
