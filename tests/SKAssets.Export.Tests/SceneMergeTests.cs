using LeanMeshIO;
using LeanMeshIO.Formats.Fbx;
using SKAssets.Export.Fbx;
using Xunit;

namespace SKAssets.Export.Tests
{
    /// <summary>
    /// Folding one FBX document into another.
    /// </summary>
    /// <remarks>
    /// The documents here are built by hand rather than converted from a mesh, for
    /// the reason the rest of this repository's tests describe meshes as profiles:
    /// a test that needs a NIF needs a game install, and then it stops running.
    /// What is being checked is the join — which objects are reused, which are
    /// copied, and whether the ids survive — and that needs no geometry at all.
    /// </remarks>
    public class SceneMergeTests
    {
        /// <summary>
        /// A document with a two-bone skeleton and whatever extras are asked for,
        /// each extra connected to the first bone.
        /// </summary>
        private static FbxDocument Scene(long firstId, params string[] extras)
        {
            var document = new FbxDocument();
            document.Nodes.Add(Header());

            var objects = new FbxNode { Name = "Objects" };
            var connections = new FbxNode { Name = "Connections" };

            long id = firstId;

            FbxNode Bone(string name)
            {
                var node = new FbxNode { Name = "Model", Properties = { id++, $"Model::{name}", "LimbNode" } };
                objects.Nodes.Add(node);
                return node;
            }

            FbxNode root = Bone("Root");
            FbxNode child = Bone("Spine");

            connections.Nodes.Add(new FbxNode
            {
                Name = "C",
                Properties = { "OO", Id(child), Id(root) },
            });

            foreach (string extra in extras)
            {
                var node = new FbxNode { Name = "Geometry", Properties = { id++, $"Geometry::{extra}", "Mesh" } };
                objects.Nodes.Add(node);
                connections.Nodes.Add(new FbxNode
                {
                    Name = "C",
                    Properties = { "OO", Id(node), Id(root) },
                });
            }

            document.Nodes.Add(objects);
            document.Nodes.Add(connections);
            return document;
        }

        /// <summary>
        /// The least a document needs to be writable: the binary writer derives its
        /// file id from the creation timestamp and throws without one. A document
        /// from NifToFbx has this already; one built by hand has to say so.
        /// </summary>
        private static FbxNode Header()
        {
            DateTime now = DateTime.UtcNow;
            var stamp = new FbxNode { Name = "CreationTimeStamp" };

            foreach ((string field, int value) in new[]
                     {
                         ("Version", 1000), ("Year", now.Year), ("Month", now.Month),
                         ("Day", now.Day), ("Hour", now.Hour), ("Minute", now.Minute),
                         ("Second", now.Second), ("Millisecond", now.Millisecond),
                     })
            {
                stamp.Nodes.Add(new FbxNode { Name = field, Properties = { value } });
            }

            return new FbxNode { Name = "FBXHeaderExtension", Nodes = { stamp } };
        }

        private static long Id(FbxNode node) => Convert.ToInt64(node.Properties[0]);

        private static List<FbxNode> Objects(FbxDocument document) =>
            document["Objects"]!.Nodes;

        private static List<FbxNode> Connections(FbxDocument document) =>
            document["Connections"]!.Nodes;

        private static string NameOf(FbxNode node)
        {
            string qualified = node.Properties[1] as string ?? string.Empty;
            int at = qualified.IndexOf("::", StringComparison.Ordinal);
            return at >= 0 ? qualified[(at + 2)..] : qualified;
        }

        [Fact]
        public void ABoneTheTargetAlreadyHasIsReusedRatherThanDuplicated()
        {
            FbxDocument target = Scene(1000, "body");
            FbxDocument source = Scene(1000, "helmet");

            MergeReport report = new SceneMerge(target).Add(source);

            Assert.Equal(2, report.Reused);
            Assert.Equal(1, report.Added);

            // One skeleton, not two: the second document's bones were dropped and
            // its geometry hung on the first document's.
            Assert.Equal(2, Objects(target).Count(n => n.Name == "Model"));
            Assert.Equal(2, Objects(target).Count(n => n.Name == "Geometry"));
        }

        [Fact]
        public void TheIncomingGeometryEndsUpOnTheTargetsOwnBone()
        {
            FbxDocument target = Scene(1000, "body");
            FbxDocument source = Scene(1000, "helmet");

            long targetRoot = Id(Objects(target).First(n => NameOf(n) == "Root"));

            new SceneMerge(target).Add(source);

            FbxNode helmet = Objects(target).First(n => NameOf(n) == "helmet");
            FbxNode link = Connections(target).First(c => Convert.ToInt64(c.Properties[1]) == Id(helmet));

            Assert.Equal(targetRoot, Convert.ToInt64(link.Properties[2]));
        }

        [Fact]
        public void EveryIdStaysUniqueEvenWhenTheDocumentsOverlapped()
        {
            // Both scenes number from 1000, so every id in one collides with one in
            // the other. That is the normal case, not a contrived one: each library
            // starts its own numbering.
            FbxDocument target = Scene(1000, "body", "cloak");
            FbxDocument source = Scene(1000, "helmet", "boots");

            new SceneMerge(target).Add(source);

            var ids = Objects(target).Select(Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void ADanglingConnectionIsDroppedAndNamed()
        {
            FbxDocument target = Scene(1000);
            FbxDocument source = Scene(1000, "helmet");

            // A curve bound to a bone that does not exist anywhere -- what a clip
            // for a different creature looks like.
            Connections(source).Add(new FbxNode
            {
                Name = "C",
                Properties = { "OO", 999_999L, 888_888L },
            });

            MergeReport report = new SceneMerge(target).Add(source);

            Assert.Equal(1, report.Dropped);
            Assert.NotEmpty(report.UnboundNames);
        }

        [Fact]
        public void MergingSeveralSourcesKeepsOneSkeleton()
        {
            FbxDocument target = Scene(1000, "body");
            var merge = new SceneMerge(target);

            foreach (string piece in new[] { "helmet", "boots", "gauntlets", "shield" })
                merge.Add(Scene(1000, piece));

            Assert.Equal(2, Objects(target).Count(n => n.Name == "Model"));
            Assert.Equal(5, Objects(target).Count(n => n.Name == "Geometry"));

            var ids = Objects(target).Select(Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void TheDefinitionsCountsMatchWhatIsThere()
        {
            FbxDocument target = Scene(1000, "body");
            new SceneMerge(target).Add(Scene(1000, "helmet"));

            FbxNode definitions = target["Definitions"]!;

            Assert.Equal(Objects(target).Count,
                Convert.ToInt32(definitions.Nodes.First(n => n.Name == "Count").Properties[0]));

            FbxNode models = definitions.Nodes.First(
                n => n.Name == "ObjectType" && (string)n.Properties[0]! == "Model");

            Assert.Equal(2, Convert.ToInt32(models.Nodes.First(n => n.Name == "Count").Properties[0]));
        }

        /// <summary>
        /// Definitions has to come before Objects, because a reader sizes its
        /// tables from it before it reaches them.
        /// </summary>
        [Fact]
        public void DefinitionsPrecedesObjects()
        {
            FbxDocument target = Scene(1000, "body");
            new SceneMerge(target).Add(Scene(1000, "helmet"));

            int definitions = target.Nodes.FindIndex(n => n.Name == "Definitions");
            int objects = target.Nodes.FindIndex(n => n.Name == "Objects");

            Assert.True(definitions >= 0 && definitions < objects,
                $"Definitions at {definitions}, Objects at {objects}");
        }

        [Fact]
        public void TheMergedDocumentSurvivesBeingWrittenAndReadBack()
        {
            FbxDocument target = Scene(1000, "body");
            new SceneMerge(target).Add(Scene(1000, "helmet"));

            string path = Path.Combine(Path.GetTempPath(), $"skmerge{Guid.NewGuid():N}.fbx");

            try
            {
                target.Save(path);
                FbxDocument reloaded = FbxDocument.Load(path);

                Assert.Equal(Objects(target).Count, Objects(reloaded).Count);
                Assert.Equal(Connections(target).Count, Connections(reloaded).Count);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
