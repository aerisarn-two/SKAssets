using System.Numerics;
using System.Text;
using HKSK.Model;
using Xunit;
namespace SKAssets.Authoring.Tests;

// How far a clip's root actually turns, measured two ways: from its last sample, which is
// what a single quaternion can say, and by following every sample, which is the only way
// to see a turn of more than half a circle.
public sealed class ZzTurnRates
{
    [Fact]
    public void Measure()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string meshes = Environment.GetEnvironmentVariable("MESHES") ?? "";
        string project = Environment.GetEnvironmentVariable("PROJECT_NAME") ?? "";
        if (outDir.Length == 0 || meshes.Length == 0 || project.Length == 0) return;

        SkyrimCache caches;
        ActorProject? actor;
        try { caches = SkyrimCache.Load(meshes); actor = caches.OpenActor(project); } catch { return; }
        if (actor is null) return;
        var sb = new StringBuilder();

        static float Yaw(Quaternion q) =>
            MathF.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 180f / MathF.PI;

        foreach (string name in (Environment.GetEnvironmentVariable("TURN_CLIPS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var animation = actor.Animations.FirstOrDefault(a =>
                Path.GetFileNameWithoutExtension(a.StoredName.Replace('\\', '/')).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (animation?.Motion is not { } m || m.Duration <= 0 || m.Rotations.Count == 0)
            { sb.AppendLine($"{name}: no motion"); continue; }

            float last = Yaw(m.Rotations[^1].Value);
            float followed = 0f, previous = Yaw(m.Rotations[0].Value);
            foreach (var sample in m.Rotations.Skip(1))
            {
                float here = Yaw(sample.Value);
                float step = here - previous;
                if (step > 180f) step -= 360f;
                if (step < -180f) step += 360f;
                followed += step;
                previous = here;
            }

            sb.AppendLine($"{name,-22} {m.Duration:F2}s, {m.Rotations.Count,3} samples, travel {m.Travel,6:F1}: "
                + $"last sample says {last,7:F1} deg ({last / m.Duration,7:F1} deg/s), "
                + $"followed says {followed,7:F1} deg ({followed / m.Duration,7:F1} deg/s)");
        }

        File.WriteAllText(Path.Combine(outDir, $"turn_rates_{project}.txt"), sb.ToString());
    }
}
