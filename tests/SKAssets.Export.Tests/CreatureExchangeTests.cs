using HKSK.Model;
using LeanMeshIO;
using NIFBX.Conversion;
using NIFSharp;
using SKAssets.Export;
using Xunit;
using FbxObject = NIFBX.Fbx.FbxObject;
using FbxScene = NIFBX.Fbx.FbxScene;

namespace SKAssets.Export.Tests
{
    /// <summary>
    /// A creature in one call: its bodies, its skeleton, its ragdoll and every
    /// animation its project has.
    /// </summary>
    /// <remarks>
    /// <b>It does not run unless asked.</b> A creature is a folder rather than a
    /// file, so these want the extracted tree:
    ///
    /// <code>
    /// SKASSETS_HAVOK_MESHES=/path/to/loose/meshes \
    ///     dotnet test --filter "FullyQualifiedName~CreatureExchange"
    /// </code>
    ///
    /// The chicken again, for the reason it is always the chicken: two files and
    /// twenty clips is a creature small enough to build twice in a test.
    /// </remarks>
    public sealed class CreatureExchangeTests
    {
        private static string Chicken =>
            Corpus.ExtractedCreatures().FirstOrDefault(
                folder => folder.Replace('\\', '/').Contains("/chicken/", StringComparison.OrdinalIgnoreCase))
            ?? Corpus.ExtractedCreatures()[0];

        private static SkyrimCache Cache => SkyrimCache.Load(Corpus.Havok!);

        [CreatureFact]
        public void ACreatureIsItsSkeletonAndEverythingBesideIt()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;

            Assert.NotNull(assets);
            Assert.Equal("chicken", assets.Name);
            Assert.EndsWith("skeleton.nif", assets.Skeleton, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("skeleton.hkx", assets.Rig!, StringComparison.OrdinalIgnoreCase);

            // The body, and not the skeleton again.
            Assert.Contains(assets.Meshes, m => m.EndsWith("chicken.nif", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(assets.Meshes, m => m.EndsWith("skeleton.nif", StringComparison.OrdinalIgnoreCase));

            // And the project, found by where its rig sits rather than by its name.
            Assert.Equal("ChickenProject", assets.Project!.Name);
        }

        [CreatureFact]
        public void AFolderWithNoSkeletonIsNotACreature()
        {
            Assert.Null(CreatureExchange.Find(Path.GetTempPath()));
        }

        /// <summary>
        /// The body arrives bound to the skeleton's own bones rather than to a
        /// second copy of them.
        /// </summary>
        /// <remarks>
        /// This is the whole of what the merge does and the only way it can fail
        /// quietly: a scene with two Pelvises animates the one the clips drive and
        /// shows the one the skin is weighted to, so the creature stands still while
        /// its skeleton walks out from under it.
        /// </remarks>
        [CreatureFact]
        public void TheBodyIsBoundToTheSkeletonsOwnBones()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;
            var db = NifXmlDatabase.LoadEmbedded();

            FbxDocument scene = CreatureExchange.Export(assets, db, out CreatureReport report, slots: []);

            Assert.Empty(report.Unmerged);
            Assert.Empty(report.Unbound);
            Assert.Equal(assets.Meshes.Count, report.Meshes.Count);

            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (FbxObject model in new FbxScene(scene).OfClass("Model"))
            {
                string name = NameEncoding.Unsanitize(model.Name);
                byName[name] = byName.TryGetValue(name, out int n) ? n + 1 : 1;
            }

            // Every bone the rig names, exactly once.
            foreach (string bone in report.Bones > 0
                ? HKFBX.Hkx.HkxSkeletonFile.Read(assets.Rig!).Rig.Bones.Select(b => b.Name)
                : [])
            {
                Assert.True(byName.TryGetValue(bone, out int count), $"no node for {bone}");
                Assert.True(count == 1, $"{count} nodes called {bone}");
            }

            // And the body actually came in.
            Assert.NotEmpty(new FbxScene(scene).OfClass("Geometry"));
        }

        /// <summary>
        /// Every mesh folded in says which file it came out of.
        /// </summary>
        [CreatureFact]
        public void AFoldedBodySaysWhichFileItCameFrom()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;

            FbxDocument scene = CreatureExchange.Export(
                assets, NifXmlDatabase.LoadEmbedded(), out _, slots: []);

            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (FbxObject model in new FbxScene(scene).OfClass("Model"))
            {
                string source = model.Properties.GetString(CreatureExchange.SourceProperty);

                if (source.Length > 0) sources.Add(source);
            }

            Assert.Equal(["chicken.nif"], sources);
        }

        /// <summary>
        /// And with the clips: every animation the project has, one stack each,
        /// every one of them driving bones.
        /// </summary>
        [ClipCorpusFact]
        public void EveryAnimationTheProjectHasBecomesAStackThatDrivesBones()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;

            FbxDocument scene = CreatureExchange.Export(
                assets, NifXmlDatabase.LoadEmbedded(), out CreatureReport report);

            Assert.NotNull(report.Clips);
            Assert.Empty(report.Clips!.Missing);
            Assert.Empty(report.Clips.Unreadable);
            Assert.Empty(report.Clips.Inert);
            Assert.Equal(assets.Project!.Animations.Count, report.Clips.Stacks.Count);

            // One stack per animation, and the bodies did not bring any of their own.
            Assert.Equal(
                report.Clips.Stacks.Count,
                new FbxScene(scene).OfClass("AnimationStack").Count());
        }
    }
}
