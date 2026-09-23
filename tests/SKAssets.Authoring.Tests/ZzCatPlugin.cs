using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace SKAssets.Authoring.Tests;

// What the written plugin says about the cat: its race, its attacks, its NPC.
public sealed class ZzCatPlugin
{
    [MastersFact]
    public void Dump()
    {
        string mod = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string esp = Path.Combine(mod, "Data", "CatSimple.esp");
        if (!File.Exists(esp)) return;
        SKAssets.Plugins.MutagenRuntime.Prepare();

        var order = SKAssets.Content.Havok.GameRecordReader.Masters.Select(f => Path.Combine(Game.Data!, f)).Where(File.Exists)
            .Select(f => (ISkyrimModGetter)SkyrimMod.CreateFromBinaryOverlay(f, SkyrimRelease.SkyrimSE)).ToList();
        var plugin = SkyrimMod.CreateFromBinaryOverlay(esp, SkyrimRelease.SkyrimSE);
        var cache = order.Append(plugin).ToImmutableLinkCache();
        var sb = new StringBuilder();

        string Name(IFormLinkGetter link) => link.IsNull ? "-" : cache.TryResolveIdentifier(link.FormKey, out string? id) ? $"{id ?? "?"} {link.FormKey}" : link.FormKey.ToString();

        foreach (var race in plugin.Races)
        {
            sb.AppendLine($"RACE {race.EditorID}: attacks={race.Attacks.Count} skin={Name(race.Skin)} bodyParts={Name(race.BodyPartData)} attackRace={Name(race.AttackRace)} reach={race.UnarmedReach} damage={race.UnarmedDamage} size={race.Size} flags={race.Flags}");
            foreach (var a in race.Attacks)
                sb.AppendLine($"   attack {a.AttackEvent} spell={Name(a.AttackData!.Spell)} flags={a.AttackData.Flags} angle={a.AttackData.AttackAngle} strike={a.AttackData.StrikeAngle} mult={a.AttackData.DamageMult} type={Name(a.AttackData.AttackType)}");
            sb.AppendLine($"   behaviour={race.BehaviorGraph?.Male?.File} skeleton={race.SkeletalModel?.Male?.File} movement=[{string.Join(", ", race.MovementTypeNames)}]");
            foreach (var (what, link) in new (string, IFormLinkGetter)[] { ("walk", race.BaseMovementDefaultWalk), ("run", race.BaseMovementDefaultRun), ("sneak", race.BaseMovementDefaultSneak), ("swim", race.BaseMovementDefaultSwim) })
                sb.AppendLine($"   default {what}: {Name(link)}");
        }

        foreach (var npc in plugin.Npcs)
            sb.AppendLine($"NPC {npc.EditorID}: race={Name(npc.Race)} attackRace={Name(npc.AttackRace)} worn={Name(npc.WornArmor)} class={Name(npc.Class)} combat={Name(npc.CombatStyle)} aggro={npc.AIData?.Aggression} confidence={npc.AIData?.Confidence} flags={npc.Configuration.Flags} template={Name(npc.Template)} templateFlags={npc.Configuration.TemplateFlags}");

        foreach (var armor in plugin.Armors)
            sb.AppendLine($"ARMO {armor.EditorID}: race={Name(armor.Race)} armature={string.Join(", ", armor.Armature.Select(Name))}");
        foreach (var aa in plugin.ArmorAddons)
            sb.AppendLine($"ARMA {aa.EditorID}: race={Name(aa.Race)} priority={aa.WeightSliderEnabled} parts={string.Join("/", aa.BodyTemplate?.FirstPersonFlags ?? default)} male={aa.WorldModel?.Male?.File} additional={aa.AdditionalRaces.Count}");
        foreach (var idle in plugin.IdleAnimations)
            sb.AppendLine($"IDLE {idle.EditorID}: event={idle.AnimationEvent} file={idle.Filename?.GivenPath}");
        sb.AppendLine($"IDLE total {plugin.IdleAnimations.Count}");

        foreach (string id in new[] { "EncSabreCat", "EncSabreCatSnowy" })
            if (cache.TryResolve<INpcGetter>(id, out var vanilla))
                sb.AppendLine($"vanilla NPC {vanilla.EditorID}: race={Name(vanilla.Race)} attackRace={Name(vanilla.AttackRace)} combat={Name(vanilla.CombatStyle)} aggro={vanilla.AIData?.Aggression} confidence={vanilla.AIData?.Confidence} assistance={vanilla.AIData?.Assistance} template={Name(vanilla.Template)} templateFlags={vanilla.Configuration.TemplateFlags} flags={vanilla.Configuration.Flags} factions={vanilla.Factions.Count} packages={vanilla.Packages.Count}");
        foreach (var npc in plugin.Npcs)
            sb.AppendLine($"ours NPC {npc.EditorID}: assistance={npc.AIData?.Assistance} factions={npc.Factions.Count} packages={npc.Packages.Count} deathItem={Name(npc.DeathItem)} voice={Name(npc.Voice)}");
        // The race the cat was copied from, in the same shape as ours above: what the copy
        // lost is what the attacks would be missing.
        foreach (string id in new[] { "SabreCatNoSpeed", "SabreCatTurnInPlace", "SabreCatPathEndNoSpeed" })
            if (cache.TryResolve<IIdleAnimationGetter>(id, out var v))
                sb.AppendLine($"vanilla IDLE {v.EditorID}: event={v.AnimationEvent} file='{v.Filename?.GivenPath}' related=[{string.Join(", ", v.RelatedIdles.Select(Name))}] conditions={v.Conditions.Count}");

        // Everything the cat's records still point at in the template's: a link left on the
        // sabre cat is the copy having missed something, and reads here by name.
        foreach (var record in plugin.EnumerateMajorRecords())
        {
            var left = record.EnumerateFormLinks()
                .Where(l => !l.IsNull && !l.FormKey.ModKey.Equals(plugin.ModKey))
                .Select(l => cache.TryResolveIdentifier(l.FormKey, out string? name) ? name : null)
                .Where(n => n is not null && n.Contains("SabreCat", StringComparison.OrdinalIgnoreCase))
                .Distinct().ToList();
            if (left.Count > 0) sb.AppendLine($"still the sabre cat's, in {record.GetType().Name} {record.EditorID}: {string.Join(", ", left)}");
        }

        foreach (var d in plugin.SoundDescriptors)
            sb.AppendLine($"SNDR {d.EditorID}: files={d.SoundFiles?.Count} first={d.SoundFiles?.FirstOrDefault()}");
        foreach (var m in plugin.SoundMarkers)
            sb.AppendLine($"SOUN {m.EditorID}: descriptor={Name(m.SoundDescriptor)}");

        foreach (var movt in plugin.MovementTypes)
            sb.AppendLine($"MOVT {movt.EditorID}: name '{movt.Name}' walk {movt.ForwardWalk} run {movt.ForwardRun}");

        foreach (var (label, which) in new[] { ("ours", (IBodyPartDataGetter?)plugin.BodyParts.FirstOrDefault()),
                                              ("vanilla", cache.TryResolve<IBodyPartDataGetter>("SabreCatBodyPartData", out var v) ? v : null) })
        {
            if (which is null) continue;
            sb.AppendLine($"BPTD {label} {which.EditorID}: model={which.Model?.File} parts={which.Parts.Count}");
            foreach (var part in which.Parts)
                sb.AppendLine($"   part {part.Flags} node '{part.PartNode}' vats '{part.VatsTarget}' limb '{part.LimbReplacementModel}'");
        }

        // What the game's own small predators reach with: a reach shorter than the two
        // capsules between them is a reach the combat AI can never close to.
        foreach (string id in new[] { "SkeeverRace", "MudcrabRace", "WolfRace", "FoxRace", "SabreCatRace", "ChickenRace", "RabbitRace", "SlaughterfishRace", "SprigganRace" })
            if (cache.TryResolve<IRaceGetter>(id, out var r))
                sb.AppendLine($"vanilla reach {r.EditorID}: reach={r.UnarmedReach} damage={r.UnarmedDamage} size={r.Size} mass={r.BaseMass} height={r.Height?.Male} attacks={r.Attacks.Count}");

        if (cache.TryResolve<IRaceGetter>("SabreCatRace", out var from))
        {
            sb.AppendLine($"vanilla RACE {from.EditorID}: attacks={from.Attacks.Count} attackRace={Name(from.AttackRace)} reach={from.UnarmedReach} damage={from.UnarmedDamage} size={from.Size} flags={from.Flags}");
            foreach (var a in from.Attacks)
                sb.AppendLine($"   attack {a.AttackEvent} spell={Name(a.AttackData!.Spell)} flags={a.AttackData.Flags} angle={a.AttackData.AttackAngle} strike={a.AttackData.StrikeAngle} mult={a.AttackData.DamageMult} type={Name(a.AttackData.AttackType)}");
            sb.AppendLine($"   equipment flags={from.EquipmentFlags} slots={from.EquipmentSlots?.Count} ours flags={plugin.Races.Single().EquipmentFlags} slots={plugin.Races.Single().EquipmentSlots?.Count}");
            sb.AppendLine($"   behaviour={from.BehaviorGraph?.Male?.File} movement=[{string.Join(", ", from.MovementTypeNames)}]");
            sb.AppendLine($"   keywords={from.Keywords?.Count} ours={plugin.Races.Single().Keywords?.Count}");
        }

        if (cache.TryResolve<IRaceGetter>("SabreCatRace", out var template) && cache.TryResolve<IArmorGetter>(template.Skin.FormKey, out var skin))
        {
            sb.AppendLine($"vanilla skin {skin.EditorID}: armature={skin.Armature.Count}");
            foreach (var link in skin.Armature)
                if (cache.TryResolve<IArmorAddonGetter>(link.FormKey, out var aa))
                    sb.AppendLine($"  vanilla ARMA {aa.EditorID}: race={Name(aa.Race)} additional={aa.AdditionalRaces.Count} male={aa.WorldModel?.Male?.File.GivenPath} female={aa.WorldModel?.Female?.File.GivenPath} weightSlider={aa.WeightSliderEnabled.Male}/{aa.WeightSliderEnabled.Female} parts={aa.BodyTemplate?.FirstPersonFlags}");
        }
        File.WriteAllText(Path.Combine(mod, "plugin.txt"), sb.ToString());
    }
}
