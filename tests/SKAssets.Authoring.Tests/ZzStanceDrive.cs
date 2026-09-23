using System.Text;
using HKSK.Engine;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Two projects driven through the same events, side by side: ours and the one it was
// copied from. A row where they reach different clips is where the copy went wrong.
public sealed class ZzStanceDrive
{
    [Fact]
    public void Compare()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string ours = Environment.GetEnvironmentVariable("PROJECT_OURS") ?? "";
        string theirs = Environment.GetEnvironmentVariable("PROJECT_THEIRS") ?? "";
        if (outDir.Length == 0 || !File.Exists(ours) || !File.Exists(theirs)) return;
        var sb = new StringBuilder();

        string Clips(string project, string[] events, float speed, (string Name, float Value)[] set)
        {
            try
            {
                Evaluation run = ActiveGenerators.Of(project, tables =>
                {
                    foreach (Variables variables in tables.Values)
                    {
                        variables.Set("Speed", speed);
                        foreach ((string name, float value) in set) variables.Set(name, value);
                    }
                }, Events.Of(events));

                var clips = run.Active.Where(a => a.Generator is HKX2.hkbClipGenerator)
                    .Select(a => a.Name).Order(StringComparer.Ordinal).ToList();
                return clips.Count == 0 ? "NOTHING" : string.Join(", ", clips);
            }
            catch (Exception e) { return $"threw {e.GetType().Name}: {e.Message}"; }
        }

        (string What, string[] Events, float Speed, (string, float)[] Set)[] cases =
        [
            ("rest", [], 0f, []),
            ("stance on", ["combatStanceStart"], 0f, []),
            ("stance then attack", ["combatStanceStart", "attackStart_Attack1"], 0f, []),
            ("attack alone", ["attackStart_Attack1"], 0f, []),
            ("stance then move", ["combatStanceStart", "moveStart", "moveForward"], 60f, []),
            ("stance then run", ["combatStanceStart", "moveStart", "moveForward"], 300f, []),
            ("move alone", ["moveStart", "moveForward"], 60f, []),
            ("run alone", ["moveStart", "moveForward"], 300f, []),
            ("move, drawn", ["moveStart", "moveForward"], 60f, [("bIsSynced", 0f), ("IsAttacking", 0f), ("bWantCastRight", 0f)]),
            ("stance off", ["combatStanceStop"], 0f, []),
        ];

        foreach (var (what, events, speed, set) in cases)
        {
            string mine = Clips(ours, events, speed, set);
            string was = Clips(theirs, events, speed, set);
            string mark = string.Equals(Plain(mine), was, StringComparison.Ordinal) ? "" : "   <== DIFFERS";
            sb.AppendLine($"{what,-20} speed {speed,5}");
            sb.AppendLine($"    ours   {mine}");
            sb.AppendLine($"    theirs {was}{mark}");
        }

        File.WriteAllText(Path.Combine(outDir, "stance_drive.txt"), sb.ToString());

        static string Plain(string s) => s.Replace("HouseCat", "SabreCat", StringComparison.OrdinalIgnoreCase);
    }
}
