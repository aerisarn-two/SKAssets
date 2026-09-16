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
        internal static string Chicken =>
            Corpus.ExtractedCreatures().FirstOrDefault(
                folder => folder.Replace('\\', '/').Contains("/chicken/", StringComparison.OrdinalIgnoreCase))
            ?? Corpus.ExtractedCreatures()[0];

        internal static SkyrimCache Cache => SkyrimCache.Load(Corpus.Havok!);

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

            // The bones the body had, as many as it had and no more, and flat under
            // its own root the way the game ships them.
            //
            // `>=` used to stand here, and it is why this passed while a mesh came
            // back with the whole skeleton threaded into it: the bones a mesh is
            // skinned to were kept along with every node above them, so a draugr's
            // hair arrived as nine nodes for its three and carried the skeleton's
            // `rigPerspective`, `rigVersion` and `species` in with them.
            NifModel shipped = NifModel.Load(
                Path.Combine(Path.GetDirectoryName(assets.Skeleton)!, "chicken.nif"), db);

            Assert.Equal(Census(shipped), Census(body));

            NifItem bodyRoot = body.GetBlock(body.FindItem(body.Footer, "Roots")!.Children[0]);

            Assert.Equal(
                Count(body, "NiNode") - 1,
                body.GetRefArray(bodyRoot, "Children").Count(c => c.Name == "NiNode"));

            Assert.Equal(0, Count(body, "bhkRigidBody"));
            Assert.Equal(0, Count(body, "bhkRagdollConstraint"));

            // And the animations, into the project.
            Assert.Empty(back.Clips!.Failed);
            Assert.Equal(assets.Project!.Animations.Count, back.Clips.Clips.Count);
        }

        /// <summary>
        /// Each file comes back rooted at its own root, not at the scene's.
        /// </summary>
        /// <remarks>
        /// A body is skinned to bones the skeleton owns, so keeping them keeps every
        /// node above them, which climbs out of the body and into the skeleton. Both
        /// roots were then left standing and NIFBX wrote a third above the pair, so a
        /// chicken's body came back as a `BSFadeNode` named `Scene` wrapping the
        /// `NiNode` it is -- and a draugr's as 118 blocks where its file holds 101,
        /// carrying the skeleton's flags and bounds with it.
        ///
        /// The root block is the claim worth making. A comparison of two NIFs walks
        /// from the root and stops where the roots are different block types, so while
        /// this was wrong every mesh comparison in the suite reported one difference
        /// and had compared nothing at all.
        /// </remarks>
        [ClipCorpusFact]
        public void EachFileComesBackRootedAtItsOwnRoot()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;
            var db = NifXmlDatabase.LoadEmbedded();

            FbxDocument scene = CreatureExchange.Export(assets, db, out _, slots: []);
            CreatureImport back = CreatureExchange.Import(scene, db);

            string folder = Path.GetDirectoryName(assets.Skeleton)!;

            foreach ((string file, NifModel rebuilt) in back.Meshes)
            {
                NifModel original = NifModel.Load(Path.Combine(folder, file), db);

                Assert.Equal(RootBlock(original).Name, RootBlock(rebuilt).Name);
            }

            // The name is not asserted, and the reason is worth keeping. A file whose
            // own root is the only node at the top of the scene is not rerooted -- the
            // skeleton's is -- and NIFBX writes its own root over that one, named
            // `Scene`. That is the same difference the skeleton shows on a round trip
            // with no DCC tool anywhere near it, so it belongs to the conversion and
            // not here, and asserting it here would move the failure rather than the
            // fault.
        }

        /// <summary>And without the skeleton's own belongings attached to it.</summary>
        /// <remarks>
        /// The skeleton's root was kept only because the body's bones hang beneath it,
        /// and everything it carries came along: a chicken's body arrived with the
        /// skeleton's BSXFlags on it, which is a statement about the whole creature
        /// made by a file that is one part of it.
        /// </remarks>
        [ClipCorpusFact]
        public void AndWithoutWhatTheSkeletonWasCarrying()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;
            var db = NifXmlDatabase.LoadEmbedded();

            FbxDocument scene = CreatureExchange.Export(assets, db, out _, slots: []);
            CreatureImport back = CreatureExchange.Import(scene, db);

            NifModel body = back.Meshes["chicken.nif"];

            Assert.Equal(0, Count(body, "BSFadeNode"));
            Assert.Equal(0, Count(body, "BSXFlags"));

            // And the mesh is still there, which is the thing a reroot can take with
            // it: the first version of this dropped five of a draugr's six shapes by
            // treating them as another file's.
            Assert.Equal(1, Count(body, "BSTriShape"));
        }

        /// <summary>
        /// The clips stay out of the meshes even when the stacks no longer say so.
        /// </summary>
        /// <remarks>
        /// A creature's animations are Havok clips, and a mesh rebuilt out of its scene
        /// must not take them in as NIF animation as well. The stacks were left out by
        /// asking each one whether it was a clip, which it answered from a property of
        /// its own -- and then the manifest arrived so that a scene back from a DCC
        /// tool could still name its clips, and this question went on being put to the
        /// stack. So the clips imported correctly into the project *and* stayed in the
        /// scene, and every mesh built from it swallowed all of them: a draugr's hair
        /// came back as 3,911 blocks where its file holds 11, its skeleton as 40,798
        /// where it holds 187.
        ///
        /// Both halves are asserted here, because the failure was that they disagreed:
        /// the clips are recognised, and none of them is in the mesh.
        /// </remarks>
        [ClipCorpusFact]
        public void TheClipsStayOutOfTheMeshesWhateverTheStacksAreCalled()
        {
            CreatureAssets assets = CreatureExchange.Find(Chicken, Cache)!;
            var db = NifXmlDatabase.LoadEmbedded();

            FbxDocument scene = CreatureExchange.Export(assets, db, out _);
            var live = new FbxScene(scene);

            // What Blender leaves: no properties, and a name of its own choosing.
            foreach (FbxObject stack in live.OfClass("AnimationStack").ToList())
            {
                string name = stack.Name;

                stack.Properties.Remove(ClipExchange.StoredNameProperty);
                stack.Properties.Remove(ClipExchange.CacheIndexProperty);
                stack.Properties.Remove(ClipExchange.GeneratorsProperty);

                stack.QualifiedName = $"AnimStack::skeleton.nif|skeleton.nif|{name}|Default";
            }

            live.Flush();

            SceneContents contents = CreatureExchange.Inspect(scene);
            Assert.True(contents.HasClips, "the clips were not recognised at all");

            CreatureImport back = CreatureExchange.Import(scene, db);

            foreach ((string file, NifModel model) in back.Meshes)
            {
                Assert.Equal(0, Count(model, "NiControllerSequence"));
                Assert.True(
                    model.Blocks.Count < 500,
                    $"{file} came back with {model.Blocks.Count} blocks, so it swallowed the clips");
            }
        }

        /// <summary>The first root the footer names.</summary>
        private static NifItem RootBlock(NifModel model) =>
            model.GetBlock(model.FindItem(model.Footer, "Roots")!.Children[0])!;

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

        /// <summary>
        /// A creature is a creature however its files are capitalised.
        /// </summary>
        /// <remarks>
        /// The game ships them capitalised -- `Character Assets/Skeleton.nif` -- and
        /// every fixture here happens to be lowercase, so asking the filesystem for
        /// the exact name passed on Windows and on this suite and failed on the one
        /// case that matters: a Linux box with the game's own files. `convert` on a
        /// draugr's folder saw four unrelated files instead of a creature, and wrote
        /// `Skeleton.nif` and `Skeleton.hkx` both out as `Skeleton.fbx`.
        /// </remarks>
        [Fact]
        public void ACreatureIsFoundHoweverItsNamesAreSpelled()
        {
            string folder = Path.Combine(Path.GetTempPath(), "skassets-case-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);

            try
            {
                File.WriteAllBytes(Path.Combine(folder, "Skeleton.nif"), []);
                File.WriteAllBytes(Path.Combine(folder, "Skeleton.hkx"), []);

                CreatureAssets? found = CreatureExchange.Find(folder);

                Assert.NotNull(found);
                Assert.Equal("Skeleton.nif", Path.GetFileName(found!.Skeleton));
                Assert.Equal("Skeleton.hkx", Path.GetFileName(found.Rig));

                // And the skeleton is not also counted as one of the bodies.
                Assert.Empty(found.Meshes);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
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
