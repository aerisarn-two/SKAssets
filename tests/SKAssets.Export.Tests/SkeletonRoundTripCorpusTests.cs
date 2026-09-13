using HKFBX.Hkx;
using HKFBX.Model;
using LeanMeshIO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using NIFSharp;
using Xunit;

namespace SKAssets.Export.Tests
{
    /// <summary>
    /// Every creature that ships both of its skeleton files, out to one FBX and back
    /// to the byte.
    /// </summary>
    /// <remarks>
    /// <b>It does not run unless asked.</b> Without <c>SKASSETS_SKYRIM_DATA</c> the
    /// tests pass vacuously, because the archives are not redistributable:
    ///
    /// <code>
    /// SKASSETS_SKYRIM_DATA="/path/to/Data" dotnet test --filter "FullyQualifiedName~RoundTripCorpus"
    /// </code>
    ///
    /// The assertion is exact and deliberately so. A skeleton.hkx that comes back
    /// with one bit changed is a skeleton the game may still load and the ragdoll
    /// may still behave differently in, and there is no threshold below which that
    /// is acceptable — so the test is not "close enough", it is the same file. The
    /// two halves are separate on purpose: the control says the Havok library can
    /// hand a file back unchanged, and the trip says nothing was lost between the
    /// two. When they fail together the fault is below this library.
    /// </remarks>
    public sealed class SkeletonRoundTripCorpusTests
    {
        private sealed record Pair(string Folder, byte[] Mesh, byte[] Havok);

        private static IReadOnlyList<Pair>? _cached;
        private static readonly Lock Gate = new();

        [MeshCorpusFact]
        public void ReadingAndWritingAHavokSkeletonChangesNothing()
        {
            IReadOnlyList<Pair> pairs = Pairs();

            string work = Directory.CreateTempSubdirectory("skcontrol").FullName;

            try
            {
                var changed = new List<string>();

                foreach (Pair pair in pairs)
                {
                    string source = Path.Combine(work, "source.hkx");
                    string written = Path.Combine(work, "control.hkx");
                    File.WriteAllBytes(source, pair.Havok);

                    HkxSkeletonFile.Write(source, HkxSkeletonFile.Read(source), written);

                    if (!File.ReadAllBytes(written).AsSpan().SequenceEqual(pair.Havok))
                        changed.Add(pair.Folder);
                }

                Assert.Empty(changed);
            }
            finally { Directory.Delete(work, recursive: true); }
        }

        [MeshCorpusFact]
        public void ASkeletonThroughAnFbxComesBackAsTheSameFile()
        {
            IReadOnlyList<Pair> pairs = Pairs();

            string work = Directory.CreateTempSubdirectory("sktrip").FullName;
            var db = NifXmlDatabase.LoadEmbedded();

            try
            {
                var changed = new List<string>();

                foreach (Pair pair in pairs)
                {
                    string source = Path.Combine(work, "source.hkx");
                    File.WriteAllBytes(source, pair.Havok);

                    SkeletonFile havok = HkxSkeletonFile.Read(source);

                    using var stream = new MemoryStream(pair.Mesh);
                    NifModel mesh = NifModel.Load(stream, db);

                    string scene = Path.Combine(work, "scene.fbx");
                    SkeletonExchange.Export(mesh, havok).Save(scene);

                    string rebuilt = Path.Combine(work, "trip.hkx");
                    HkxSkeletonFile.Write(source, SkeletonExchange.ImportHavok(FbxDocument.Load(scene)), rebuilt);

                    if (!File.ReadAllBytes(rebuilt).AsSpan().SequenceEqual(pair.Havok))
                        changed.Add(pair.Folder);
                }

                Assert.Empty(changed);
            }
            finally { Directory.Delete(work, recursive: true); }
        }

        /// <summary>
        /// Every folder holding both a skeleton.nif and a skeleton.hkx with a
        /// ragdoll in it — 45 of them in the shipped game.
        /// </summary>
        /// <remarks>
        /// Read once and shared. Both tests want the same bytes and opening every
        /// archive twice costs more than holding two megabytes of skeletons.
        /// </remarks>
        private static IReadOnlyList<Pair> Pairs()
        {
            // The attribute has already skipped the test when this is absent.
            string data = Corpus.Data!;

            lock (Gate)
            {
                if (_cached is not null)
                    return _cached;

                var meshes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                var rigs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

                foreach (string archive in Directory.GetFiles(data, "*.bsa"))
                {
                    // A malformed or unreadable archive is not this test's subject.
                    try
                    {
                        foreach (var entry in Archive.CreateReader(GameRelease.SkyrimSE, archive).Files)
                        {
                            string path = entry.Path.Replace('\\', '/').ToLowerInvariant();
                            string folder = path[..Math.Max(0, path.LastIndexOf('/'))];

                            if (path.EndsWith("/skeleton.nif", StringComparison.Ordinal))
                                meshes[folder] = entry.GetBytes();
                            else if (path.EndsWith("/skeleton.hkx", StringComparison.Ordinal))
                                rigs[folder] = entry.GetBytes();
                        }
                    }
                    catch (Exception e) when (e is not OutOfMemoryException) { }
                }

                var pairs = new List<Pair>();
                string work = Directory.CreateTempSubdirectory("skpairs").FullName;

                try
                {
                    foreach (string folder in meshes.Keys.Where(rigs.ContainsKey).OrderBy(x => x, StringComparer.Ordinal))
                    {
                        string probe = Path.Combine(work, "probe.hkx");
                        File.WriteAllBytes(probe, rigs[folder]);

                        // A skeleton with no bodies has no ragdoll, and the ragdoll
                        // is what this exercises.
                        try
                        {
                            if (HkxSkeletonFile.Read(probe).Bodies.Count == 0)
                                continue;
                        }
                        catch (Exception e) when (e is not OutOfMemoryException) { continue; }

                        pairs.Add(new Pair(folder, meshes[folder], rigs[folder]));
                    }
                }
                finally { Directory.Delete(work, recursive: true); }

                Assert.Equal(45, pairs.Count);

                return _cached = pairs;
            }
        }
    }
}
