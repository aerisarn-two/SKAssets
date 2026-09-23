using System.Numerics;
using HKFBX.Model;
using LeanMeshIO;
using NIFSharp;
using SKAssets.Content.Assets;

namespace SKAssets.Authoring
{
    /// <summary>
    /// Whether a creature's files agree with each other: the skeleton NIF with the Havok rig
    /// the animations drive, the body's skin with the skeleton it is worn on, and the body
    /// with the FBX it was made from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each file is written by its own converter from its own reading of the FBX, and a
    /// disagreement between readers produces files that are each correct on their own. The
    /// one that prompted this: Blender's factory scene is metric, and its exporter writes the
    /// metre as a scale of 100 on every root node. HKFBX reads a root's bones and not its
    /// scale; NIFBX applies it. The skeleton.hkx and every clip came out right, and the
    /// skeleton.nif and the skin 100 times too large -- five files, no error.
    /// </para>
    /// <para>
    /// So the checks compare what each file means in the world: where every bone stands
    /// according to the NIF and according to the Havok rig; where the skin was bound to each
    /// bone and where the skeleton puts it; how many triangles the source had and how many
    /// the skin holds, and whether each one names vertices that exist.
    /// </para>
    /// </remarks>
    public static class CreatureChecks
    {
        /// <summary>How far a bone may stand from where another file puts it, in game units.</summary>
        public const float Tolerance = 0.05f;

        /// <summary>Every named node's rest transform in the NIF's world.</summary>
        public static Dictionary<string, Matrix4x4> NodeWorld(NifModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            var world = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
            if (model.Blocks.Count == 0) return world;

            void Walk(NifItem node, Matrix4x4 parent, int depth)
            {
                Matrix4x4 at = model.GetTransform(node).ToMatrix() * parent;
                world.TryAdd(model.GetName(node), at);
                if (depth < 256 && model.BlockInherits(node, "NiNode"))
                    foreach (NifItem child in model.GetChildren(node))
                        Walk(child, at, depth + 1);
            }

            Walk(model.Blocks[0], Matrix4x4.Identity, 0);
            return world;
        }

        /// <summary>The skeleton NIF's bones against the Havok rig's, bone by bone.</summary>
        public static IReadOnlyList<MeshFinding> Skeleton(NifModel skeleton, SkeletonFile havok)
        {
            ArgumentNullException.ThrowIfNull(skeleton);
            ArgumentNullException.ThrowIfNull(havok);

            var nif = NodeWorld(skeleton);
            var rig = havok.Rig;
            var world = new Matrix4x4[rig.Count];
            var pairs = new List<(string Bone, Vector3 Nif, Vector3 Havok)>();

            for (int i = 0; i < rig.Count; i++)
            {
                BoneTransform pose = rig.Bones[i].ReferencePose;
                Matrix4x4 local = Matrix4x4.CreateScale(pose.Scale) * Matrix4x4.CreateFromQuaternion(pose.Rotation)
                    * Matrix4x4.CreateTranslation(pose.Translation);
                int parent = rig.Bones[i].ParentIndex;
                world[i] = parent >= 0 && parent < i ? local * world[parent] : local;

                if (nif.TryGetValue(rig.Bones[i].Name, out Matrix4x4 node))
                    pairs.Add((rig.Bones[i].Name, node.Translation, world[i].Translation));
            }

            var findings = new List<MeshFinding>();
            int missing = rig.Count - pairs.Count;
            if (missing > 0)
                findings.Add(new MeshFinding("skeleton-bones", FindingSeverity.Warning,
                    $"{missing} of the Havok rig's {rig.Count} bones have no node in the skeleton NIF"));

            findings.AddRange(Compare("skeleton-placement", "the skeleton NIF", "the Havok rig", pairs));
            return findings;
        }

        /// <summary>
        /// Every skinned shape's bind pose against the skeleton it is worn on: where the skin
        /// was bound to each bone and where the skeleton puts that bone.
        /// </summary>
        public static IReadOnlyList<MeshFinding> Skin(NifModel body, NifModel skeleton)
        {
            ArgumentNullException.ThrowIfNull(body);
            ArgumentNullException.ThrowIfNull(skeleton);

            var bones = NodeWorld(skeleton);
            var pairs = new List<(string Bone, Vector3 Nif, Vector3 Havok)>();
            var findings = new List<MeshFinding>();
            var absent = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (NifItem shape in body.Blocks.Where(IsShape))
            {
                if (body.GetRef(shape, "Skin") is not { } skin || body.GetRef(skin, "Data") is not { } data) continue;

                Matrix4x4 overall = body.FindItem(data, "Skin Transform") is { } o ? body.GetTransform(o).ToMatrix() : Matrix4x4.Identity;
                Matrix4x4.Invert(overall, out Matrix4x4 overallInverse);
                var boneList = body.FindItem(data, "Bone List")?.Children ?? [];
                var boneNodes = body.GetRefArray(skin, "Bones").ToList();

                for (int i = 0; i < boneNodes.Count && i < boneList.Count; i++)
                {
                    string name = body.GetName(boneNodes[i]);
                    if (!bones.TryGetValue(name, out Matrix4x4 standing)) { absent.Add(name); continue; }
                    if (body.FindItem(boneList[i], "Skin Transform") is not { } st) continue;
                    Matrix4x4.Invert(body.GetTransform(st).ToMatrix(), out Matrix4x4 bind);
                    pairs.Add((name, standing.Translation, (bind * overallInverse).Translation));
                }
            }

            if (absent.Count > 0)
                findings.Add(new MeshFinding("skin-bones", FindingSeverity.Error,
                    $"the skin is bound to {absent.Count} bones the skeleton does not have: {string.Join(", ", absent.Take(8))}"));

            findings.AddRange(Compare("skin-bind", "the skeleton", "the skin's bind pose", pairs));
            return findings;
        }

        /// <summary>
        /// The body's triangles against the FBX's: as many of them, and each naming three
        /// distinct vertices the shape has.
        /// </summary>
        public static IReadOnlyList<MeshFinding> Triangles(NifModel body, string fbx)
        {
            ArgumentNullException.ThrowIfNull(body);
            ArgumentException.ThrowIfNullOrWhiteSpace(fbx);

            int source = 0;
            foreach (var geometry in new NIFBX.Fbx.FbxScene(FbxDocument.Load(fbx)).OfClass("Geometry", "Mesh"))
            {
                if (geometry.Node.Nodes.FirstOrDefault(n => n.Name == "PolygonVertexIndex")?.Properties.FirstOrDefault() is not int[] indices) continue;
                int corners = 0;
                foreach (int index in indices)
                {
                    corners++;
                    if (index < 0) { source += Math.Max(0, corners - 2); corners = 0; }
                }
            }

            int written = 0, invalid = 0, degenerate = 0;
            foreach (NifItem shape in body.Blocks.Where(IsShape))
            {
                NifItem? partition = body.GetRef(shape, "Skin") is { } skin ? body.GetRef(skin, "Skin Partition") : null;

                if (partition is null)
                {
                    written += (int)(body.FindItem(shape, "Num Triangles")?.Value.ToUInt() ?? 0);
                    continue;
                }

                int vertices = body.FindItem(partition, "Vertex Data")?.Children.Count
                    ?? (int)(body.FindItem(shape, "Num Vertices")?.Value.ToUInt() ?? 0);

                foreach (NifItem part in body.FindItem(partition, "Partitions")?.Children ?? [])
                {
                    var map = body.FindItem(part, "Vertex Map")?.Children.Select(c => (int)c.Value.ToUInt()).ToList() ?? [];
                    foreach (NifItem item in body.FindItem(part, "Triangles")?.Children ?? [])
                    {
                        written++;
                        NifTriangle t = item.Value.Get<NifTriangle>();
                        int[] local = [t.V1, t.V2, t.V3];
                        if (local.Any(i => i >= map.Count || map[i] >= vertices)) { invalid++; continue; }
                        if (local.Distinct().Count() < 3) degenerate++;
                    }
                }
            }

            var findings = new List<MeshFinding>();
            if (written < source)
                findings.Add(new MeshFinding("triangles-missing", FindingSeverity.Error,
                    $"the FBX has {source} triangles and the NIF {written}: {source - written} were lost"));
            if (invalid > 0)
                findings.Add(new MeshFinding("triangles-invalid", FindingSeverity.Error,
                    $"{invalid} triangles name a vertex the shape does not have"));
            if (degenerate > 0)
                findings.Add(new MeshFinding("triangles-degenerate", FindingSeverity.Warning,
                    $"{degenerate} triangles name the same vertex twice and draw nothing"));
            if (findings.Count == 0)
                findings.Add(new MeshFinding("triangles", FindingSeverity.Note, $"all {source} of the FBX's triangles are in the NIF"));
            return findings;
        }

        /// <summary>
        /// Every vertex of every skinned shape moved by bones whose weights sum to one.
        /// </summary>
        /// <remarks>
        /// A vertex weighted to nothing is drawn at the origin once the skeleton moves it, so
        /// every triangle that uses it stretches back to the actor's feet. The skin can hold
        /// every triangle of its source and be bound exactly where the skeleton stands and
        /// still do that: a Blender mesh shares a vertex across a UV seam, the converter splits
        /// it, and the cat's 364 split copies came out with no weight at all.
        /// </remarks>
        public static IReadOnlyList<MeshFinding> Weights(NifModel body)
        {
            ArgumentNullException.ThrowIfNull(body);

            int vertices = 0, bare = 0, partial = 0;
            foreach (NifItem shape in body.Blocks.Where(IsShape))
            {
                if (body.GetRef(shape, "Skin") is not { } skin || body.GetRef(skin, "Data") is not { } data) continue;

                int count = body.GetRef(skin, "Skin Partition") is { } partition && body.FindItem(partition, "Vertex Data") is { Children.Count: > 0 } buffer
                    ? buffer.Children.Count
                    : (int)(body.FindItem(shape, "Num Vertices")?.Value.ToUInt() ?? body.FindItem(data, "Num Vertices")?.Value.ToUInt() ?? 0);

                var total = new float[count];
                foreach (NifItem bone in body.FindItem(data, "Bone List")?.Children ?? [])
                    foreach (NifItem weight in body.FindItem(bone, "Vertex Weights")?.Children ?? [])
                    {
                        int index = (int)(body.FindItem(weight, "Index")?.Value.ToUInt() ?? 0);
                        if (index < count) total[index] += body.FindItem(weight, "Weight")?.Value.ToFloat() ?? 0f;
                    }

                vertices += count;
                bare += total.Count(t => t < 1e-3f);
                partial += total.Count(t => t >= 1e-3f && MathF.Abs(t - 1f) > 0.01f);
            }

            var findings = new List<MeshFinding>();
            if (bare > 0)
                findings.Add(new MeshFinding("skin-unweighted", FindingSeverity.Error,
                    $"{bare} of {vertices} vertices are moved by no bone and collapse to the origin when the skeleton moves"));
            if (partial > 0)
                findings.Add(new MeshFinding("skin-weights-sum", FindingSeverity.Warning,
                    $"{partial} vertices have weights that do not sum to one"));
            if (findings.Count == 0 && vertices > 0)
                findings.Add(new MeshFinding("skin-weights", FindingSeverity.Note, $"all {vertices} vertices are fully weighted"));
            return findings;
        }

        /// <summary>
        /// Every block's declared size against the size it would be written at.
        /// </summary>
        /// <remarks>
        /// A NIF's header states how long each block is, and the game reads a block and then
        /// checks it consumed exactly that many bytes. A file whose sizes were measured before
        /// its last edit -- a texture path rewritten to where the texture now is, sixteen bytes
        /// longer -- loads in a viewer that reads by structure and is refused by the game and
        /// the Creation Kit with "stream size mismatch". Nothing recomputes them but
        /// <c>UpdateHeader</c>, so nothing but this notices.
        /// </remarks>
        public static IReadOnlyList<MeshFinding> BlockSizes(NifModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            List<int> Declared() => model.FindItem(model.Header, "Block Size")?.Children
                .Select(c => (int)c.Value.ToUInt()).ToList() ?? [];

            List<int> declared = Declared();
            model.UpdateHeader();
            List<int> measured = Declared();

            var wrong = declared.Zip(measured).Select((p, i) => (Index: i, p.First, p.Second))
                .Where(p => p.First != p.Second).ToList();

            if (declared.Count != measured.Count)
                return [new MeshFinding("nif-block-count", FindingSeverity.Error,
                    $"the header lists {declared.Count} block sizes for {measured.Count} blocks")];

            if (wrong.Count == 0)
                return [new MeshFinding("nif-block-sizes", FindingSeverity.Note, $"all {declared.Count} blocks are the size the header says")];

            (int index, int said, int isNow) = wrong[0];
            string name = index < model.Blocks.Count ? model.Blocks[index].Name : "?";
            return [new MeshFinding("nif-block-sizes", FindingSeverity.Error,
                $"{wrong.Count} blocks are not the size the header says, the first being block {index}, a {name}, at {said} bytes against {isNow}")];
        }

        /// <summary>Where the creature stands relative to the ground.</summary>
        /// <remarks>
        /// A creature is authored standing on the origin: the root bone is at it and the lowest
        /// bone is on it. All 52 of the game's creature skeletons have their lowest bone at or
        /// below one unit and not one above it, seven reaching a little under -- the sabre cat's
        /// lowest is -4.2 -- because a bone may hang below the foot that carries it.
        ///
        /// An asset authored for another engine often is not. A Biped from 3ds Max is built
        /// around its pelvis, so the character hangs below the ground by the length of its legs,
        /// and a creature imported that way is buried to the waist with its feet underground.
        /// </remarks>
        public static IReadOnlyList<MeshFinding> Ground(NifModel skeleton)
        {
            ArgumentNullException.ThrowIfNull(skeleton);

            var bones = NodeWorld(skeleton);
            if (bones.Count == 0) return [];

            var lowest = bones.OrderBy(b => b.Value.Translation.Z).First();
            float floor = lowest.Value.Translation.Z;
            float top = bones.Values.Max(m => m.Translation.Z);

            if (floor > 1f)
                return [new MeshFinding("skeleton-ground", FindingSeverity.Error,
                    $"the whole rig stands {floor:F1} units above the ground, its lowest bone being '{lowest.Key}'; "
                    + "a creature is authored standing on the origin and this one floats")];

            // Below the ground by more than the game's own ever reach is the Biped case: built
            // around the pelvis, so the legs hang under the floor.
            if (floor < -5f)
                return [new MeshFinding("skeleton-ground", FindingSeverity.Error,
                    $"the rig hangs {-floor:F1} units below the ground, its lowest bone being '{lowest.Key}'; "
                    + "the game's own reach -4.2 at worst, so this is a rig built around its pelvis rather than its feet")];

            return [new MeshFinding("skeleton-ground", FindingSeverity.Note,
                $"the rig stands on the ground, {floor:F1} to {top:F1} units")];
        }

        /// <summary>The slot each skinned partition says the mesh is worn in.</summary>
        /// <remarks>
        /// A dismembered skin names a biped slot per partition, and the slots are 30 to 61. A
        /// converter that does not know which one writes 0, and the game reads that as a biped
        /// object out of range: rather than skinning the mesh to the actor it looks for a `Prn`
        /// string naming a node to hang it off, finds none, and refuses the mesh with "Could not
        /// find parent node extra data". 362 of the game's own worn meshes say 32, the body, and
        /// only 14 partitions in the whole of it say 0.
        /// </remarks>
        public static IReadOnlyList<MeshFinding> WornSlots(NifModel body)
        {
            ArgumentNullException.ThrowIfNull(body);

            var slots = new List<(string Shape, uint Slot)>();
            foreach (NifItem shape in body.Blocks.Where(IsShape))
                if (body.GetRef(shape, "Skin") is { Name: "BSDismemberSkinInstance" } skin)
                    foreach (NifItem partition in body.FindItem(skin, "Partitions")?.Children ?? [])
                        slots.Add((body.GetName(shape) ?? "?", body.GetUInt(partition, "Body Part")));

            if (slots.Count == 0)
                return [new MeshFinding("skin-slot", FindingSeverity.Note, "no partition names a slot, which is how a creature's own body is skinned")];

            var wrong = slots.Where(s => s.Slot is < 30 or > 61).ToList();

            return wrong.Count == 0
                ? [new MeshFinding("skin-slot", FindingSeverity.Note,
                    $"all {slots.Count} partitions name a slot: {string.Join(", ", slots.Select(s => s.Slot).Distinct().Order())}")]
                : [new MeshFinding("skin-slot", FindingSeverity.Error,
                    $"{wrong.Count} of {slots.Count} partitions name no slot, the first on '{wrong[0].Shape}' saying {wrong[0].Slot}; "
                    + "the game reads it as a biped object out of range and refuses the mesh")];
        }

        /// <summary>The sphere each of a skin's bones carries for what it moves.</summary>
        /// <remarks>
        /// A skinned shape has no bound of its own: the engine rebuilds one each frame from a
        /// sphere per bone, held in the bone's space. A converter leaves them all empty and at
        /// the origin, and a bound merged from nothing but coincident empty spheres is not a
        /// number -- the engine propagates that up the tree and culls everything beneath it,
        /// which is an invisible actor complaining once a frame. No shape the game ships has an
        /// empty one.
        /// </remarks>
        public static IReadOnlyList<MeshFinding> SkinBounds(NifModel body)
        {
            ArgumentNullException.ThrowIfNull(body);

            int bones = 0, empty = 0;
            foreach (NifItem shape in body.Blocks.Where(IsShape))
            {
                if (body.GetRef(shape, "Skin") is not { } skin || body.GetRef(skin, "Data") is not { } data) continue;
                foreach (NifItem bone in body.FindItem(data, "Bone List")?.Children ?? [])
                {
                    bones++;
                    if (body.FindItem(bone, "Bounding Sphere") is { } sphere
                        && float.TryParse(body.FindItem(sphere, "Radius")?.Value.ToString(), out float r) && r <= 0f) empty++;
                }
            }

            if (bones == 0) return [];

            return empty == bones
                ? [new MeshFinding("skin-bounds", FindingSeverity.Error,
                    $"not one of the {bones} bones carries a sphere, so the shape's bound is merged from nothing and comes out as no number at all")]
                : empty > 0
                    ? [new MeshFinding("skin-bounds", FindingSeverity.Note,
                        $"{bones - empty} of {bones} bones carry a sphere; the rest move no vertex")]
                    : [new MeshFinding("skin-bounds", FindingSeverity.Note, $"all {bones} bones carry a sphere")];
        }

        private static bool IsShape(NifItem block) => block.Name is "BSTriShape" or "BSDynamicTriShape" or "NiTriShape";

        /// <summary>
        /// Two files' places for the same bones: the worst disagreement, and whether it is a
        /// scale -- one file's bones all the same factor further out than the other's.
        /// </summary>
        private static IEnumerable<MeshFinding> Compare(string rule, string a, string b, List<(string Bone, Vector3 A, Vector3 B)> pairs)
        {
            if (pairs.Count == 0) yield break;

            var worst = pairs.MaxBy(p => Vector3.Distance(p.A, p.B));
            float distance = Vector3.Distance(worst.A, worst.B);
            if (distance <= Tolerance)
            {
                yield return new MeshFinding(rule, FindingSeverity.Note, $"{a} and {b} agree on all {pairs.Count} bones, the worst {distance:F4} units apart");
                yield break;
            }

            // A scale shows as the same ratio on nearly every bone. Nearly, not every: a
            // creature can carry a bone or two the two files already disagreed about, and one
            // of those must not hide the factor that moved all the rest.
            var ratios = pairs.Where(p => p.B.Length() > 1f).Select(p => p.A.Length() / p.B.Length()).Order().ToList();
            float ratio = ratios.Count == 0 ? 1f : ratios[ratios.Count / 2];
            bool uniform = ratios.Count > 0 && ratios.Count(r => MathF.Abs(r - ratio) < 0.01f * ratio) >= 0.9f * ratios.Count;

            if (uniform && MathF.Abs(ratio - 1f) > 0.01f)
            {
                yield return new MeshFinding(rule, FindingSeverity.Error,
                    $"{a} is {ratio:G4} times the size of {b} -- a scale on a root node one converter applied and the other did not");
                yield break;
            }

            // A few bones apart is what the game itself does: 12 of its 49 creatures disagree
            // with their own rigs, mostly on bones nothing is skinned to -- a magic node, a
            // weapon mount, the wolf's lip -- the troll's by 187 units and the female's on 36
            // of 96 fingers and toes. Most of the skeleton out of place is another matter.
            var apart = pairs.Where(p => Vector3.Distance(p.A, p.B) > Tolerance).ToList();
            bool widespread = apart.Count > pairs.Count / 2;

            yield return new MeshFinding(rule, widespread ? FindingSeverity.Error : FindingSeverity.Warning,
                $"{a} and {b} disagree on where {apart.Count} of {pairs.Count} bones stand, the worst {worst.Bone} by {distance:F3} units");
        }
    }
}
