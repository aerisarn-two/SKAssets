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
        /// The mesh half keeps its ragdoll, and reading it does not spoil the Havok
        /// half read after it.
        /// </summary>
        /// <remarks>
        /// A creature's constraints live in both files and both are read from the
        /// same two FBX properties, so the export has to choose a vocabulary and it
        /// chooses Havok's. A mesh rebuilt from a scene still in that vocabulary
        /// finds no node by any of those names and quietly drops every constraint it
        /// has — the cow came back with 124 blocks where it has 147, missing all 12
        /// hinges and all 11 ragdoll constraints. Nothing else about the file
        /// changes, which is why it went unnoticed.
        ///
        /// The second half of the assertion is the other side of that fix: the mesh
        /// spelling is put back only for the length of the conversion, so a document
        /// the Havok half is read from afterwards must still round-trip to the byte.
        /// </remarks>
        [MeshCorpusFact]
        public void AMeshThroughAnFbxKeepsItsConstraints()
        {
            IReadOnlyList<Pair> pairs = Pairs();

            string work = Directory.CreateTempSubdirectory("skmesh").FullName;
            var db = NifXmlDatabase.LoadEmbedded();

            try
            {
                var lost = new List<string>();
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

                    FbxDocument document = FbxDocument.Load(scene);
                    NifModel rebuilt = SkeletonExchange.ImportMesh(document, db);

                    if (Constraints(rebuilt) != Constraints(mesh))
                        lost.Add($"{pair.Folder}: {Constraints(rebuilt)} of {Constraints(mesh)}");

                    // Same document, Havok read after the mesh.
                    string trip = Path.Combine(work, "trip.hkx");
                    HkxSkeletonFile.Write(source, SkeletonExchange.ImportHavok(document), trip);

                    if (!File.ReadAllBytes(trip).AsSpan().SequenceEqual(pair.Havok))
                        changed.Add(pair.Folder);
                }

                Assert.Empty(lost);
                Assert.Empty(changed);
            }
            finally { Directory.Delete(work, recursive: true); }
        }

        /// <summary>How many constraint blocks a mesh holds.</summary>
        private static int Constraints(NifModel model) =>
            model.Blocks.Count(b => b.Def.Name.EndsWith("Constraint", StringComparison.Ordinal));

        /// <summary>The pairs an extracted tree already holds side by side.</summary>
        private static IReadOnlyList<Pair> FromFolders(IReadOnlyList<string> folders)
        {
            var pairs = new List<Pair>();
            string work = Directory.CreateTempSubdirectory("skpairs").FullName;

            try
            {
                foreach (string folder in folders)
                {
                    string havok = Path.Combine(folder, "skeleton.hkx");

                    // A skeleton with no bodies has no ragdoll, and the ragdoll is
                    // what this exercises.
                    try
                    {
                        if (HkxSkeletonFile.Read(havok).Bodies.Count == 0)
                            continue;
                    }
                    catch (Exception e) when (e is not OutOfMemoryException) { continue; }

                    pairs.Add(new Pair(
                        folder,
                        File.ReadAllBytes(Path.Combine(folder, "skeleton.nif")),
                        File.ReadAllBytes(havok)));
                }
            }
            finally { Directory.Delete(work, recursive: true); }

            Assert.True(pairs.Count >= 45, $"only {pairs.Count} creatures carried a ragdoll");

            return pairs;
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
            lock (Gate)
            {
                if (_cached is not null)
                    return _cached;

                // An extracted tree first, where there is one: both halves of a
                // creature are already side by side in it, and it does not need the
                // drive holding the game to be plugged in. The archives are the
                // fallback and the original source of that tree.
                if (Corpus.ExtractedCreatures() is { Count: > 0 } folders)
                    return _cached = FromFolders(folders);

                // The attribute has already skipped the test when this is absent.
                string data = Corpus.Data!;

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
