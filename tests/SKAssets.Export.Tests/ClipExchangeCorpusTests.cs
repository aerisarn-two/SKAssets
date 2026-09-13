using HKFBX.Fbx;
using HKFBX.Hkx;
using HKFBX.Model;
using HKSK.Model;
using LeanMeshIO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using NIFSharp;
using SKAssets.Export;
using Xunit;

namespace SKAssets.Export.Tests
{
    /// <summary>
    /// A creature's whole animation set in one scene, against the shipped game.
    /// </summary>
    /// <remarks>
    /// <b>It does not run unless asked.</b> It wants both halves of the game and they
    /// come from different places — the meshes out of the archives, the Havok cache as
    /// loose files, because the cache is text the game ships inside a BSA and HKSK
    /// reads a folder:
    ///
    /// <code>
    /// SKASSETS_SKYRIM_DATA="/path/to/Data" SKASSETS_HAVOK_MESHES=/path/to/loose/meshes \
    ///     dotnet test --filter "FullyQualifiedName~ClipExchangeCorpus"
    /// </code>
    ///
    /// Decompressing a clip runs Havok's own codec through mopper under Wine, so this
    /// is slow — the two smallest creatures are the subject rather than all 49, and a
    /// full sweep is a separate exercise. They are enough to catch the failure that
    /// matters: a stack that binds no bones is an export that looks like it worked.
    /// </remarks>
    public sealed class ClipExchangeCorpusTests
    {
        /// <summary>
        /// The hare and the chicken: 18 and 20 clips, few enough to decode in a test
        /// and real enough to mean something.
        /// </summary>
        private static readonly string[] Subjects = ["HareProject", "ChickenProject"];

        [HavokCorpusFact]
        public void EveryClipOfACreatureBecomesAStackThatDrivesBones()
        {
            var subjects = Opened();

            foreach ((ActorProject project, SkeletonFile havok, byte[] mesh) in subjects)
            {
                var db = NifXmlDatabase.LoadEmbedded();
                using var stream = new MemoryStream(mesh);

                FbxDocument document = SkeletonExchange.Export(NifModel.Load(stream, db), havok);
                ClipReport report = ClipExchange.AddClips(document, havok.Rig, project);

                Assert.Empty(report.Missing);
                Assert.Empty(report.Unreadable);
                Assert.Equal(project.Animations.Count, report.Stacks.Count);

                // The one that matters: a stack bound to nothing is inert, and an
                // export full of inert stacks looks exactly like one that worked.
                Assert.Empty(report.Inert);

                // Every stack has a name of its own, or a reader cannot tell the
                // clips apart.
                Assert.Equal(
                    report.Stacks.Count,
                    report.Stacks.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            }
        }

        [HavokCorpusFact]
        public void TheClipsSurviveBeingWrittenAndReopened()
        {
            var subjects = Opened();

            string work = Directory.CreateTempSubdirectory("skclips").FullName;

            try
            {
                foreach ((ActorProject project, SkeletonFile havok, byte[] mesh) in subjects)
                {
                    var db = NifXmlDatabase.LoadEmbedded();
                    using var stream = new MemoryStream(mesh);

                    FbxDocument document = SkeletonExchange.Export(NifModel.Load(stream, db), havok);
                    ClipReport report = ClipExchange.AddClips(document, havok.Rig, project);

                    string path = Path.Combine(work, project.Name + ".fbx");
                    document.Save(path);

                    FbxDocument reopened = FbxDocument.Load(path);
                    IReadOnlyList<string> takes = FbxAnimationReader.ReadTakeNames(reopened);

                    Assert.Equal(report.Stacks.Select(s => s.Name), takes);

                    // A clip the cache says travels, because that one has to animate.
                    // Picking the longest instead finds a static hold: the chicken's
                    // longest is 'Idle_Sitd1', 201 frames of a bird sitting still,
                    // and it is 97 constant curves rather than a broken export.
                    ClipStack travelling = report.Stacks.First(s => s.Travels);

                    Assert.True(Moves(reopened, havok.Rig, travelling),
                        $"'{travelling.Name}' travels, yet moves nothing over {travelling.Frames} frames");

                    // And a stack reads back as itself rather than as the scene. Every
                    // clip's curves sit on the same properties of the same nodes, so a
                    // reader that ignores which stack owns them blends the lot: over
                    // the chicken, an unfiltered read of a static clip finds 1,025
                    // moving samples that belong to other clips.
                    ClipStack[] distinct = [.. report.Stacks.Where(s => s.Frames > 1).Take(2)];

                    if (distinct.Length == 2)
                    {
                        SampledAnimation first = Read(reopened, havok.Rig, distinct[0]);
                        SampledAnimation second = Read(reopened, havok.Rig, distinct[1]);

                        int differing = 0;
                        int frames = Math.Min(first.FrameCount, second.FrameCount);

                        for (int frame = 0; frame < frames; frame++)
                            for (int bone = 0; bone < havok.Rig.Count; bone++)
                                if (first[frame, bone] != second[frame, bone])
                                    differing++;

                        Assert.True(differing > 0,
                            $"'{distinct[0].Name}' and '{distinct[1].Name}' read back identical");
                    }
                }
            }
            finally { Directory.Delete(work, recursive: true); }
        }

        [HavokCorpusFact]
        public void AStackSaysWhichAnimationItCameFrom()
        {
            var subjects = Opened();

            (ActorProject project, SkeletonFile havok, byte[] mesh) = subjects[0];

            var db = NifXmlDatabase.LoadEmbedded();
            using var stream = new MemoryStream(mesh);

            FbxDocument document = SkeletonExchange.Export(NifModel.Load(stream, db), havok);
            ClipReport report = ClipExchange.AddClips(document, havok.Rig, project);

            var byName = new Dictionary<string, FbxObject>(StringComparer.Ordinal);

            foreach (FbxObject stack in new FbxScene(document).OfClass("AnimationStack"))
                byName[stack.Name] = stack;

            foreach (ClipStack clip in report.Stacks)
            {
                FbxObject stack = byName[clip.Name];

                // A stem is not enough to put a clip back: the project stores a path
                // and addresses the animation by index.
                Assert.Equal(clip.StoredName, stack.Properties.GetString(ClipExchange.StoredNameProperty));
                Assert.Equal(clip.CacheIndex, (int)stack.Properties.GetDouble(ClipExchange.CacheIndexProperty));
            }
        }

        /// <summary>Whether any frame of a clip differs from its first.</summary>
        /// <remarks>
        /// Against frame zero rather than against the last frame: a looping clip ends
        /// where it started, so first-versus-last is the one comparison guaranteed to
        /// find nothing on exactly the clips that do loop.
        /// </remarks>
        private static bool Moves(FbxDocument document, Skeleton rig, ClipStack clip)
        {
            SampledAnimation back = Read(document, rig, clip);

            for (int frame = 1; frame < back.FrameCount; frame++)
                for (int bone = 0; bone < rig.Count; bone++)
                    if (back[frame, bone] != back[0, bone])
                        return true;

            return false;
        }

        private static SampledAnimation Read(FbxDocument document, Skeleton rig, ClipStack clip) =>
            FbxAnimationReader.ReadAnimation(
                document, rig, frameCount: clip.Frames, frameDuration: 1f / 30f, takeName: clip.Name);

        /// <summary>
        /// The subjects, opened once: the project out of the cache, the rig out of its
        /// skeleton.hkx, and the mesh out of the archives.
        /// </summary>
        private static IReadOnlyList<(ActorProject Project, SkeletonFile Havok, byte[] Mesh)> Opened()
        {
            // The attribute has already skipped the test when this is absent.
            string loose = Corpus.Havok!;

            lock (Gate)
            {
                if (_cached is not null)
                    return _cached;

                var meshes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

                // The mesh beside the rig, where the tree has it. A creature's two
                // halves ship in different archives, so an extracted tree holding
                // both is the easier corpus to have and the archives are the
                // fallback that made it.
                foreach (string folder in Corpus.ExtractedCreatures())
                {
                    string relative = Path.GetRelativePath(loose, folder)
                        .Replace('\\', '/').ToLowerInvariant();

                    meshes[$"meshes/{relative}/skeleton.nif"] =
                        File.ReadAllBytes(Path.Combine(folder, "skeleton.nif"));
                }

                foreach (string archive in Directory.GetFiles(Corpus.Data ?? loose, "*.bsa"))
                {
                    // An archive this test cannot read is not its subject.
                    try
                    {
                        foreach (var entry in Archive.CreateReader(GameRelease.SkyrimSE, archive).Files)
                        {
                            string path = entry.Path.Replace('\\', '/').ToLowerInvariant();

                            if (path.EndsWith("/skeleton.nif", StringComparison.Ordinal))
                                meshes.TryAdd(path, entry.GetBytes());
                        }
                    }
                    catch (Exception e) when (e is not OutOfMemoryException) { }
                }

                SkyrimCache cache = SkyrimCache.Load(loose);
                var opened = new List<(ActorProject, SkeletonFile, byte[])>();

                foreach (ActorProject project in cache.Actors())
                {
                    if (!Subjects.Contains(project.Name, StringComparer.OrdinalIgnoreCase))
                        continue;

                    Assert.NotNull(project.SkeletonPath);

                    string folder = Path.GetDirectoryName(project.SkeletonPath)!;
                    string relative = Path.GetRelativePath(loose, folder).Replace('\\', '/').ToLowerInvariant();

                    Assert.True(
                        meshes.TryGetValue($"meshes/{relative}/skeleton.nif", out byte[]? mesh),
                        $"no skeleton.nif beside or in the archives for meshes/{relative}");

                    opened.Add((project, HkxSkeletonFile.Read(project.SkeletonPath!), mesh!));
                }

                Assert.Equal(Subjects.Length, opened.Count);

                return _cached = opened;
            }
        }

        private static IReadOnlyList<(ActorProject Project, SkeletonFile Havok, byte[] Mesh)>? _cached;
        private static readonly Lock Gate = new();
    }
}
