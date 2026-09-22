using System.Text;
using HKSK.Engine;
using Xunit;
namespace SKAssets.Authoring.Tests;

// The cat's graph driven the way the engine drives it: which clips an event reaches.
public sealed class ZzCatDrive
{
    [Fact]
    public void Drive()
    {
        string mod = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        if (mod.Length == 0) return;
        string project = Path.Combine(mod, "Data", "Meshes", "actors", "HouseCat", "HouseCatProject.hkx");
        if (!File.Exists(project)) return;
        var sb = new StringBuilder();

        string[] Clips(Events events, float speed = 0f)
        {
            Evaluation run = ActiveGenerators.Of(project, tables =>
            {
                foreach (Variables variables in tables.Values) variables.Set("Speed", speed);
            }, events);
            return [.. run.Active.Where(a => a.Generator is HKX2.hkbClipGenerator)
                .Select(a => $"{a.Name}={((HKX2.hkbClipGenerator)a.Generator).m_animationName}")];
        }

        foreach (var (what, events, speed) in new (string, Events, float)[]
        {
            ("at rest", Events.None, 0f),
            ("moving", Events.Of("moveStart", "moveForward"), 60f),
            ("running", Events.Of("moveStart", "moveForward"), 300f),
            ("attack 1", Events.Of("attackStart_Attack1"), 0f),
            ("attack 2", Events.Of("attackStart_Attack2"), 0f),
            ("attack left", Events.Of("attackStart_AttackLeft1"), 0f),
            ("attack right", Events.Of("attackStart_AttackRight1"), 0f),
            ("power attack", Events.Of("attackStart_ForwardPower"), 0f),
            ("recoil", Events.Of("recoilStart"), 0f),
            ("stagger", Events.Of("staggerStart"), 0f),
            ("swim", Events.Of("swimStart"), 0f),
            ("death", Events.Of("DeathAnimation"), 0f),
            ("sneak", Events.Of("catSneakStart"), 0f),
            ("fall", Events.Of("catFall"), 0f),
        })
        {
            string[] clips = Clips(events, speed);
            sb.AppendLine($"{what,-14} [{string.Join(", ", events.Names)}] -> {(clips.Length == 0 ? "NOTHING" : string.Join(", ", clips))}");
        }

        File.WriteAllText(Path.Combine(mod, "drive.txt"), sb.ToString());
    }
}
