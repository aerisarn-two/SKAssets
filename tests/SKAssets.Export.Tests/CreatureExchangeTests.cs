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
        /// Every node says which files it belongs to, and a bone belongs to more
        /// than one.
        /// </summary>
        /// <remarks>
        /// The whole of what makes the scene separable again. A bone the body is
        /// skinned to is in the skeleton file and in the body file, and the merge
        /// keeps one node for both; unless the node says so, writing the creature
        /// back out can only produce one enormous skeleton.nif with every body
        /// inside it.
        /// </remarks>
        [CreatureFact]
        public void EveryNodeSaysWhichFilesItBelongsTo()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;

            FbxDocument scene = CreatureExchange.Export(
                assets, NifXmlDatabase.LoadEmbedded(), out _, slots: []);

            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int shared = 0, bodyOnly = 0;

            foreach (FbxObject model in new FbxScene(scene).OfClass("Model"))
            {
                IReadOnlyList<string> sources = CreatureExchange.SourcesOf(model);

                foreach (string source in sources) files.Add(source);

                if (sources.Count > 1) shared++;
                else if (sources.Count == 1 && sources[0].Equals("chicken.nif", StringComparison.OrdinalIgnoreCase))
                    bodyOnly++;
            }

            Assert.Equal(["skeleton.nif", "chicken.nif"], files.OrderBy(f => f, StringComparer.Ordinal).Reverse());

            // The bones the skin binds to are in both files; the mesh node is the
            // body's alone.
            Assert.True(shared > 0, "no node was shared between the skeleton and the body");
            Assert.True(bodyOnly > 0, "the body contributed no node of its own");
        }

        /// <summary>
        /// A scene holding everything comes apart into the files it was built from.
        /// </summary>
        /// <remarks>
        /// The body and the skeleton share their bones, so a scene that cannot say
        /// which node belongs to which file can only be written back as one
        /// enormous skeleton.nif with the body inside it. This is the claim that it
        /// can.
        ///
        /// The body does not come back byte for byte and is not expected to: it
        /// arrives with the skeleton above it, because a scene has one root and the
        /// body's own was merged away when the two were joined. What it does come
        /// back with is every bone it was skinned to, its geometry, and a skin
        /// binding them — which is what makes it a body rather than a pile of
        /// triangles.
        /// </remarks>
        [ClipCorpusFact]
        public void AWholeCreatureComesApartIntoTheFilesItWasBuiltFrom()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;
            var db = NifXmlDatabase.LoadEmbedded();

            FbxDocument scene = CreatureExchange.Export(assets, db, out _);
            CreatureImport back = CreatureExchange.Import(scene, db, assets.Project);

            // All four halves.
            Assert.NotNull(back.Skeleton);
            Assert.NotNull(back.Havok);
            Assert.NotNull(back.Clips);
            Assert.Equal(2, back.Meshes.Count);

            // The skeleton, with the ragdoll it describes.
            NifModel original = NifModel.Load(assets.Skeleton, db);
            Assert.Equal(original.Blocks.Count, back.Skeleton!.Blocks.Count);

            Assert.Equal(
                Census(original),
                Census(back.Skeleton));

            // The body, with its skin.
            NifModel body = back.Meshes["chicken.nif"];

            Assert.Equal(1, Count(body, "BSTriShape"));
            Assert.Equal(1, Count(body, "NiSkinInstance"));
            Assert.Equal(1, Count(body, "NiSkinPartition"));

            // Every bone the body had, and none of the skeleton's collision.
            Assert.True(Count(body, "NiNode") >= Count(NifModel.Load(
                Path.Combine(Path.GetDirectoryName(assets.Skeleton)!, "chicken.nif"), db), "NiNode"));

            Assert.Equal(0, Count(body, "bhkRigidBody"));
            Assert.Equal(0, Count(body, "bhkRagdollConstraint"));

            // And the animations, into the project.
            Assert.Empty(back.Clips!.Failed);
            Assert.Equal(assets.Project!.Animations.Count, back.Clips.Clips.Count);
        }

        /// <summary>
        /// A rig on its own is a rig, and no mesh is invented for it.
        /// </summary>
        [CreatureFact]
        public void ARigWithNoMeshComesBackAsARig()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;

            FbxDocument scene = HKFBX.Fbx.FbxSkeletonWriter.Build(
                HKFBX.Hkx.HkxSkeletonFile.Read(assets.Rig!));

            CreatureImport back = CreatureExchange.Import(scene, NifXmlDatabase.LoadEmbedded());

            Assert.NotNull(back.Havok);
            Assert.NotEmpty(back.Havok!.Rig.Bones);
            Assert.Empty(back.Meshes);
            Assert.Null(back.Skeleton);
            Assert.Null(back.Clips);
        }

        /// <summary>
        /// And a mesh on its own is a mesh: nothing writes a skeleton.hkx for a
        /// scene that never carried a rig, because there would be nothing to write
        /// it from.
        /// </summary>
        [CreatureFact]
        public void AMeshWithNoRigComesBackAsAMesh()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;
            var db = NifXmlDatabase.LoadEmbedded();

            FbxDocument scene = SkeletonExchange.Export(NifModel.Load(assets.Skeleton, db));
            CreatureImport back = CreatureExchange.Import(scene, db);

            Assert.NotNull(back.Skeleton);
            Assert.Single(back.Meshes);
            Assert.Null(back.Clips);
        }

        /// <summary>
        /// A scene this library did not build has no record of what it was made
        /// from, and is read as the one mesh it is.
        /// </summary>
        [CreatureFact]
        public void ASceneWithNoRecordOfItsFilesIsOneMesh()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;
            var db = NifXmlDatabase.LoadEmbedded();

            FbxDocument scene = new NIFBX.Conversion.NifToFbx(
                NifModel.Load(assets.Meshes[0], db)).Convert();

            CreatureImport back = CreatureExchange.Import(scene, db);

            Assert.Single(back.Meshes);
            Assert.NotNull(back.Skeleton);
            Assert.Null(back.Clips);
        }

        private static string Census(NifModel model) =>
            string.Join(' ', model.Blocks.GroupBy(b => b.Def.Name)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}:{g.Count()}"));

        private static int Count(NifModel model, string type) =>
            model.Blocks.Count(b => b.Def.Name == type);

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
