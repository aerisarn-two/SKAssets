using System.Collections.Immutable;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using SKAssets.Authoring;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    // The records for a creature whose Havok project was assembled rather than copied, written
    // against the real masters and read back the way anything else reads a plugin.
    public class ZzAssembledPlugin
    {
        [MastersFact]
        public void Dump()
        {
            string outDir = Environment.GetEnvironmentVariable("SK_OUT")
                ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "sk-bought-dump")).FullName;

            SKAssets.Plugins.MutagenRuntime.Prepare();

            var order = SKAssets.Content.Havok.GameRecordReader.Masters
                .Select(f => Path.Combine(Game.Data!, f)).Where(File.Exists)
                .Select(f => (ISkyrimModGetter)SkyrimMod.CreateFromBinaryOverlay(f, SkyrimRelease.SkyrimSE))
                .ToList();

            string folder = Path.Combine(outDir, "assembled");
            Directory.CreateDirectory(folder);

            var sb = new StringBuilder();

            // Which NPCs the template race has, so the test names one that exists.
            var cacheBefore = order.ToImmutableLinkCache();
            var theirs = order.SelectMany(m => m.Npcs)
                .Where(n => cacheBefore.TryResolve<IRaceGetter>(n.Race.FormKey, out var r)
                            && string.Equals(r.EditorID, "SkeletonRace", StringComparison.OrdinalIgnoreCase))
                .Select(n => n.EditorID ?? "")
                .Where(e => e.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();

            sb.AppendLine($"the template race has {theirs.Count} NPCs: {string.Join(", ", theirs.Take(10))}");
            sb.AppendLine();

            using var authoring = new PluginAuthoring(order, "BoneSkeleton.esp", folder);

            CreatureResult made = authoring.ImportAssembledCreature(new AssembledCreature(
                Template: "SkeletonRace",
                Name: "BoneSkeleton",
                ProjectFolder: @"actors\BoneSkeleton",
                ProjectFile: "BoneSkeletonProject.hkx",
                BehaviourFile: @"behaviors\BoneSkeletonBehavior.hkx",
                TemplateFolder: @"actors\draugr",
                SkeletonModel: @"actors\BoneSkeleton\character assets\skeleton.nif",
                Movement: ("BoneSkeletonDefault",
                    new MovementSpeeds(70.91f, 160.69f, 33.57f, 33.57f, 12.1f, 12.1f, 14.1f, 14.1f)),
                Attacks: ["SwingHeavy", "SwingNormal", "SwingQuick"],
                Npc: theirs.FirstOrDefault(),
                DisplayName: "Bone Skeleton"));

            sb.AppendLine($"project: {made.Project}");
            sb.AppendLine($"{made.Records.Count} records:");
            foreach (var group in made.Records.GroupBy(r => r.Type).OrderByDescending(g => g.Count()))
                sb.AppendLine($"  {group.Key} x{group.Count()}");
            sb.AppendLine();
            sb.AppendLine("notes:");
            foreach (string n in made.Notes) sb.AppendLine($"  {n}");

            authoring.Save();

            // Read back the way the Creation Kit would.
            string esp = Path.Combine(folder, "BoneSkeleton.esp");
            sb.AppendLine();
            sb.AppendLine($"--- {esp} ({new FileInfo(esp).Length} bytes)");

            var plugin = SkyrimMod.CreateFromBinaryOverlay(esp, SkyrimRelease.SkyrimSE);
            var cache = order.Append(plugin).ToImmutableLinkCache();
            string Name(IFormLinkGetter link) => link.IsNull ? "-"
                : cache.TryResolveIdentifier(link.FormKey, out string? id) ? id ?? "?" : link.FormKey.ToString();

            foreach (var race in plugin.Races)
            {
                sb.AppendLine($"RACE {race.EditorID} '{race.Name}'");
                sb.AppendLine($"  behaviour = {race.BehaviorGraph?.Male?.File}");
                sb.AppendLine($"  skeleton  = {race.SkeletalModel?.Male?.File}");
                sb.AppendLine($"  skin      = {Name(race.Skin)}");
                sb.AppendLine($"  bodyParts = {Name(race.BodyPartData)}");
                sb.AppendLine($"  walk      = {Name(race.BaseMovementDefaultWalk)}");
                sb.AppendLine($"  run       = {Name(race.BaseMovementDefaultRun)}");
            }

            foreach (var race in plugin.Races)
            {
                sb.AppendLine($"  attacks ({race.Attacks.Count}): " +
                    string.Join(", ", race.Attacks.Select(a => a.AttackEvent)));
            }

            foreach (var movt in plugin.MovementTypes)
                sb.AppendLine($"MOVT {movt.EditorID} '{movt.Name}' fwd {movt.ForwardWalk}/{movt.ForwardRun} " +
                              $"back {movt.BackWalk}/{movt.BackRun} left {movt.LeftWalk} right {movt.RightWalk}");

            var idles = plugin.IdleAnimations.ToList();
            sb.AppendLine($"{idles.Count} idles; the files they name:");
            foreach (var group in idles.Where(i => i.Filename?.GivenPath is { Length: > 0 })
                         .GroupBy(i => i.Filename!.GivenPath))
                sb.AppendLine($"  {group.Count()} x {group.Key}");

            sb.AppendLine($"idles naming no file of their own: {idles.Count(i => i.Filename?.GivenPath is not { Length: > 0 })}");

            foreach (var npc in plugin.Npcs)
                sb.AppendLine($"NPC {npc.EditorID} race={Name(npc.Race)} attackRace={Name(npc.AttackRace)} worn={Name(npc.WornArmor)}");

            File.WriteAllText(Path.Combine(outDir, "assembled-plugin.txt"), sb.ToString());
        }
    }
}
