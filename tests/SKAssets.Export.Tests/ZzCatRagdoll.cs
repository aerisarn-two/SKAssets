using System.Numerics;
using System.Text;
using HKFBX.Fbx;
using HKFBX.Hkx;
using HKFBX.Model;
using LeanMeshIO;
using NIFBX.Conversion;
using NIFSharp;
using SKAssets.Content.Assets;
using SKAssets.Export;
using Xunit;
namespace SKAssets.Export.Tests;

// Validates the cat's authored ragdoll FBX through the same readers the creature import
// uses: the Havok half read back, the NIF half built, and a re-export round trip.
public sealed class ZzCatRagdoll
{
    [Fact]
    public void Validate()
    {
        string dir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        string path = Environment.GetEnvironmentVariable("HKSK_PROBE_FBX") ?? Path.Combine(dir, "cat_ragdoll.fbx");
        var sb = new StringBuilder();
        var doc = FbxDocument.Load(path);

        // ---------------------------------------------------------------- Havok half
        SkeletonFile file = SkeletonExchange.ImportHavok(doc);
        sb.AppendLine($"rig bones {file.Rig.Bones.Count}: {string.Join(", ", file.Rig.Bones.Take(6).Select(b => $"{b.Name}<-{b.ParentIndex}"))} ...");
        sb.AppendLine($"bodies {file.Bodies.Count}, joints {file.Joints.Count}, ragdoll bones {file.Ragdoll?.Bones.Count}, rig->ragdoll {file.RigToRagdoll.Count}");
        // model-space frames of the rig, composed from the locals
        var world = new Matrix4x4[file.Rig.Bones.Count];
        for (int i = 0; i < world.Length; i++)
        {
            var b = file.Rig.Bones[i];
            var local = Matrix4x4.CreateScale(b.ReferencePose.Scale) * Matrix4x4.CreateFromQuaternion(b.ReferencePose.Rotation) * Matrix4x4.CreateTranslation(b.ReferencePose.Translation);
            world[i] = b.ParentIndex >= 0 ? local * world[b.ParentIndex] : local;
        }
        var index = file.Rig.Bones.Select((b, i) => (b.Name, i)).ToDictionary(x => x.Name, x => x.i, StringComparer.OrdinalIgnoreCase);
        float worst = 0; string worstName = "";
        foreach (var body in file.Bodies)
        {
            int i = index[body.RigBone!];
            float d = Vector3.Distance(world[i].Translation, body.Transform.Translation);
            if (d > worst) { worst = d; worstName = body.Name; }
        }
        sb.AppendLine($"body placement vs composed rig: worst {worst:F4} units at {worstName}");
        var ragdoll = file.Ragdoll!;
        int roots = ragdoll.Bones.Count(b => b.ParentIndex < 0);
        sb.AppendLine($"ragdoll roots {roots} (expect 1); parents: {string.Join(", ", ragdoll.Bones.Select(b => $"{b.Name.Replace("Ragdoll_", "")}<-{(b.ParentIndex < 0 ? "-" : ragdoll.Bones[b.ParentIndex].Name.Replace("Ragdoll_", ""))}"))}");
        var filters = file.Bodies.Select(b => b.CollisionFilterInfo).ToList();
        sb.AppendLine($"filters: zero {filters.Count(f => f == 0)}, distinct {filters.Distinct().Count()}, e.g. {string.Join(",", filters.Take(5).Select(f => $"0x{f:x}"))}");
        foreach (var body in file.Bodies.Take(4))
            sb.AppendLine($"  body {body.Name,-24} rig={body.RigBone,-12} T=({body.Transform.Translation.X:F2},{body.Transform.Translation.Y:F2},{body.Transform.Translation.Z:F2}) capsule a=({body.Shape!.VertexA.X:F2},{body.Shape.VertexA.Y:F2},{body.Shape.VertexA.Z:F2}) b=({body.Shape.VertexB.X:F2},{body.Shape.VertexB.Y:F2},{body.Shape.VertexB.Z:F2}) r={body.Shape.Radius:F2} invMass={body.InverseMass:F3} mt={body.MotionType} q={body.QualityType} ii=({body.InverseInertia.X:F4},{body.InverseInertia.Y:F4},{body.InverseInertia.Z:F4})");
        foreach (var j in file.Joints.Take(3))
            sb.AppendLine($"  joint {j.BodyA,-22} <- {j.BodyB,-22} kind={j.Kind} cone={j.ConeMaxAngle:F2} plane=[{j.PlaneMinAngle:F2},{j.PlaneMaxAngle:F2}] twist=[{j.TwistMinAngle:F2},{j.TwistMaxAngle:F2}] pivotB=({j.FrameB.Translation.X:F2},{j.FrameB.Translation.Y:F2},{j.FrameB.Translation.Z:F2}) twistB=({j.FrameB.M11:F2},{j.FrameB.M12:F2},{j.FrameB.M13:F2}) twistA=({j.FrameA.M11:F2},{j.FrameA.M12:F2},{j.FrameA.M13:F2})");
        // joint consistency: A frame in the child body and B frame in the parent body must coincide in model space
        var bodyWorld = file.Bodies.ToDictionary(b => b.Name, b => Matrix4x4.CreateFromQuaternion(b.Transform.Rotation) * Matrix4x4.CreateTranslation(b.Transform.Translation), StringComparer.OrdinalIgnoreCase);
        float worstJoint = 0;
        foreach (var j in file.Joints)
        {
            var a = j.FrameA * bodyWorld[j.BodyA]; var b = j.FrameB * bodyWorld[j.BodyB];
            worstJoint = MathF.Max(worstJoint, Vector3.Distance(a.Translation, b.Translation) + Vector3.Distance(new Vector3(a.M11, a.M12, a.M13), new Vector3(b.M11, b.M12, b.M13)));
        }
        sb.AppendLine($"joint frames A vs B in model space: worst mismatch {worstJoint:F4}");
        // Non-adjacent bodies collide with each other, so none may overlap at rest.
        static float SegDist(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s_, t;
            if (a <= 1e-9f && e <= 1e-9f) return r.Length();
            if (a <= 1e-9f) { s_ = 0; t = Math.Clamp(f / e, 0, 1); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-9f) { t = 0; s_ = Math.Clamp(-c / a, 0, 1); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s_ = denom != 0 ? Math.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                    t = (b * s_ + f) / e;
                    if (t < 0) { t = 0; s_ = Math.Clamp(-c / a, 0, 1); }
                    else if (t > 1) { t = 1; s_ = Math.Clamp((b - c) / a, 0, 1); }
                }
            }
            return Vector3.Distance(p1 + d1 * s_, p2 + d2 * t);
        }
        var parentOf = file.Joints.ToDictionary(j => j.BodyA, j => j.BodyB, StringComparer.OrdinalIgnoreCase);
        var segs = file.Bodies.Where(b => b.Shape is not null).Select(b => (b.Name, A: Vector3.Transform(b.Shape!.VertexA, bodyWorld[b.Name]), B: Vector3.Transform(b.Shape.VertexB, bodyWorld[b.Name]), R: b.Shape.Radius)).ToList();
        int overlaps = 0; float minGap = float.MaxValue; string minPair = "";
        for (int i = 0; i < segs.Count; i++)
            for (int k = i + 1; k < segs.Count; k++)
            {
                var (n1, a1, b1, r1) = segs[i]; var (n2, a2, b2, r2) = segs[k];
                bool adjacent = (parentOf.TryGetValue(n1, out var p1) && p1.Equals(n2, StringComparison.OrdinalIgnoreCase)) || (parentOf.TryGetValue(n2, out var p2) && p2.Equals(n1, StringComparison.OrdinalIgnoreCase));
                if (adjacent) continue;
                float gap = SegDist(a1, b1, a2, b2) - r1 - r2;
                if (gap < 0) overlaps++;
                if (gap < minGap) { minGap = gap; minPair = $"{n1} / {n2}"; }
            }
        sb.AppendLine($"non-adjacent capsule pairs: {overlaps} overlapping; smallest clearance {minGap:F2} units at {minPair}");

        // ---------------------------------------------------------------- template write
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
        string template = Path.Combine(meshes, "actors/skeever/character assets/skeleton.hkx");
        if (File.Exists(template))
        {
            var unmatched = HkxSkeletonFile.Write(template, file, Path.Combine(dir, "cat_over_skeever.hkx"));
            sb.AppendLine($"HkxSkeletonFile.Write over the skeever template: {unmatched.Count} names the template had no place for (of {file.Rig.Bones.Count + file.Bodies.Count})");
        }

        // ---------------------------------------------------------------- NIF half
        var schema = NifXmlDatabase.LoadEmbedded();
        NifModel nif = SkeletonExchange.ImportMesh(doc, schema);
        string nifPath = Path.Combine(dir, "cat_skeleton.nif");
        nif.Save(nifPath);
        var reloaded = NifModel.Load(nifPath, schema);
        var byType = reloaded.Blocks.GroupBy(b => b.Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
        sb.AppendLine($"nif blocks: {string.Join(", ", byType)}");
        var nodeNames = reloaded.Blocks.Where(b => b.Name is "NiNode" or "BSFadeNode").Select(b => reloaded.GetString(b, "Name")).ToList();
        sb.AppendLine($"nif nodes ({nodeNames.Count}): {string.Join(", ", nodeNames.Take(8))} ...");
        var findings = SkeletonRules.CheckRig(file.Rig.Bones.Select(b => b.Name), nodeNames);
        sb.AppendLine($"CheckRig: {findings.Count} findings {string.Join("; ", findings.Take(3))}");

        // ---------------------------------------------------------------- round trip
        var again = SkeletonExchange.Export(nif, file);
        string rt = Path.Combine(dir, "cat_roundtrip.fbx"); again.Save(rt);
        var back = SkeletonExchange.ImportHavok(FbxDocument.Load(rt));
        sb.AppendLine($"round trip: rig {back.Rig.Bones.Count}, bodies {back.Bodies.Count}, joints {back.Joints.Count}, ragdoll roots {back.Ragdoll?.Bones.Count(b => b.ParentIndex < 0)}");
        File.WriteAllText(Path.Combine(dir, "cat_validation.txt"), sb.ToString());
    }
}
