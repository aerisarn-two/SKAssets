using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Races whose name matches, with the project they run and the size they are.
public sealed class ZzRaces
{
    [MastersFact]
    public void Dump()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? Path.GetTempPath();
        string want = Environment.GetEnvironmentVariable("RACE_LIKE") ?? "";
        if (want.Length == 0) return;
        SKAssets.Plugins.MutagenRuntime.Prepare();

        var order = SKAssets.Content.Havok.GameRecordReader.Masters.Select(f => Path.Combine(Game.Data!, f)).Where(File.Exists)
            .Select(f => (ISkyrimModGetter)SkyrimMod.CreateFromBinaryOverlay(f, SkyrimRelease.SkyrimSE)).ToList();
        var cache = order.ToImmutableLinkCache();
        var sb = new StringBuilder();

        foreach (var race in cache.PriorityOrder.WinningOverrides<IRaceGetter>())
        {
            string id = race.EditorID ?? "";
            if (!want.Split(';').Any(w => id.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;

            sb.AppendLine($"{id}: graph={race.BehaviorGraph?.Male?.File} skeleton={race.SkeletalModel?.Male?.File}");
            sb.AppendLine($"   size={race.Size} height={race.Height?.Male} weight={race.Weight?.Male} mass={race.BaseMass} "
                + $"reach={race.UnarmedReach} damage={race.UnarmedDamage} attacks={race.Attacks.Count} flags={race.Flags}");
        }

        File.WriteAllText(Path.Combine(outDir, "races.txt"), sb.ToString());
    }
}
