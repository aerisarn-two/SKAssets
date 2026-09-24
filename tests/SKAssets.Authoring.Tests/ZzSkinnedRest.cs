using System.Numerics;
using System.Text;
using NIFSharp;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    // Where the body actually ends up once the skeleton poses it. Not where its vertices are
    // stored and not where its bind pose was taken -- where the game will draw it.
    public class ZzSkinnedRest
    {
        [Fact]
        public void Dump()
        {
            string mod = Environment.GetEnvironmentVariable("SKELETON_MOD") ?? "";
            if (mod.Length == 0) return;

            string here = Path.Combine(mod, "Meshes", "actors", "BoneSkeleton");
            string bodyPath = Path.Combine(here, "boneskeleton.nif");
            string rigPath = Path.Combine(here, "character assets", "skeleton.nif");
            if (!File.Exists(bodyPath) || !File.Exists(rigPath)) return;

            string outDir = Environment.GetEnvironmentVariable("SK_OUT")
                ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "sk-bought-dump")).FullName;

            var db = NifXmlDatabase.LoadEmbedded();
            NifModel body = NifModel.Load(bodyPath, db), rig = NifModel.Load(rigPath, db);
            var sb = new StringBuilder();

            // Where the skeleton puts each bone.
            var bones = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            Walk(rig, rig.Blocks[0], Matrix4x4.Identity, bones);

            Vector3 rigLow = new(float.MaxValue), rigHigh = new(float.MinValue);
            foreach (var b in bones.Values)
            {
                rigLow = Vector3.Min(rigLow, b.Translation);
                rigHigh = Vector3.Max(rigHigh, b.Translation);
            }

            sb.AppendLine($"the skeleton's {bones.Count} nodes span");
            sb.AppendLine($"  x {rigLow.X,8:0.#} to {rigHigh.X,8:0.#}");
            sb.AppendLine($"  y {rigLow.Y,8:0.#} to {rigHigh.Y,8:0.#}");
            sb.AppendLine($"  z {rigLow.Z,8:0.#} to {rigHigh.Z,8:0.#}");
            sb.AppendLine();

            foreach (NifItem shape in body.Blocks.Where(b =>
                b.Name is "BSTriShape" or "BSSubIndexTriShape" or "BSDynamicTriShape" or "NiTriShape"))
            {
                if (body.GetRef(shape, "Skin") is not { } skin || body.GetRef(skin, "Data") is not { } data) continue;

                var stored = Positions(body, shape).ToList();
                if (stored.Count == 0) continue;

                Vector3 storedLow = stored.Aggregate(Vector3.Min), storedHigh = stored.Aggregate(Vector3.Max);
                sb.AppendLine($"'{body.GetString(shape, "Name")}': {stored.Count} vertices as stored span");
                sb.AppendLine($"  x {storedLow.X,8:0.#} to {storedHigh.X,8:0.#}");
                sb.AppendLine($"  y {storedLow.Y,8:0.#} to {storedHigh.Y,8:0.#}");
                sb.AppendLine($"  z {storedLow.Z,8:0.#} to {storedHigh.Z,8:0.#}");

                Matrix4x4 overall = body.FindItem(data, "Skin Transform") is { } o
                    ? body.GetTransform(o).ToMatrix() : Matrix4x4.Identity;

                // Pose every vertex the way the engine does: out of object space by the bone's
                // own skin transform, then into the world by where the skeleton puts that bone.
                var moved = new Vector3[stored.Count];
                var weight = new float[stored.Count];
                var list = body.FindItem(data, "Bone List")?.Children ?? [];
                var nodes = body.GetRefArray(skin, "Bones").ToList();
                int missing = 0;

                for (int i = 0; i < nodes.Count && i < list.Count; i++)
                {
                    string name = body.GetName(nodes[i]);
                    if (!bones.TryGetValue(name, out Matrix4x4 standing)) { missing++; continue; }
                    if (body.FindItem(list[i], "Skin Transform") is not { } st) continue;

                    Matrix4x4 into = overall * body.GetTransform(st).ToMatrix() * standing;

                    foreach (NifItem w in body.FindItem(list[i], "Vertex Weights")?.Children ?? [])
                    {
                        int at = (int)body.GetUInt(w, "Index");
                        if (at >= stored.Count) continue;
                        if (body.FindItem(w, "Weight") is not { } f
                            || !float.TryParse(f.Value.ToString(), out float amount) || amount <= 0) continue;

                        moved[at] += Vector3.Transform(stored[at], into) * amount;
                        weight[at] += amount;
                    }
                }

                var posed = moved.Select((p, i) => weight[i] > 0 ? p / weight[i] : p).ToList();
                Vector3 low = posed.Aggregate(Vector3.Min), high = posed.Aggregate(Vector3.Max);

                sb.AppendLine($"  bones the skeleton has not got: {missing}");
                sb.AppendLine($"  posed by the skeleton, it spans");
                sb.AppendLine($"    x {low.X,8:0.#} to {high.X,8:0.#}   (skeleton {rigLow.X:0.#} to {rigHigh.X:0.#})");
                sb.AppendLine($"    y {low.Y,8:0.#} to {high.Y,8:0.#}   (skeleton {rigLow.Y:0.#} to {rigHigh.Y:0.#})");
                sb.AppendLine($"    z {low.Z,8:0.#} to {high.Z,8:0.#}   (skeleton {rigLow.Z:0.#} to {rigHigh.Z:0.#})");

                Vector3 centreBody = (low + high) / 2, centreRig = (rigLow + rigHigh) / 2;
                sb.AppendLine($"  its middle is ({centreBody.X:0.#},{centreBody.Y:0.#},{centreBody.Z:0.#}) " +
                              $"against the skeleton's ({centreRig.X:0.#},{centreRig.Y:0.#},{centreRig.Z:0.#}), " +
                              $"{(centreBody - centreRig).Length():0.##} units apart");
            }

            // The thing that matters: where the body's own copy of each bone stands, against
            // where the skeleton stands it. A shipped body's two agree exactly.
            sb.AppendLine();
            sb.AppendLine("--- the body's own bone nodes, against the skeleton's");

            var mine = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            Walk(body, body.Blocks[0], Matrix4x4.Identity, mine);

            var apart = new List<(string Bone, float How)>();
            foreach ((string name, Matrix4x4 standing) in bones)
                if (mine.TryGetValue(name, out Matrix4x4 ours))
                    apart.Add((name, (ours.Translation - standing.Translation).Length()));

            if (apart.Count == 0) sb.AppendLine("  no bone is in both files");
            else
            {
                var worst = apart.OrderByDescending(a => a.How).First();
                sb.AppendLine($"  {apart.Count} bones in both; the worst is '{worst.Bone}' at {worst.How:0.####} units");
                foreach (var a in apart.OrderByDescending(a => a.How).Take(4))
                    sb.AppendLine($"    {a.Bone,-30} {a.How,8:0.####}");
            }

            // What the body carries of its own: a shipped one names the bones it is weighted
            // to and does not bring copies of them.
            sb.AppendLine();
            sb.AppendLine("--- the nodes each body carries");

            foreach ((string what, string path) in new[]
                     {
                         ("ours", bodyPath),
                         ("wolf", Path.Combine(Game.Meshes ?? "", "actors", "canine", "character assets wolf", "wolf.nif")),
                         ("draugr", Path.Combine(Game.Meshes ?? "", "actors", "draugr", "draugrmale01.nif")),
                         ("human", Path.Combine(Game.Meshes ?? "", "actors", "character", "character assets", "malebody_1.nif")),
                     })
            {
                if (!File.Exists(path)) { sb.AppendLine($"  {what}: absent"); continue; }

                NifModel m = NifModel.Load(path, db);
                var theirs = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
                Walk(m, m.Blocks[0], Matrix4x4.Identity, theirs);

                var skinned = m.Blocks
                    .Where(b => b.Name is "NiSkinInstance" or "BSDismemberSkinInstance")
                    .SelectMany(sk => m.GetRefArray(sk, "Bones"))
                    .Select(m.GetName)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                sb.AppendLine($"  {what}: {theirs.Count} nodes, skinned to {skinned.Count} bones; " +
                              $"root '{m.GetString(m.Blocks[0], "Name")}'");
                sb.AppendLine($"    bones that are also nodes in the file: " +
                              $"{skinned.Count(b => theirs.ContainsKey(b))} of {skinned.Count}");
            }

            File.WriteAllText(Path.Combine(outDir, "skinned-rest.txt"), sb.ToString());
        }

        private static void Walk(NifModel m, NifItem at, Matrix4x4 parent, Dictionary<string, Matrix4x4> into)
        {
            Matrix4x4 world = m.GetTransform(at).ToMatrix() * parent;
            if (m.GetString(at, "Name") is { Length: > 0 } name) into[name] = world;
            foreach (NifItem link in m.FindItem(at, "Children")?.Children ?? [])
                if (int.TryParse(link.Value.ToString(), out int i) && i >= 0 && i < m.Blocks.Count)
                    Walk(m, m.Blocks[i], world, into);
        }

        private static IEnumerable<Vector3> Positions(NifModel model, NifItem shape)
        {
            NifItem? buffer = model.GetRef(shape, "Skin") is { } skin
                && model.GetRef(skin, "Skin Partition") is { } partition
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
    }
}
