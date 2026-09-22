using System.Numerics;
using NIFSharp;

namespace SKAssets.Authoring
{
    /// <summary>
    /// A mesh worn by an actor, made to sit the way the game's own worn meshes sit.
    /// </summary>
    public static class WornMesh
    {
        private static bool IsShape(NifItem block) => block.Name is "BSTriShape" or "BSDynamicTriShape" or "NiTriShape";

        /// <summary>
        /// Moves every skinned shape to be a child of the root, keeping where it stands.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A skinned shape is placed by its bones, and the bones are the actor's, so the shape's
        /// own place in the tree is not where it is drawn -- it is only where the engine starts
        /// from. Every body the game has an actor wear is a child of the root: 1,459 of the
        /// shipped skinned shapes are, and the 126 that are not are effects, thrown weapons and
        /// the face parts of a generated head, none of them worn through an armour addon.
        /// </para>
        /// <para>
        /// A converter has no reason to know this. Blender parents a body to its armature and
        /// the FBX says so, so the cat's body arrived under the bone it was parented to, which
        /// leaves the shape carrying that bone's transform on top of its own and its bound
        /// computed in a frame the skin does not use.
        /// </para>
        /// <para>
        /// The shape keeps the place it had: what its parents did to it is composed into its own
        /// transform before it is moved. Unskinned shapes are left where they are, since a tree
        /// of them is how a mesh with moving parts is built.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<string> Flatten(NifModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            NifItem root = model.Blocks[0];
            var parent = new Dictionary<int, NifItem>();
            foreach (NifItem node in model.Blocks)
                foreach (NifItem child in model.GetRefArray(node, "Children"))
                    parent[model.IndexOf(child)] = node;

            var notes = new List<string>();
            foreach (NifItem shape in model.Blocks.Where(IsShape).ToList())
            {
                if (model.GetRef(shape, "Skin") is null) continue;
                if (!parent.TryGetValue(model.IndexOf(shape), out NifItem? above) || ReferenceEquals(above, root)) continue;

                NifTransform placed = model.GetTransform(shape);
                var chain = new List<string>();
                for (NifItem? at = above; at is not null && !ReferenceEquals(at, root);
                     at = parent.GetValueOrDefault(model.IndexOf(at)))
                {
                    placed = placed.ComposedWith(model.GetTransform(at));
                    chain.Add(model.GetName(at) ?? at.Name);
                }

                model.SetTransform(shape, placed);
                Detach(model, above, shape);
                Attach(model, root, shape);
                notes.Add($"'{model.GetName(shape)}' was worn under {string.Join(" under ", chain)} and is now under the root");
            }

            if (notes.Count > 0) model.UpdateHeader();
            return notes;
        }

        /// <summary>
        /// Gives every bone of every skinned shape the sphere enclosing what it moves.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A skinned shape is drawn wherever its bones are, so its bound cannot be a sphere in
        /// the file: it is rebuilt each frame from one sphere per bone, held in that bone's own
        /// space and carried out to where the bone stands. The converter leaves all of them at
        /// the origin with a radius of nothing.
        /// </para>
        /// <para>
        /// Merging spheres that share a centre and have no size is a division by the distance
        /// between them, which is zero: the bound comes out as a NaN, the engine propagates it
        /// up the tree, and everything under that node is culled -- the actor is invisible and
        /// says so once a frame. The game's own bodies carry real radii, 45.9 units on the sabre
        /// cat's pelvis.
        /// </para>
        /// <para>
        /// Each sphere is measured from the vertices that bone actually moves, put into the
        /// bone's space the way the skin itself is: through the skin's overall transform and
        /// then the bone's.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<string> Bounds(NifModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            var notes = new List<string>();
            foreach (NifItem shape in model.Blocks.Where(IsShape))
            {
                if (model.GetRef(shape, "Skin") is not { } skin || model.GetRef(skin, "Data") is not { } data) continue;

                List<Vector3> positions = [.. Positions(model, shape)];
                if (positions.Count == 0) continue;

                Matrix4x4 overall = model.FindItem(data, "Skin Transform") is { } o
                    ? model.GetTransform(o).ToMatrix() : Matrix4x4.Identity;

                int measured = 0;
                float widest = 0f;
                foreach (NifItem bone in model.FindItem(data, "Bone List")?.Children ?? [])
                {
                    if (model.FindItem(bone, "Skin Transform") is not { } transform) continue;
                    Matrix4x4 intoBone = overall * model.GetTransform(transform).ToMatrix();

                    var moved = new List<Vector3>();
                    foreach (NifItem weight in model.FindItem(bone, "Vertex Weights")?.Children ?? [])
                    {
                        int index = (int)model.GetUInt(weight, "Index");
                        if (index >= positions.Count) continue;
                        if (model.FindItem(weight, "Weight") is { } w && float.TryParse(w.Value.ToString(), out float f) && f <= 0f) continue;
                        moved.Add(Vector3.Transform(positions[index], intoBone));
                    }

                    if (moved.Count == 0) continue;

                    Vector3 low = moved.Aggregate(Vector3.Min), high = moved.Aggregate(Vector3.Max);
                    Vector3 centre = (low + high) / 2f;
                    float radius = moved.Max(p => (p - centre).Length());

                    if (model.FindItem(bone, "Bounding Sphere") is not { } sphere) continue;
                    model.FindItem(sphere, "Center")!.Value.Set(new NifVector3(centre.X, centre.Y, centre.Z));
                    model.FindItem(sphere, "Radius")!.Value.SetFloat(radius);
                    measured++;
                    widest = Math.Max(widest, radius);
                }

                int bones = model.FindItem(data, "Bone List")?.Children.Count ?? 0;
                if (measured > 0)
                    notes.Add($"'{model.GetName(shape)}': {measured} of {bones} bones given the sphere of what they move, "
                        + $"the widest {widest:F1} units{(measured < bones ? $"; {bones - measured} move nothing and keep an empty sphere" : "")}");
            }

            return notes;
        }

        /// <summary>The shape's vertex positions, from wherever its version of the format keeps them.</summary>
        private static IEnumerable<Vector3> Positions(NifModel model, NifItem shape)
        {
            NifItem? buffer = model.GetRef(shape, "Skin") is { } skin && model.GetRef(skin, "Skin Partition") is { } partition
                ? model.FindItem(partition, "Vertex Data")
                : null;

            if (buffer is not null)
            {
                foreach (NifItem vertex in buffer.Children)
                    if (model.FindItem(vertex, "Vertex") is { } at)
                    {
                        NifVector3 v = at.Value.Get<NifVector3>();
                        yield return new Vector3(v.X, v.Y, v.Z);
                    }

                yield break;
            }

            if (model.GetRef(shape, "Data") is not { } data) yield break;
            foreach (NifVector3 v in model.GetVertices(data)) yield return new Vector3(v.X, v.Y, v.Z);
        }

        private static void Detach(NifModel model, NifItem node, NifItem child)
        {
            var kept = model.GetRefArray(node, "Children").Where(c => !ReferenceEquals(c, child)).ToList();
            Children(model, node, kept);
        }

        private static void Attach(NifModel model, NifItem node, NifItem child)
        {
            var with = model.GetRefArray(node, "Children").Append(child).ToList();
            Children(model, node, with);
        }

        private static void Children(NifModel model, NifItem node, List<NifItem> children)
        {
            model.FindItem(node, "Num Children")!.Value.SetCount((ulong)children.Count);
            NifItem list = model.FindItem(node, "Children")!;
            model.UpdateArraySize(list);
            for (int i = 0; i < children.Count; i++) list.Children[i].Value.SetLink(model.IndexOf(children[i]));
        }
    }
}
