using System.Text;
using HKSK.Model;
using Xunit;
namespace SKAssets.Authoring.Tests;

// The events the cat's attack clips carry, beside the sabre cat's own.
public sealed class ZzCatAttackEvents
{
    [Fact]
    public void Compare()
    {
        string mod = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
        if (mod.Length == 0 || meshes.Length == 0) return;
        var sb = new StringBuilder();

        void Show(string label, ActorProject project, IEnumerable<string> clipNames)
        {
            sb.AppendLine($"== {label}");
            foreach (string name in clipNames)
            {
                Clip? clip = project.Clip(name);
                if (clip is null) { sb.AppendLine($"  {name}: no clip"); continue; }
                AnimationSlot slot = project.Animations[clip.Entry.CacheIndex];
                string path = project.AnimationPath(slot) ?? "";
                var (duration, tracks) = File.Exists(path)
                    ? HKSK.Havok.UncompressedAnimation.Events(path)
                    : (0f, []);
                sb.AppendLine($"  {name,-24} {slot.StoredName,-44} duration {duration,6:F2} speed {clip.Entry.PlaybackSpeed}");
                sb.AppendLine($"     cache events: {string.Join(", ", clip.Entry.Events.Select(e => $"{e.Name}@{e.Time:F2}"))}");
                sb.AppendLine($"     annotations:  {string.Join(", ", tracks.SelectMany(t => t.Events).Select(e => $"{e.Text}@{e.Time:F2}"))}");
            }
        }

        string[] attacks = ["Attack1", "Attack2", "AttackLeft1", "AttackLeft2", "AttackRight1", "AttackRight2", "AttackPowerForward", "AttackPowerForward_Short", "Recoil", "StaggerBackSmall"];
        Show("cat", SkyrimCache.Load(Path.Combine(mod, "Data", "Meshes")).OpenActor("HouseCatProject")!, attacks);
        Show("sabre cat", SkyrimCache.Load(meshes).OpenActor("SabreCatProject")!, attacks);
        File.WriteAllText(Path.Combine(mod, "attack_events.txt"), sb.ToString());
    }
}
