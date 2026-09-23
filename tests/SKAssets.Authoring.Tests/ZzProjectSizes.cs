using System.Text;
using HKSK.Model;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Every actor project the game ships, by how much animation it carries.
public sealed class ZzProjectSizes
{
    [Fact]
    public void Dump()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? Path.GetTempPath();
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
        if (meshes.Length == 0) return;

        SkyrimCache caches;
        try { caches = SkyrimCache.Load(meshes); } catch { return; }
        var rows = new List<(string Name, int Animations, int Clips, int Behaviours)>();

        foreach (var project in caches.AnimationData.Projects)
        {
            string name = Path.GetFileNameWithoutExtension(project.Name);
            ActorProject? actor;
            try { actor = caches.OpenActor(name); } catch { continue; }
            if (actor is null || !actor.HasHavok) continue;
            rows.Add((name, actor.Animations.Count, actor.Clips.Count, actor.Behaviors.Count));
        }

        var sb = new StringBuilder();
        foreach (var row in rows.OrderBy(r => r.Animations))
            sb.AppendLine($"{row.Name,-34} {row.Animations,5} animations  {row.Clips,5} clips  {row.Behaviours,3} behaviours");
        sb.AppendLine($"== {rows.Count} projects");

        File.WriteAllText(Path.Combine(outDir, "project_sizes.txt"), sb.ToString());
    }
}
