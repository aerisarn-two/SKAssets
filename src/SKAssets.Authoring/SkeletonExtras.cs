using System.Numerics;
using NIFSharp;

namespace SKAssets.Authoring
{
    /// <summary>
    /// What a skeleton NIF carries besides its bones, given to one rebuilt from an FBX.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A converter writes the bones, the ragdoll and a <c>BSXFlags</c>, which is a complete
    /// description of the skeleton and not a complete skeleton file. Every skeleton the game
    /// ships hangs three more pieces of extra data off its root, and a creature built from
    /// an FBX has none of them:
    /// </para>
    /// <list type="bullet">
    /// <item><c>BSBound</c> named <c>BBX</c>, the actor's box: a centre and half extents,
    /// and in all 49 of the shipped skeletons the centre is the box's own height above the
    /// origin, so the box stands on the ground rather than straddling it.</item>
    /// <item><c>BSBoneLODExtraData</c> named <c>BSBoneLOD</c>, a distance and a bone per
    /// entry, at which the bones under it stop being animated.</item>
    /// <item><c>NiIntegerExtraData</c> named <c>SkeletonID</c>. It looks like an identity and
    /// is not one: the shipped skeletons share a handful of values between creatures that
    /// have nothing to do with each other -- 1361955 covers the hare, the wolf, the draugr
    /// and the falmer -- so it is the exporter's run and any value does, including the
    /// template's, which is what is copied here.</item>
    /// </list>
    /// </remarks>
    public static class SkeletonExtras
    {
        private static readonly string[] Carried = ["BSBound", "BSBoneLODExtraData", "NiIntegerExtraData", "BSXFlags"];

        /// <summary>
        /// Gives <paramref name="rebuilt"/> the extra data <paramref name="template"/> has and it
        /// has not, with the box measured from <paramref name="bodies"/> where any are given.
        /// </summary>
        /// <param name="boneMap">Template bone to new bone, for the bones named by the LOD list.</param>
        public static IReadOnlyList<string> Carry(NifModel rebuilt, NifModel template,
            IReadOnlyDictionary<string, string>? boneMap = null, IReadOnlyList<NifModel>? bodies = null)
        {
            ArgumentNullException.ThrowIfNull(rebuilt);
            ArgumentNullException.ThrowIfNull(template);

            var notes = new List<string>();
            NifItem root = rebuilt.Blocks[0];
            var present = new List<NifItem>(rebuilt.GetRefArray(root, "Extra Data List"));
            var have = new HashSet<string>(present.Select(b => rebuilt.GetName(b) ?? b.Name), StringComparer.OrdinalIgnoreCase);

            foreach (NifItem from in template.GetRefArray(template.Blocks[0], "Extra Data List"))
            {
                string name = template.GetName(from) ?? from.Name;
                if (!Carried.Contains(from.Name, StringComparer.Ordinal) || have.Contains(name)) continue;

                // The LOD list names bones, and a rig of its own may have none of them: the
                // sabre cat's one entry is a finger the cat has not got. An empty list is not
                // what the shipped files hold, so it is left out rather than written empty.
                if (from.Name == "BSBoneLODExtraData" && Surviving(rebuilt, template, from, boneMap).Count == 0)
                {
                    notes.Add($"{name}: none of its {template.GetUInt(from, "BoneLOD Count")} entries name a bone this rig has, and it is left out");
                    continue;
                }

                NifItem made = rebuilt.InsertBlock(from.Name);
                rebuilt.SetString(made, "Name", name);

                switch (from.Name)
                {
                    case "BSBound":
                        (Vector3 centre, Vector3 half) = Box(rebuilt, bodies);
                        rebuilt.FindItem(made, "Center")!.Value.Set(new NifVector3(centre.X, centre.Y, centre.Z));
                        rebuilt.FindItem(made, "Dimensions")!.Value.Set(new NifVector3(half.X, half.Y, half.Z));
                        notes.Add($"{name}: half extents {half.X:F1} by {half.Y:F1} by {half.Z:F1}, "
                            + $"measured from {(bodies is { Count: > 0 } ? "the body" : "the bones")}");
                        break;

                    case "BSBoneLODExtraData":
                        int kept = BoneLod(rebuilt, made, template, from, boneMap);
                        notes.Add($"{name}: {kept} of {template.GetUInt(from, "BoneLOD Count")} entries name a bone this rig has");
                        break;

                    default:
                        // A number and nothing else: SkeletonID, and BSXFlags where the
                        // converter did not write one.
                        rebuilt.FindItem(made, "Integer Data")!.Value.SetCount(template.GetUInt(from, "Integer Data"));
                        notes.Add($"{name}: {template.GetUInt(from, "Integer Data")}, the template's");
                        break;
                }

                present.Add(made);
            }

            if (notes.Count == 0) return notes;

            rebuilt.FindItem(root, "Num Extra Data List")!.Value.SetCount((ulong)present.Count);
            NifItem list = rebuilt.FindItem(root, "Extra Data List")!;
            rebuilt.UpdateArraySize(list);
            for (int i = 0; i < present.Count; i++) list.Children[i].Value.SetLink(rebuilt.IndexOf(present[i]));

            // Blocks were added, so the header's list of them is a block short of the file.
            // Saving would work this out anyway; a caller reading the model before it saves
            // should not be told the file is inconsistent when it is only unmeasured.
            rebuilt.UpdateHeader();

            return notes;
        }

        /// <summary>
        /// The actor's box: half extents around a centre that is the box's own height up, which
        /// is what every shipped skeleton states and what leaves the box standing on the ground.
        /// </summary>
        private static (Vector3 Centre, Vector3 Half) Box(NifModel rebuilt, IReadOnlyList<NifModel>? bodies)
        {
            var points = new List<Vector3>();
            foreach (NifModel body in bodies ?? [])
                foreach (NifItem shape in body.Blocks)
                    points.AddRange(Positions(body, shape));

            // Nothing to measure but the rig, which is inside the body and so a floor on it.
            if (points.Count == 0)
                foreach (Matrix4x4 at in CreatureChecks.NodeWorld(rebuilt).Values)
                    points.Add(at.Translation);

            if (points.Count == 0) return (Vector3.Zero, Vector3.Zero);

            float MaxAbs(Func<Vector3, float> of) => points.Max(p => Math.Abs(of(p)));
            float top = points.Max(p => p.Z);
            return (new Vector3(0, 0, top / 2f), new Vector3(MaxAbs(p => p.X), MaxAbs(p => p.Y), top / 2f));
        }

        private static IEnumerable<Vector3> Positions(NifModel body, NifItem shape)
        {
            // Skinned SSE geometry keeps its vertices in the partition; the older layout in a
            // data block beside the shape.
            NifItem? buffer = body.GetRef(shape, "Skin") is { } skin && body.GetRef(skin, "Skin Partition") is { } partition
                ? body.FindItem(partition, "Vertex Data")
                : null;

            if (buffer is not null)
            {
                foreach (NifItem vertex in buffer.Children)
                    if (body.FindItem(vertex, "Vertex") is { } at)
                    {
                        NifVector3 v = at.Value.Get<NifVector3>();
                        yield return new Vector3(v.X, v.Y, v.Z);
                    }

                yield break;
            }

            if (body.GetRef(shape, "Data") is not { } data) yield break;
            foreach (NifVector3 v in body.GetVertices(data)) yield return new Vector3(v.X, v.Y, v.Z);
        }

        /// <summary>The LOD entries whose bone the new rig has, under the name it has for it.</summary>
        private static List<(uint Distance, string Bone)> Surviving(NifModel rebuilt, NifModel template, NifItem from,
            IReadOnlyDictionary<string, string>? boneMap)
        {
            var bones = new HashSet<string>(
                rebuilt.Blocks.Where(b => b.Name is "NiNode" or "BSFadeNode").Select(b => rebuilt.GetName(b) ?? ""),
                StringComparer.OrdinalIgnoreCase);

            var kept = new List<(uint Distance, string Bone)>();
            foreach (NifItem entry in template.FindItem(from, "BoneLOD Info")?.Children ?? [])
            {
                string bone = template.GetString(entry, "Bone Name");
                if (boneMap?.TryGetValue(bone, out string? renamed) == true) bone = renamed;
                if (bones.Contains(bone)) kept.Add((template.GetUInt(entry, "Distance"), bone));
            }

            return kept;
        }

        /// <summary>Copies the LOD list, keeping the entries whose bone the new rig has.</summary>
        private static int BoneLod(NifModel rebuilt, NifItem made, NifModel template, NifItem from,
            IReadOnlyDictionary<string, string>? boneMap)
        {
            List<(uint Distance, string Bone)> kept = Surviving(rebuilt, template, from, boneMap);

            rebuilt.FindItem(made, "BoneLOD Count")!.Value.SetCount((ulong)kept.Count);
            NifItem list = rebuilt.FindItem(made, "BoneLOD Info")!;
            rebuilt.UpdateArraySize(list);
            for (int i = 0; i < kept.Count; i++)
            {
                rebuilt.FindItem(list.Children[i], "Distance")!.Value.SetCount(kept[i].Distance);
                rebuilt.SetString(list.Children[i], "Bone Name", kept[i].Bone);
            }

            return kept.Count;
        }
    }
}
