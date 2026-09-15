using HKFBX.Codec;
using HKFBX.Fbx;
using HKFBX.Hkx;
using HKFBX.Model;
using HKSK.Cache;
using HKSK.Model;
using LeanMeshIO;
using NIFSharp;
using SKAssets.Export;
using Xunit;

namespace SKAssets.Export.Tests
{
    /// <summary>
    /// A creature's whole animation set out to one scene and back into the files
    /// the game reads.
    /// </summary>
    /// <remarks>
    /// <b>It does not run unless asked.</b> It needs the extracted meshes folder
    /// and Havok's codec:
    ///
    /// <code>
    /// SKASSETS_HAVOK_MESHES=/path/to/loose/meshes \
    ///     dotnet test --filter "FullyQualifiedName~ClipRoundTrip"
    /// </code>
    ///
    /// Everything here works on a copy. The import writes animation packfiles and
    /// edits the cache, and the corpus is game data.
    ///
    /// The subject is the chicken: 20 animations, small enough to compress twenty
    /// times in a test and real enough that what it says is true of the rest.
    /// </remarks>
    public sealed class ClipRoundTripTests
    {
        /// <summary>
        /// Every stack the export wrote comes back as the animation it came from.
        /// </summary>
        /// <remarks>
        /// The claim worth making is about the cache rather than the curves: a
        /// clip's travel is not in its packfile, it is in
        /// <c>animationdatasinglefile.txt</c>, and a round trip that loses it
        /// produces a creature that animates correctly and never moves. The
        /// packfiles are rewritten through Havok's own lossy spline encoder, so
        /// they are not expected back to the byte -- what is expected back is every
        /// slot, at its own index, with its own travel and turn.
        /// </remarks>
        [ClipCorpusFact]
        public void EveryClipComesBackAsTheAnimationItCameFrom()
        {
            using var work = new Workspace();
            ActorProject project = work.Chicken();

            var before = project.Animations
                .ToDictionary(a => a.StoredName, a => (a.Index, a.Motion?.Travel ?? 0f, a.Motion?.Turn ?? 0f),
                    StringComparer.OrdinalIgnoreCase);

            FbxDocument document = work.Scene(project, out SkeletonFile havok);
            ClipReport exported = ClipExchange.AddClips(document, havok.Rig, project);

            Assert.Empty(exported.Unreadable);
            Assert.Empty(exported.Inert);
            Assert.Equal(project.Animations.Count, exported.Stacks.Count);

            ImportReport imported = ClipExchange.ImportClips(document, project);

            Assert.Empty(imported.Failed);
            Assert.Equal(exported.Stacks.Count, imported.Clips.Count);

            // Nothing was added: every stack matched a slot the project already had.
            Assert.Equal(before.Count, project.Animations.Count);

            foreach (AnimationSlot slot in project.Animations)
            {
                (int index, float travel, float turn) = before[slot.StoredName];

                Assert.Equal(index, slot.Index);
                Assert.Equal(travel, slot.Motion?.Travel ?? 0f, 2);
                Assert.Equal(turn, slot.Motion?.Turn ?? 0f, 2);
            }
        }

        /// <summary>
        /// And the curves themselves: every clip comes back animating what it
        /// animated.
        /// </summary>
        /// <remarks>
        /// HKFBX proves this for one animation at a time -- the chicken's walk out
        /// to an FBX and back lands within a hundredth of a unit on every bone of
        /// every frame. The reason to prove it again here is that this layer does
        /// something HKFBX's test cannot: twenty clips into <em>one</em> document,
        /// where they share the nodes they drive and the properties they drive them
        /// on. Every failure found while writing these tests was of that kind, and
        /// each one left the cache perfectly correct.
        ///
        /// The root bone is the sharpest instrument here. Its track is where the
        /// export puts the cache's travel and where the import takes it back off,
        /// so a clip whose root comes back as another clip's -- or as nothing --
        /// says so louder than any other bone.
        ///
        /// The loop is lossy on purpose: the original spline is decompressed,
        /// sampled onto nodes, read back and compressed again through Havok's own
        /// encoder. What is asserted is the drift that survives all of it. Measured
        /// over the chicken's twenty clips, every frame and every one of its 33
        /// tracks, the worst is 0.0011 units of translation and 0.0051 of quaternion
        /// distance, both on MT_Idle. The bounds are set well above that, because
        /// the failure this is for is not drift: a track in the wrong place is a
        /// whole bone's motion misplaced and arrives in units, not thousandths --
        /// the one that prompted this test was 9.7.
        /// </remarks>
        [ClipCorpusFact]
        public void EveryClipComesBackAnimatingWhatItAnimated()
        {
            using var work = new Workspace();
            ActorProject project = work.Chicken();

            var codec = new MopperAnimationCodec();
            var before = new Dictionary<string, SampledAnimation>(StringComparer.OrdinalIgnoreCase);

            foreach (AnimationSlot slot in project.Animations)
                if (project.AnimationPath(slot) is { } path && File.Exists(path))
                    before[slot.StoredName] = Decoded(path, codec);

            Assert.NotEmpty(before);

            FbxDocument document = work.Scene(project, out SkeletonFile havok);
            ClipReport exported = ClipExchange.AddClips(document, havok.Rig, project);

            Assert.Empty(exported.Unreadable);

            ImportReport imported = ClipExchange.ImportClips(document, project);
            Assert.Empty(imported.Failed);

            double worstTranslation = 0, worstRotation = 0;
            string worst = string.Empty;

            foreach (ImportedClip clip in imported.Clips)
            {
                SampledAnimation original = before[clip.StoredName];
                SampledAnimation after = Decoded(clip.Path!, codec);

                Assert.Equal(original.FrameCount, after.FrameCount);
                Assert.Equal(original.TrackCount, after.TrackCount);

                for (int frame = 0; frame < original.FrameCount; frame++)
                for (int track = 0; track < original.TrackCount; track++)
                {
                    BoneTransform a = original[frame, track];
                    BoneTransform b = after[frame, track];

                    double translation = (a.Translation - b.Translation).Length();

                    // A quaternion and its negation are the same rotation, and the
                    // codec is free to hand back either.
                    double rotation = Math.Min(
                        (a.Rotation - b.Rotation).Length(), (a.Rotation + b.Rotation).Length());

                    if (translation > worstTranslation || rotation > worstRotation)
                        worst = $"{clip.Stack} frame {frame} track {track}";

                    worstTranslation = Math.Max(worstTranslation, translation);
                    worstRotation = Math.Max(worstRotation, rotation);
                }
            }

            Assert.True(worstTranslation < 1e-1,
                $"translation drifted {worstTranslation} at {worst}");

            Assert.True(worstRotation < 1e-2,
                $"rotation drifted {worstRotation} at {worst}");
        }

        /// <summary>An animation packfile as frames of bone transforms.</summary>
        private static SampledAnimation Decoded(string path, IAnimationCodec codec)
        {
            (SplineAnimationData spline, IReadOnlyList<short> trackToBone, _) =
                HkxAnimationFile.ReadAnimation(path);

            return codec.Decompress(spline) with { TrackToBone = trackToBone };
        }

        /// <summary>
        /// And the cache written after that trip still says what it said.
        /// </summary>
        /// <remarks>
        /// The import edits the cache in memory; saving is the caller's, and what
        /// reaches the disk is what the game reads. A file that parses but has gained
        /// a movement block, or lost a clip, or renumbered a project it never touched,
        /// is a cache that loads and an actor that behaves differently.
        ///
        /// The travel numbers themselves are compared to two decimals rather than as
        /// text: they leave as a cache entry, are sampled onto a bone, read back off
        /// it and written out again, and asking that to reproduce a decimal string
        /// exactly would be asking the wrong question. Everything that is not a
        /// number is compared exactly.
        /// </remarks>
        [ClipCorpusFact]
        public void TheCacheSavedAfterTheTripStillSaysWhatItSaid()
        {
            using var work = new Workspace();
            ActorProject project = work.Chicken();

            string originalSets = work.Cache.SetData.Write();

            var projects = work.Cache.AnimationData.Projects
                .Select(p => (p.Name, Clips: p.Block.Clips.Count, Movements: p.Movements?.Movements.Count ?? 0))
                .ToList();

            var slots = project.Animations
                .ToDictionary(a => a.StoredName, a => (a.Index, a.Motion?.Travel ?? 0f, a.Motion?.Turn ?? 0f),
                    StringComparer.OrdinalIgnoreCase);

            var clips = project.Clips
                .ToDictionary(c => c.Entry.Name, c => c.Entry.CacheIndex, StringComparer.OrdinalIgnoreCase);

            FbxDocument document = work.Scene(project, out SkeletonFile havok);
            ClipExchange.AddClips(document, havok.Rig, project);
            ImportReport imported = ClipExchange.ImportClips(document, project);

            Assert.Empty(imported.Failed);

            work.Cache.Save();

            SkyrimCache reloaded = SkyrimCache.Load(work.Meshes);

            // Every project, in its own order, with the clips and the movement
            // blocks it had. A round trip that writes a movement for every
            // animation rather than for the ones that travel shows up here and
            // nowhere else: the actor still animates, and walks on the spot.
            Assert.Equal(
                projects,
                reloaded.AnimationData.Projects
                    .Select(p => (p.Name, Clips: p.Block.Clips.Count, Movements: p.Movements?.Movements.Count ?? 0))
                    .ToList());

            // Nothing writes to the set data, so it should come back to the byte.
            Assert.Equal(originalSets, reloaded.SetData.Write());

            ActorProject after = reloaded.OpenActor("ChickenProject")!;

            Assert.Equal(slots.Count, after.Animations.Count);

            foreach (AnimationSlot slot in after.Animations)
            {
                (int index, float travel, float turn) = slots[slot.StoredName];

                Assert.Equal(index, slot.Index);
                Assert.Equal(travel, slot.Motion?.Travel ?? 0f, 2);
                Assert.Equal(turn, slot.Motion?.Turn ?? 0f, 2);
            }

            Assert.Equal(
                clips.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase),
                after.Clips.ToDictionary(c => c.Entry.Name, c => c.Entry.CacheIndex,
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A stack the project has no animation for arrives as a new slot, appended,
        /// with everything already numbered left where it was.
        /// </summary>
        /// <remarks>
        /// This is what importing an authored creature actually does, and the risk it
        /// carries. A slot's index is what every clip generator addresses it by, so an
        /// import that inserts rather than appends silently repoints every clip after
        /// it at the wrong animation.
        /// </remarks>
        [ClipCorpusFact]
        public void AnAnimationTheProjectDoesNotHaveIsAppendedRatherThanInserted()
        {
            using var work = new Workspace();
            ActorProject project = work.Chicken();

            var before = project.Animations.ToDictionary(a => a.StoredName, a => a.Index,
                StringComparer.OrdinalIgnoreCase);

            var generators = project.Clips
                .ToDictionary(c => c.Entry.Name, c => c.Entry.CacheIndex, StringComparer.OrdinalIgnoreCase);

            FbxDocument document = work.Scene(project, out SkeletonFile havok);

            AnimationSlot source = project.Animation("TurnLoopingL")!;
            ClipExchange.AddClips(document, havok.Rig, project, [source]);

            // Rename the stack's animation to one the project has never heard of,
            // which is what a scene carrying an animator's new clip looks like.
            var scene = new FbxScene(document);
            FbxObject stack = scene.OfClass("AnimationStack")
                .Single(s => s.Properties.GetString(ClipExchange.StoredNameProperty).Length > 0);

            stack.Properties.SetUserString(ClipExchange.StoredNameProperty, @"Animations\TurnLoopingLL.hkx");
            stack.Properties.SetUserString(ClipExchange.GeneratorsProperty, "TurnLoopingLL");
            scene.Flush();

            ImportReport imported = ClipExchange.ImportClips(document, project);

            Assert.Empty(imported.Failed);
            ImportedClip clip = Assert.Single(imported.Clips);

            Assert.Equal(before.Count + 1, project.Animations.Count);
            Assert.Equal(before.Count, clip.CacheIndex);

            foreach ((string stored, int index) in before)
                Assert.Equal(index, project.Animation(stored)!.Index);

            foreach ((string name, int index) in generators)
                Assert.Equal(index, project.Clip(name)!.Entry.CacheIndex);

            // And the clip the stack named can now play it.
            Assert.Equal(clip.CacheIndex, project.Clip("TurnLoopingLL")!.Entry.CacheIndex);
        }

        /// <summary>
        /// A stack this library did not write is left where it is.
        /// </summary>
        /// <remarks>
        /// Seven of the game's 49 actors carry animation in their skeleton.nif, which
        /// NIFBX writes as a stack like any other. Reading it back as a clip would
        /// invent an animation the creature never had and give the cache a slot with
        /// no file behind it.
        /// </remarks>
        [ClipCorpusFact]
        public void AStackTheExportDidNotWriteIsIgnored()
        {
            using var work = new Workspace();
            ActorProject project = work.Chicken();

            FbxDocument document = work.Scene(project, out SkeletonFile havok);

            FbxAnimationWriter.AddStack(
                document, havok.Rig, Flat(havok.Rig), "a_stack_from_the_mesh");

            int animations = project.Animations.Count;
            ImportReport imported = ClipExchange.ImportClips(document, project);

            Assert.Empty(imported.Failed);
            Assert.Empty(imported.Clips);
            Assert.Equal(["a_stack_from_the_mesh"], imported.Ignored);
            Assert.Equal(animations, project.Animations.Count);
        }

        /// <summary>
        /// A scene whose stacks a DCC tool has stripped and renamed still comes back.
        /// </summary>
        /// <remarks>
        /// This is what Blender does, and it is not misbehaviour: an animation stack
        /// arrives as an action, which keeps the curves and the name and has nowhere
        /// to put the stack's properties, and the stacks written on the way out are
        /// new ones Blender named itself --
        /// <c>Skeleton.nif|Skeleton.nif|1HMAttackA|Default</c>, the object and the
        /// action and the track joined by bars. Measured on a draugr: 217 stacks out,
        /// 217 stacks back, and 216 of 216 clip properties gone. Every animation was
        /// still there and none of them could be named, so the import counted no clips
        /// and wrote none.
        ///
        /// So the same three facts are written on a node as well, where they survive,
        /// and a stack that has lost its own is looked up there by whichever part of
        /// its new name it was given. The stripping here is the measured one: the
        /// three properties removed and the name decorated exactly that way.
        ///
        /// A stack that was never a clip has to stay ignored through all of it -- that
        /// is what <see cref="AStackTheExportDidNotWriteIsIgnored"/> is about, and a
        /// fallback that guesses would undo it -- so one is put in and checked for.
        /// </remarks>
        [ClipCorpusFact]
        public void ClipsComeBackFromASceneThatHasBeenThroughABlender()
        {
            using var work = new Workspace();
            ActorProject project = work.Chicken();

            FbxDocument document = work.Scene(project, out SkeletonFile havok);
            ClipReport exported = ClipExchange.AddClips(document, havok.Rig, project);

            Assert.NotEmpty(exported.Stacks);

            FbxAnimationWriter.AddStack(
                document, havok.Rig, Flat(havok.Rig), "a_stack_from_the_mesh");

            // What Blender leaves behind.
            var scene = new FbxScene(document);

            foreach (FbxObject stack in scene.OfClass("AnimationStack").ToList())
            {
                string name = stack.Name;

                stack.Properties.Remove(ClipExchange.StoredNameProperty);
                stack.Properties.Remove(ClipExchange.CacheIndexProperty);
                stack.Properties.Remove(ClipExchange.GeneratorsProperty);

                stack.QualifiedName = $"AnimStack::Skeleton.nif|Skeleton.nif|{name}|Default";
            }

            scene.Flush();

            Assert.All(
                new FbxScene(document).OfClass("AnimationStack"),
                stack => Assert.Empty(stack.Properties.GetString(ClipExchange.StoredNameProperty)));

            // The manifest is what is left, and it is enough.
            IReadOnlyDictionary<string, ClipExchange.ClipRecord> manifest =
                ClipExchange.Manifest(document);

            Assert.Equal(exported.Stacks.Count, manifest.Count);

            var before = project.Animations
                .ToDictionary(a => a.StoredName, a => a.Index, StringComparer.OrdinalIgnoreCase);

            ImportReport imported = ClipExchange.ImportClips(document, project);

            Assert.Empty(imported.Failed);
            Assert.Equal(exported.Stacks.Count, imported.Clips.Count);

            // The one that was never a clip is still not one.
            Assert.Equal(
                ["Skeleton.nif|Skeleton.nif|a_stack_from_the_mesh|Default"],
                imported.Ignored);

            // And each came back as the animation it was, not merely as some animation.
            Assert.Equal(before.Count, project.Animations.Count);

            foreach (ImportedClip clip in imported.Clips)
                Assert.Equal(before[clip.StoredName], clip.CacheIndex);
        }

        /// <summary>
        /// The whole creature, in one scene and back out: mesh, rig, ragdoll,
        /// clips and cache.
        /// </summary>
        /// <remarks>
        /// The other tests here each hold one half. This is the thing itself, the
        /// way a creature is actually authored -- everything into a single FBX, a
        /// DCC tool in the middle, and the files the game reads out the other side
        /// -- and it is the only test where the halves can interfere with each
        /// other. They can: adding twenty animation stacks to a scene puts curves
        /// on the very nodes whose exact transforms the skeleton exchange carries,
        /// and nothing else would notice if one displaced the other.
        ///
        /// Four claims, in the order a creature would break them:
        ///
        /// <list type="number">
        /// <item>the skeleton.hkx comes back byte for byte, clips in the scene or
        /// not;</item>
        /// <item>the skeleton.nif comes back with every block it had, the ragdoll
        /// constraints among them;</item>
        /// <item>every clip comes back animating what it animated;</item>
        /// <item>and the cache still addresses them the way it did.</item>
        /// </list>
        ///
        /// The chicken, because the whole set has to be compressed twenty times
        /// through Havok's codec and the draugr's 216 clips would make this a test
        /// nobody runs.
        /// </remarks>
        [ClipCorpusFact]
        public void AWholeCreatureGoesOutAsOneSceneAndComesBackAsItsFiles()
        {
            using var work = new Workspace();
            ActorProject project = work.Chicken();

            string havokPath = Path.Combine(work.Creature, "character assets", "skeleton.hkx");
            string meshPath = Path.Combine(work.Creature, "character assets", "skeleton.nif");

            Assert.True(File.Exists(meshPath), $"no skeleton.nif at {meshPath}");

            byte[] originalHavok = File.ReadAllBytes(havokPath);
            SkeletonFile havok = HkxSkeletonFile.Read(havokPath);

            var db = NifXmlDatabase.LoadEmbedded();
            NifModel mesh = NifModel.Load(meshPath, db);

            int blocks = mesh.Blocks.Count;
            int constraints = mesh.Blocks.Count(
                b => b.Def.Name.EndsWith("Constraint", StringComparison.Ordinal));

            var codec = new MopperAnimationCodec();
            var curves = new Dictionary<string, SampledAnimation>(StringComparer.OrdinalIgnoreCase);

            foreach (AnimationSlot slot in project.Animations)
                if (project.AnimationPath(slot) is { } path && File.Exists(path))
                    curves[slot.StoredName] = Decoded(path, codec);

            var slots = project.Animations.ToDictionary(
                a => a.StoredName, a => (a.Index, a.Motion?.Travel ?? 0f), StringComparer.OrdinalIgnoreCase);

            // Out: one scene holding all of it.
            FbxDocument scene = SkeletonExchange.Export(mesh, havok);
            ClipReport exported = ClipExchange.AddClips(scene, havok.Rig, project, codec: codec);

            Assert.Empty(exported.Unreadable);
            Assert.Empty(exported.Inert);

            // Through a file, because that is what a DCC tool hands back.
            string fbx = Path.Combine(work.Folder, "chicken.fbx");
            scene.Save(fbx);

            FbxDocument back = FbxDocument.Load(fbx);

            // And in: the three files the game reads.
            NifModel rebuiltMesh = SkeletonExchange.ImportMesh(back, db);

            string rebuiltHavok = Path.Combine(work.Folder, "skeleton.hkx");
            HkxSkeletonFile.Write(havokPath, SkeletonExchange.ImportHavok(back), rebuiltHavok);

            ImportReport imported = ClipExchange.ImportClips(back, project, codec);
            Assert.Empty(imported.Failed);

            work.Cache.Save();

            // 1: the rig, the ragdoll and the mappers, to the byte.
            Assert.Equal(originalHavok, File.ReadAllBytes(rebuiltHavok));

            // 2: the mesh, with the ragdoll it describes.
            Assert.Equal(blocks, rebuiltMesh.Blocks.Count);
            Assert.Equal(constraints, rebuiltMesh.Blocks.Count(
                b => b.Def.Name.EndsWith("Constraint", StringComparison.Ordinal)));

            // 3: the animations.
            Assert.Equal(curves.Count, imported.Clips.Count);

            foreach (ImportedClip clip in imported.Clips)
            {
                SampledAnimation was = curves[clip.StoredName];
                SampledAnimation now = Decoded(clip.Path!, codec);

                Assert.Equal(was.FrameCount, now.FrameCount);
                Assert.Equal(was.TrackCount, now.TrackCount);

                for (int frame = 0; frame < was.FrameCount; frame++)
                for (int track = 0; track < was.TrackCount; track++)
                {
                    BoneTransform a = was[frame, track];
                    BoneTransform b = now[frame, track];

                    Assert.True((a.Translation - b.Translation).Length() < 1e-1,
                        $"{clip.Stack} frame {frame} track {track} moved");

                    Assert.True(
                        Math.Min((a.Rotation - b.Rotation).Length(), (a.Rotation + b.Rotation).Length()) < 1e-2,
                        $"{clip.Stack} frame {frame} track {track} turned");
                }
            }

            // 4: and the cache that addresses them.
            ActorProject after = SkyrimCache.Load(work.Meshes).OpenActor("ChickenProject")!;

            Assert.Equal(slots.Count, after.Animations.Count);

            foreach (AnimationSlot slot in after.Animations)
            {
                (int index, float travel) = slots[slot.StoredName];

                Assert.Equal(index, slot.Index);
                Assert.Equal(travel, slot.Motion?.Travel ?? 0f, 2);
            }
        }

        /// <summary>Two frames of the rig at rest, which is enough to be a stack.</summary>
        private static SampledAnimation Flat(Skeleton rig)
        {
            var transforms = new BoneTransform[2 * rig.Count];

            for (int frame = 0; frame < 2; frame++)
                for (int bone = 0; bone < rig.Count; bone++)
                    transforms[frame * rig.Count + bone] = rig.Bones[bone].ReferencePose;

            return new SampledAnimation
            {
                FrameCount = 2,
                TrackCount = rig.Count,
                Duration = 1f / 30f,
                FrameDuration = 1f / 30f,
                Transforms = transforms,
            };
        }

        /// <summary>
        /// A copy of one creature and of the cache, so a test can write.
        /// </summary>
        private sealed class Workspace : IDisposable
        {
            public Workspace()
            {
                Folder = Path.Combine(Path.GetTempPath(), $"skassets-clips-{Guid.NewGuid():N}");
                Meshes = Path.Combine(Folder, "meshes");

                string havok = Corpus.Havok!;

                Copy(Path.Combine(havok, "actors", "ambient", "chicken"),
                     Path.Combine(Meshes, "actors", "ambient", "chicken"));

                Directory.CreateDirectory(Meshes);

                File.Copy(Path.Combine(havok, SkyrimCache.AnimationDataFileName),
                          Path.Combine(Meshes, SkyrimCache.AnimationDataFileName));

                File.Copy(Path.Combine(havok, SkyrimCache.AnimationSetDataFileName),
                          Path.Combine(Meshes, SkyrimCache.AnimationSetDataFileName));

                Cache = SkyrimCache.Load(Meshes);
            }

            public string Folder { get; }

            public string Meshes { get; }

            public SkyrimCache Cache { get; }

            /// <summary>The creature's own folder inside the copy.</summary>
            public string Creature => Path.Combine(Meshes, "actors", "ambient", "chicken");

            public ActorProject Chicken() =>
                Cache.OpenActor("ChickenProject")
                    ?? throw new InvalidOperationException("no chicken in the cache");

            /// <summary>
            /// The creature's rig as a scene, from its skeleton.hkx.
            /// </summary>
            /// <remarks>
            /// Not through <see cref="SkeletonExchange.Export"/>, which wants the
            /// mesh as well: a clip is entirely a Havok matter, and asking for the
            /// archives to test one would keep this from running anywhere the loose
            /// files are enough.
            /// </remarks>
            public FbxDocument Scene(ActorProject project, out SkeletonFile havok)
            {
                string path = Path.Combine(
                    Meshes, "actors", "ambient", "chicken", "character assets", "skeleton.hkx");

                havok = HkxSkeletonFile.Read(path);
                _ = project;

                return FbxSkeletonWriter.Build(havok);
            }

            private static void Copy(string from, string to)
            {
                Directory.CreateDirectory(to);

                foreach (string file in Directory.GetFiles(from))
                    File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);

                foreach (string folder in Directory.GetDirectories(from))
                    Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
            }

            public void Dispose()
            {
                try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
            }
        }
    }
}
