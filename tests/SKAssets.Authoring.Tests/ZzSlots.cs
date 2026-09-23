using System.Text;
using HKSK.Model;
using Xunit;
namespace SKAssets.Authoring.Tests;

// A project's animation slots: what the graph can play, and which generators play each.
public sealed class ZzSlots
{
    [Fact]
    public void Dump()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? Path.GetTempPath();
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
        string project = Environment.GetEnvironmentVariable("PROJECT_NAME") ?? "";
        if (meshes.Length == 0 || project.Length == 0) return;

        SkyrimCache caches;
        ActorProject? actor;
        try { caches = SkyrimCache.Load(meshes); actor = caches.OpenActor(project); } catch { return; }
        if (actor is null) return;

        var sb = new StringBuilder();
        sb.AppendLine($"{project}: {actor.Animations.Count} animations, {actor.Clips.Count} clip generators");

        var byAnimation = actor.Clips.GroupBy(c => c.Entry.Name).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var animation in actor.Animations)
        {
            var plays = actor.Clips.Where(c => string.Equals(c.Slot?.StoredName ?? "", animation.StoredName, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Entry.Name).ToList();
            var m = animation.Motion;
            sb.AppendLine($"   {animation.StoredName,-54} {(m is null ? "" : $"{m.Duration,6:F2}s travel {m.Travel,7:F1}")}  "
                + $"[{string.Join(", ", plays)}]");
        }

        File.WriteAllText(Path.Combine(outDir, $"slots_{project}.txt"), sb.ToString());
    }
}
