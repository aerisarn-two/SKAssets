using System.Numerics;
using System.Text;
using NIFSharp;
using Xunit;
namespace SKAssets.Export.Tests;

// The cat's skeleton.nif against its body NIF: does each bone stand where the skin was
// bound to it, and does the body hold every triangle?
public sealed class ZzCatConsistency
{
    [Fact]
    public void Check()
    {
        string mod = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        if (mod.Length == 0) return;
        string folder = Environment.GetEnvironmentVariable("CAT_CHECK_FOLDER") ?? Path.Combine(mod, "Data", "Meshes", "actors", "HouseCat");
        var db = NifXmlDatabase.LoadEmbedded();
        var sb = new StringBuilder();
        var skel = NifModel.Load(Directory.GetFiles(folder, "*.nif", SearchOption.AllDirectories).First(f => Path.GetFileName(f).Equals("skeleton.nif", StringComparison.OrdinalIgnoreCase)), db);
        var body = NifModel.Load(Environment.GetEnvironmentVariable("CAT_CHECK_BODY") ?? Path.Combine(folder, "HouseCatSkin.nif"), db);

        Dictionary<string, Matrix4x4> World(NifModel m)
        {
            var result = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
            void Walk(NifItem node, Matrix4x4 parent)
            {
                Matrix4x4 w = m.GetTransform(node).ToMatrix() * parent;
                result.TryAdd(m.GetName(node), w);
                if (m.BlockInherits(node, "NiNode"))
                    foreach (NifItem child in m.GetChildren(node)) Walk(child, w);
            }
            Walk(m.Blocks[0], Matrix4x4.Identity);
            return result;
        }
        var skelWorld = World(skel);
        var bodyWorld = World(body);
        sb.AppendLine($"skeleton root '{skel.GetName(skel.Blocks[0])}' {skel.Blocks[0].Name} scale {skel.GetTransform(skel.Blocks[0]).Scale}; body root '{body.GetName(body.Blocks[0])}' {body.Blocks[0].Name} scale {body.GetTransform(body.Blocks[0]).Scale}");

        foreach (NifItem shape in body.Blocks.Where(b => b.Name is "BSTriShape" or "NiTriShape"))
        {
            var alpha = body.GetRef(shape, "Alpha Property");
            var shader = body.GetRef(shape, "Shader Property");
            string alphaText = alpha is null ? "none" : $"flags 0x{body.FindItem(alpha, "Flags")?.Value.ToUInt():x} threshold {body.FindItem(alpha, "Threshold")?.Value.ToUInt()}";
            string shaderText = shader is null ? "-" : $"{body.FindItem(shader, "Shader Flags 1")?.Value.ToUInt():x}/{body.FindItem(shader, "Shader Flags 2")?.Value.ToUInt():x}";
            sb.AppendLine($"  alpha {alphaText}; shader flags {shaderText}; data size {body.FindItem(shape, "Data Size")?.Value.ToUInt()}");
            sb.AppendLine($"shape '{body.GetName(shape)}' {shape.Name}: vertices {body.FindItem(shape, "Num Vertices")?.Value.ToUInt()}, triangles {body.FindItem(shape, "Num Triangles")?.Value.ToUInt()}, transform scale {body.GetTransform(shape).Scale} T={body.GetTransform(shape).Translation.ToNumerics()}");
            NifItem? skin = body.GetRef(shape, "Skin");
            if (skin is null) continue;
            NifItem? data = body.GetRef(skin, "Data");
            NifItem? partition = body.GetRef(skin, "Skin Partition");
            var bones = body.GetRefArray(skin, "Bones").ToList();
            if (partition is not null)
            {
                var parts = body.FindItem(partition, "Partitions");
                int partTris = 0;
                foreach (var p in parts?.Children ?? []) partTris += (int)(body.FindItem(p, "Num Triangles")?.Value.ToUInt() ?? 0);
                sb.AppendLine($"  partition: {parts?.Children.Count} partitions, {partTris} triangles; data size {body.FindItem(partition, "Data Size")?.Value.ToUInt()} vertex size {body.FindItem(partition, "Vertex Size")?.Value.ToUInt()}");
            }
            if (data is null) continue;
            NifItem? overall = body.FindItem(data, "Skin Transform");
            sb.AppendLine($"  skin transform: scale {(overall is null ? 1 : body.GetTransform(overall).Scale)} T={(overall is null ? Vector3.Zero : body.GetTransform(overall).Translation.ToNumerics())}");
            var list = body.FindItem(data, "Bone List")!.Children;
            double sumRatio = 0; int n = 0; float worst = 0; string worstName = "";
            for (int i = 0; i < bones.Count && i < list.Count; i++)
            {
                string name = body.GetName(bones[i]);
                var st = body.GetTransform(body.FindItem(list[i], "Skin Transform")!);
                Matrix4x4.Invert(st.ToMatrix(), out var bind);
                if (!skelWorld.TryGetValue(name, out var sw)) { sb.AppendLine($"  bone {name}: not in the skeleton"); continue; }
                float d = Vector3.Distance(bind.Translation, sw.Translation);
                if (d > worst) { worst = d; worstName = name; }
                if (bind.Translation.Length() > 5) { sumRatio += sw.Translation.Length() / bind.Translation.Length(); n++; }
                if (i < 4 || name.Contains("head") || name.Contains("foot_f.L"))
                    sb.AppendLine($"  bone {name,-18} bind {Fmt(bind.Translation)} skeleton {Fmt(sw.Translation)} bodyNode {Fmt(bodyWorld.GetValueOrDefault(name).Translation)} skinScale {st.Scale:F4}");
            }
            sb.AppendLine($"  bind vs skeleton: worst {worst:F3} at {worstName}; mean |skeleton|/|bind| {sumRatio / Math.Max(n, 1):F4} over {n} bones");
        }
        // ---- the Havok rig the animations drive, against the skeleton.nif's nodes
        string hkx = Directory.GetFiles(folder, "*.hkx", SearchOption.AllDirectories).FirstOrDefault(f => Path.GetFileName(f).Equals("skeleton.hkx", StringComparison.OrdinalIgnoreCase)) ?? "";
        if (hkx.Length > 0)
        {
            var rig = HKFBX.Hkx.HkxSkeletonFile.Read(hkx);
            var world = new Matrix4x4[rig.Rig.Count];
            float worstRig = 0; string worstBone = ""; double ratio = 0; int m = 0;
            for (int i = 0; i < world.Length; i++)
            {
                var b = rig.Rig.Bones[i];
                var local = Matrix4x4.CreateScale(b.ReferencePose.Scale) * Matrix4x4.CreateFromQuaternion(b.ReferencePose.Rotation) * Matrix4x4.CreateTranslation(b.ReferencePose.Translation);
                world[i] = b.ParentIndex >= 0 ? local * world[b.ParentIndex] : local;
                if (!skelWorld.TryGetValue(b.Name, out var nif)) continue;
                float d = Vector3.Distance(world[i].Translation, nif.Translation);
                if (d > worstRig) { worstRig = d; worstBone = b.Name; }
                if (world[i].Translation.Length() > 5) { ratio += nif.Translation.Length() / world[i].Translation.Length(); m++; }
            }
            sb.AppendLine($"havok rig vs skeleton.nif: worst {worstRig:F3} at {worstBone}; mean |nif|/|havok| {ratio / Math.Max(m, 1):F4} over {m} bones");
            foreach (var body2 in rig.Bodies.Take(3))
                sb.AppendLine($"  havok body {body2.Name} at {Fmt(body2.Transform.Translation)}, capsule r {body2.Shape?.Radius:F2}");
        }

        // ---- every source triangle in the skin, by position, and its winding
        string trisPath = Path.Combine(mod, "body_tris.txt");
        if (File.Exists(trisPath) && Environment.GetEnvironmentVariable("CAT_CHECK_BODY") is null)
        {
            static string Key(Vector3 v) => $"{MathF.Round(v.X, 1)},{MathF.Round(v.Y, 1)},{MathF.Round(v.Z, 1)}";
            var source = File.ReadAllLines(trisPath).Select(l => l.Split(' ').Select(float.Parse).ToArray())
                .Select(a => new[] { new Vector3(a[0], a[1], a[2]), new Vector3(a[3], a[4], a[5]), new Vector3(a[6], a[7], a[8]) }).ToList();
            var nifTris = new List<Vector3[]>();
            int outOfRange = 0, degenerate = 0;
            foreach (NifItem shape in body.Blocks.Where(b => b.Name == "BSTriShape"))
            {
                if (body.GetRef(shape, "Skin") is not { } skin || body.GetRef(skin, "Skin Partition") is not { } partition) continue;
                Matrix4x4 shapeWorld = body.GetTransform(shape).ToMatrix();
                var vertexData = body.FindItem(partition, "Vertex Data")?.Children ?? [];
                var positions = vertexData.Select(v => Vector3.Transform(body.FindItem(v, "Vertex")!.Value.Get<NifVector3>().ToNumerics(), shapeWorld)).ToList();
                foreach (var part in body.FindItem(partition, "Partitions")!.Children)
                {
                    var map = body.FindItem(part, "Vertex Map")?.Children.Select(c => (int)c.Value.ToUInt()).ToList() ?? [];
                    foreach (var tri in body.FindItem(part, "Triangles")?.Children ?? [])
                    {
                        NifTriangle t3 = tri.Value.Get<NifTriangle>();
                        var idx = new[] { (int)t3.V1, t3.V2, t3.V3 };
                        if (idx.Any(i => i >= map.Count || map[i] >= positions.Count)) { outOfRange++; continue; }
                        var p = idx.Select(i => positions[map[i]]).ToArray();
                        if (idx.Distinct().Count() < 3) degenerate++;
                        nifTris.Add(p);
                    }
                }
            }
            var nifByKey = new Dictionary<string, Vector3[]>();
            foreach (var t in nifTris) nifByKey.TryAdd(string.Join("|", t.Select(Key).Order()), t);
            int found = 0, flipped = 0;
            foreach (var t in source)
            {
                if (!nifByKey.TryGetValue(string.Join("|", t.Select(Key).Order()), out var n)) continue;
                found++;
                Vector3 a = Vector3.Cross(t[1] - t[0], t[2] - t[0]), b = Vector3.Cross(n[1] - n[0], n[2] - n[0]);
                if (Vector3.Dot(a, b) < 0) flipped++;
            }
            sb.AppendLine($"triangles: source {source.Count}, nif {nifTris.Count} (out of range {outOfRange}, degenerate {degenerate}); source found in nif {found}, missing {source.Count - found}, winding flipped {flipped}");
            // Vertices by nearest position rather than by rounded key, then triangles by vertex
            // sets; an unmatched source triangle whose quad is covered the other way is a split,
            // one whose area no NIF triangle covers is a hole.
            var points = new List<Vector3>();
            int Id(Vector3 v)
            {
                for (int i = 0; i < points.Count; i++) if (Vector3.DistanceSquared(points[i], v) < 1e-4f) return i;
                points.Add(v); return points.Count - 1;
            }
            var nifSets = nifTris.Select(t => t.Select(Id).OrderBy(i => i).ToArray()).ToList();
            var nifKeys = nifSets.Select(k => string.Join(",", k)).ToHashSet();
            var edges = new Dictionary<string, int>();
            foreach (var k in nifSets)
                foreach (var (a, b) in new[] { (k[0], k[1]), (k[1], k[2]), (k[0], k[2]) }) edges[$"{a},{b}"] = edges.GetValueOrDefault($"{a},{b}") + 1;
            int exact = 0, split = 0, hole = 0, unknownVertex = 0, reversed = 0;
            var holes = new List<string>();
            foreach (var t in source)
            {
                var ids = t.Select(v => { int before = points.Count; int id = Id(v); if (points.Count > before) unknownVertex++; return id; }).OrderBy(i => i).ToArray();
                if (nifKeys.Contains(string.Join(",", ids)))
                {
                    exact++;
                    var n = nifTris[nifSets.FindIndex(k => k.SequenceEqual(ids))];
                    if (Vector3.Dot(Vector3.Cross(t[1] - t[0], t[2] - t[0]), Vector3.Cross(n[1] - n[0], n[2] - n[0])) < 0) reversed++;
                    continue;
                }
                int shared = new[] { (ids[0], ids[1]), (ids[1], ids[2]), (ids[0], ids[2]) }.Count(e => edges.ContainsKey($"{e.Item1},{e.Item2}"));
                if (shared == 3) split++; else { hole++; if (holes.Count < 6) holes.Add(string.Join(" ", t.Select(Fmt))); }
            }
            sb.AppendLine($"  by nearest vertex: exact {exact}, edges all present (a quad split the other way) {split}, uncovered {hole}, wound the other way {reversed}; source vertices absent from the nif {unknownVertex}; nif points {points.Count}");
            foreach (var h in holes) sb.AppendLine($"    uncovered: {h}");
            if (source.Count > 0 && nifTris.Count > 0)
                sb.AppendLine($"  first source vertex {Fmt(source[0][0])}, first nif vertex {Fmt(nifTris[0][0])}");
        }
        foreach (string fbxName in new[] { "Cat_Simple_Body.fbx", "../catsimple_fbx/Cat_Simple_Ragdoll.fbx" })
        {
            var doc = LeanMeshIO.FbxDocument.Load(Path.Combine(mod, fbxName));
            var scene = new HKFBX.Fbx.FbxScene(doc);
            foreach (var model in scene.RootModels().Take(3))
                sb.AppendLine($"fbx {fbxName}: root '{model.Name}' {model.Properties.GetString("nif_block_type")} Lcl Scaling {model.Properties.GetVector3("Lcl Scaling")}");
        }
        {
            var partition = body.Blocks.First(b => b.Name == "NiSkinPartition");
            var tri = body.FindItem(body.FindItem(partition, "Partitions")!.Children[0], "Triangles")!;
            sb.AppendLine($"triangle array '{tri.Name}' children {tri.Children.Count}; first: {string.Join(", ", tri.Children.Take(1).SelectMany(c => c.Children).Select(c => $"{c.Name}={c.Value.ToUInt()}"))}; value type {tri.Children.FirstOrDefault()?.Value.Type}");
        }
        {
            var shape0 = body.Blocks.First(b => b.Name == "BSTriShape");
            var skin0 = body.GetRef(shape0, "Skin")!;
            var partition = body.GetRef(skin0, "Skin Partition")!;
            var vdata = body.FindItem(partition, "Vertex Data")!.Children;
            var first = vdata[0];
            sb.AppendLine($"vertex fields: {string.Join(", ", first.Children.Select(c => $"{c.Name}:{c.Value.Type}[{c.Children.Count}]"))}");
            foreach (var part in body.FindItem(partition, "Partitions")!.Children)
                sb.AppendLine($"partition fields: {string.Join(", ", part.Children.Select(c => $"{c.Name}[{c.Children.Count}]"))}; bones {body.FindItem(part, "Num Bones")?.Value.ToUInt()} weights/vertex {body.FindItem(part, "Num Weights Per Vertex")?.Value.ToUInt()}");
            int zero = 0, low = 0, badIndex = 0; var sums = new List<float>();
            int partBones = (int)(body.FindItem(body.FindItem(partition, "Partitions")!.Children[0], "Num Bones")?.Value.ToUInt() ?? 0);
            foreach (var v in vdata)
            {
                var w = body.FindItem(v, "Bone Weights")?.Children.Select(c => c.Value.ToFloat()).ToArray() ?? [];
                var ix = body.FindItem(v, "Bone Indices")?.Children.Select(c => (int)c.Value.ToUInt()).ToArray() ?? [];
                float sum = w.Sum(); sums.Add(sum);
                if (sum < 1e-3f) zero++; else if (sum < 0.99f) low++;
                if (ix.Zip(w).Any(p => p.Second > 0 && p.First >= partBones)) badIndex++;
            }
            sb.AppendLine($"weights: {vdata.Count} vertices, zero total {zero}, below 0.99 {low}, index past the partition's {partBones} bones {badIndex}; sum range {sums.Min():F3}..{sums.Max():F3}");
            var data = body.GetRef(skin0, "Data")!;
            var list = body.FindItem(data, "Bone List")!.Children;
            var per = new float[vdata.Count];
            foreach (var bone in list)
                foreach (var vw in body.FindItem(bone, "Vertex Weights")?.Children ?? [])
                {
                    int i = (int)(body.FindItem(vw, "Index")?.Value.ToUInt() ?? 0);
                    if (i < per.Length) per[i] += body.FindItem(vw, "Weight")?.Value.ToFloat() ?? 0;
                }
            sb.AppendLine($"skin data: has weights {body.FindItem(data, "Has Vertex Weights")?.Value.ToUInt()}, vertices with zero total in NiSkinData {per.Count(x => x < 1e-3f)}");
        }
        File.WriteAllText(Path.Combine(mod, "consistency.txt"), sb.ToString());
        static string Fmt(Vector3 v) => $"({v.X,7:F2},{v.Y,7:F2},{v.Z,7:F2})";
    }
}
