using HKFBX.Fbx;
using HKFBX.Hkx;
using HKFBX.Model;
using HKSK.Cache;
using HKSK.Model;
using LeanMeshIO;
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
