using LeanMeshIO;
using LeanMeshIO.Formats.Fbx;

namespace SKAssets.Export.Fbx
{
    /// <summary>
    /// What a merge did, and what it could not do.
    /// </summary>
    /// <param name="Added">Objects brought across from the source.</param>
    /// <param name="Reused">
    /// Source objects not copied because the target already had a node of that
    /// name — the bones an animation binds to, above all.
    /// </param>
    /// <param name="Dropped">
    /// Connections whose ends did not both survive. A curve bound to a bone the
    /// target does not have is the usual cause, and the usual cause of that is two
    /// files disagreeing about a bone name.
    /// </param>
    /// <param name="UnboundNames">The names those dropped connections wanted.</param>
    public sealed record MergeReport(
        int Added, int Reused, int Dropped, IReadOnlyList<string> UnboundNames)
    {
        public override string ToString() =>
            $"{Added} added, {Reused} reused, {Dropped} dropped"
            + (UnboundNames.Count == 0 ? "" : $" ({string.Join(", ", UnboundNames.Take(4))})");
    }

    /// <summary>
    /// Folds one FBX document into another.
    /// </summary>
    /// <remarks>
    /// The primitive a complete export needs and none of the three libraries has,
    /// because each builds a whole document of its own: <c>NifToFbx</c> makes one
    /// from a mesh, <c>FbxAnimationWriter.Build</c> makes one per Havok clip, and
    /// neither can write into a document somebody else started. An asset made of a
    /// mesh, four skins and two hundred clips is two hundred and five documents
    /// that have to become one file.
    ///
    /// The whole difficulty is the skeleton. Every one of those documents carries
    /// its own copy of the same bones, and concatenating them produces an FBX with
    /// two hundred and five skeletons in it. So a node is matched **by name**
    /// against the target: where the target already has it the source's copy is
    /// dropped, and everything that pointed at it is pointed at the target's copy
    /// instead. That is what binds a Havok clip to the mesh's own bones, and it is
    /// why the two files having the same bone names is not a nicety — it is the
    /// join.
    ///
    /// Source nodes are moved rather than copied, so a document is spent by a merge
    /// and must not be used again.
    /// </remarks>
    public sealed class SceneMerge
    {
        private readonly FbxDocument _target;
        private readonly FbxNode _objects;
        private readonly FbxNode _connections;
        private readonly Dictionary<string, long> _shared = new(StringComparer.Ordinal);
        private long _nextId;

        /// <summary>Starts a merge into <paramref name="target"/>, modified in place.</summary>
        public SceneMerge(FbxDocument target)
        {
            ArgumentNullException.ThrowIfNull(target);

            _target = target;
            _objects = Section(target, "Objects");
            _connections = Section(target, "Connections");

            foreach (FbxNode node in _objects.Nodes)
            {
                long id = IdOf(node);
                _nextId = Math.Max(_nextId, id);

                if (IsShared(node))
                    _shared.TryAdd(NameOf(node), id);
            }

            _nextId++;
        }

        /// <summary>The document being merged into.</summary>
        public FbxDocument Document => _target;

        /// <summary>
        /// Folds <paramref name="source"/> in, reusing any node whose name the
        /// target already has.
        /// </summary>
        public MergeReport Add(FbxDocument source)
        {
            ArgumentNullException.ThrowIfNull(source);

            FbxNode incoming = Section(source, "Objects");
            FbxNode links = Section(source, "Connections");

            var remap = new Dictionary<long, long> { [0] = 0 };
            var names = new Dictionary<long, string>();
            int added = 0, reused = 0;

            foreach (FbxNode node in incoming.Nodes)
            {
                long id = IdOf(node);
                string name = NameOf(node);
                names[id] = name;

                if (IsShared(node) && _shared.TryGetValue(name, out long existing))
                {
                    remap[id] = existing;
                    reused++;
                    continue;
                }

                long fresh = _nextId++;
                remap[id] = fresh;

                if (node.Properties.Count > 0)
                    node.Properties[0] = fresh;

                _objects.Nodes.Add(node);

                if (IsShared(node))
                    _shared.TryAdd(name, fresh);

                added++;
            }

            int dropped = 0;
            var unbound = new List<string>();

            foreach (FbxNode link in links.Nodes)
            {
                if (link.Properties.Count < 3)
                    continue;

                long from = Convert.ToInt64(link.Properties[1]);
                long to = Convert.ToInt64(link.Properties[2]);

                if (!remap.TryGetValue(from, out long source_) || !remap.TryGetValue(to, out long destination))
                {
                    dropped++;
                    string name = names.GetValueOrDefault(to) ?? names.GetValueOrDefault(from) ?? "?";
                    if (!unbound.Contains(name))
                        unbound.Add(name);
                    continue;
                }

                link.Properties[1] = source_;
                link.Properties[2] = destination;
                _connections.Nodes.Add(link);
            }

            RefreshDefinitions();

            return new MergeReport(added, reused, dropped, unbound);
        }

        /// <summary>
        /// Only nodes are matched by name.
        /// </summary>
        /// <remarks>
        /// Two meshes may legitimately be called the same thing in different files
        /// and two materials certainly are, so matching everything by name would
        /// weld unrelated objects together. Bones are the things that have to be one
        /// object, because an animation that binds to a second copy of a skeleton
        /// drives nothing anyone can see.
        /// </remarks>
        private static bool IsShared(FbxNode node)
        {
            if (node.Name != "Model" || node.Properties.Count < 3)
                return false;

            string subClass = node.Properties[2] as string ?? string.Empty;
            return subClass is "LimbNode" or "Null" or "Root";
        }

        private static long IdOf(FbxNode node) =>
            node.Properties.Count > 0 ? Convert.ToInt64(node.Properties[0]) : 0;

        private static string NameOf(FbxNode node)
        {
            string qualified = node.Properties.Count > 1
                ? node.Properties[1] as string ?? string.Empty
                : string.Empty;

            int separator = qualified.IndexOf("::", StringComparison.Ordinal);
            return separator >= 0 ? qualified[(separator + 2)..] : qualified;
        }

        private static FbxNode Section(FbxDocument document, string name)
        {
            FbxNode? found = document[name];

            if (found is null)
            {
                found = new FbxNode { Name = name };
                document.Nodes.Add(found);
            }

            return found;
        }

        /// <summary>
        /// Rewrites the object counts a reader sizes its tables from.
        /// </summary>
        /// <remarks>
        /// A reader that meets more objects than Definitions promised either stops
        /// early or reallocates, and neither is something to leave to chance in a
        /// file that has just grown by two hundred takes.
        /// </remarks>
        private void RefreshDefinitions()
        {
            FbxNode definitions = Section(_target, "Definitions");

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (FbxNode node in _objects.Nodes)
                counts[node.Name] = counts.GetValueOrDefault(node.Name) + 1;

            definitions.Nodes.RemoveAll(n => n.Name == "Count");
            definitions.Nodes.Insert(0, new FbxNode { Name = "Count", Properties = { _objects.Nodes.Count } });

            foreach ((string className, int count) in counts)
            {
                FbxNode? type = definitions.Nodes.FirstOrDefault(
                    n => n.Name == "ObjectType" && (n.Properties.FirstOrDefault() as string) == className);

                if (type is null)
                {
                    type = new FbxNode { Name = "ObjectType", Properties = { className } };
                    definitions.Nodes.Add(type);
                }

                type.Nodes.RemoveAll(n => n.Name == "Count");
                type.Nodes.Insert(0, new FbxNode { Name = "Count", Properties = { count } });
            }

            // Definitions has to precede Objects: a reader sizes its tables from it
            // before it reaches them.
            int at = _target.Nodes.IndexOf(definitions);
            int objects = _target.Nodes.IndexOf(_objects);

            if (at > objects && objects >= 0)
            {
                _target.Nodes.RemoveAt(at);
                _target.Nodes.Insert(objects, definitions);
            }
        }
    }
}
