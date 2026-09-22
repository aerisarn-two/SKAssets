using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace SKAssets.Authoring.Tests;

// What the masters say about the sabre cat, for the cat made from it.
public sealed class ZzCatRecords
{
    [MastersFact]
    public void Dump()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? Path.GetTempPath();
        var sb = new StringBuilder();
        SKAssets.Plugins.MutagenRuntime.Prepare();
        var mods = SKAssets.Content.Havok.GameRecordReader.Masters.Select(f => Path.Combine(Game.Data!, f)).Where(File.Exists)
            .Select(f => (ISkyrimModGetter)SkyrimMod.CreateFromBinaryOverlay(f, SkyrimRelease.SkyrimSE)).ToList();
        var cache = mods.ToImmutableLinkCache();
        var race = cache.PriorityOrder.WinningOverrides<IRaceGetter>().First(r => r.EditorID == "SabreCatRace");
        sb.AppendLine($"race {race.EditorID} {race.FormKey} height={race.Height} weight={race.Weight} baseMass={race.BaseMass} reach={race.UnarmedReach} unarmedDmg={race.UnarmedDamage} accel={race.AccelerationRate} decel={race.DecelerationRate} angAccel={race.AngularAccelerationRate} angTol={race.AngularTolerance} size={race.Size} flags={race.Flags} injured={race.InjuredHealthPercent} aim={race.AimAngleTolerance} flightRadius={race.FlightRadius} shieldBone={race.ShieldBipedObject} bodyBipedObject={race.BodyBipedObject}");
        foreach (var t in new[] { ("walk", race.BaseMovementDefaultWalk), ("run", race.BaseMovementDefaultRun), ("swim", race.BaseMovementDefaultSwim), ("fly", race.BaseMovementDefaultFly), ("sneak", race.BaseMovementDefaultSneak), ("sprint", race.BaseMovementDefaultSprint) })
            sb.AppendLine($"  default {t.Item1} -> {(t.Item2.IsNull ? "-" : cache.TryResolve<IMovementTypeGetter>(t.Item2.FormKey, out var m) ? m.EditorID + " " + m.Name : "?")}");
        sb.AppendLine($"  movementTypeNames=[{string.Join(",", race.MovementTypeNames)}] behavior={race.BehaviorGraph.Male?.File} skeleton={race.SkeletalModel?.Male?.File} bodyPartData={race.BodyPartData.FormKey} attackRace={race.AttackRace.FormKey} impactData={race.ImpactDataSet.FormKey} decapitate? bipedObjects={race.BipedObjectNames.Count}");
        foreach (var a in race.Attacks) sb.AppendLine($"  attack event={a.AttackEvent} dmgMult={a.AttackData?.DamageMult} spell={a.AttackData?.Spell.FormKey} flags={a.AttackData?.Flags} angle={a.AttackData?.AttackAngle} strikeAngle={a.AttackData?.StrikeAngle} stagger={a.AttackData?.Stagger} type={a.AttackData?.AttackType.FormKey} knockdown={a.AttackData?.Knockdown} recovery={a.AttackData?.RecoveryTime} stamina={a.AttackData?.StaminaMult}");
        foreach (var movt in cache.PriorityOrder.WinningOverrides<IMovementTypeGetter>().Where(m => (m.EditorID ?? "").Contains("Sabre", StringComparison.OrdinalIgnoreCase) || (m.Name ?? "").Contains("Sabre", StringComparison.OrdinalIgnoreCase)))
            sb.AppendLine($"movt {movt.EditorID} name={movt.Name} fw={movt.ForwardWalk}/{movt.ForwardRun} back={movt.BackWalk}/{movt.BackRun} left={movt.LeftWalk}/{movt.LeftRun} right={movt.RightWalk}/{movt.RightRun} rotWalk={movt.RotateInPlaceWalk} rotRun={movt.RotateInPlaceRun} rotWhileMoving={movt.RotateWhileMovingRun}");
        if (cache.TryResolve<IBodyPartDataGetter>(race.BodyPartData.FormKey, out var bptd))
            foreach (var p in bptd.Parts) sb.AppendLine($"  part {p.Name} node={p.PartNode} vats={p.VatsTarget} ik={p.IkStartNode} flags={p.Flags} limbReplace={p.LimbReplacementModel} gore={p.GoreTargetBone} targetBone? ");
        // idles: every idle whose event the sabre cat's graph knows, or that sends a death event
        string[] events = ["idleFeedingStart", "idleSabreCatLieDownStart", "idleSabreCatSitStart", "idleSabreCatLieDownStop", "idleSabreCatSitStop", "DeathAnimation", "aggroWarningStart", "combatIdle1Start", "combatIdle2Start", "idleTrapEnterInstantStart", "idleStop", "IdleStop", "idleExit"];
        foreach (var idle in cache.PriorityOrder.WinningOverrides<IIdleAnimationGetter>())
        {
            if (idle.AnimationEvent is not { } ev) continue;
            if (!events.Contains(ev, StringComparer.OrdinalIgnoreCase) && !ev.Contains("Death", StringComparison.OrdinalIgnoreCase) && !(idle.EditorID ?? "").Contains("Sabre", StringComparison.OrdinalIgnoreCase)) continue;
            string conds = string.Join(" & ", idle.Conditions.Select(c => c is IConditionFloatGetter f ? $"{f.Data.GetType().Name.Replace("ConditionData", "")}{Arg(f.Data)} {f.CompareOperator} {f.ComparisonValue}" : c.GetType().Name));
            string parent = idle.RelatedIdles.Count > 0 && cache.TryResolve<IIdleRelationGetter>(idle.RelatedIdles[0].FormKey, out var rel) ? rel.EditorID ?? "" : "";
            sb.AppendLine($"idle {idle.EditorID} {idle.FormKey} event={ev} file={idle.Filename} parent={parent} flags={idle.Flags} conds=[{conds}]");
        }
        sb.AppendLine("== every idle naming a sabre cat behaviour");
        var all = cache.PriorityOrder.WinningOverrides<IIdleAnimationGetter>().ToList();
        string Name(Mutagen.Bethesda.Plugins.IFormLinkGetter l) => l.IsNull ? "-" : cache.TryResolveIdentifier(l.FormKey, out string? id) ? id ?? l.FormKey.ToString() : l.FormKey.ToString();
        foreach (var idle in all.Where(i => (i.Filename?.GivenPath ?? "").Contains("SabreCat", StringComparison.OrdinalIgnoreCase)))
        {
            string conds = string.Join(" & ", idle.Conditions.Select(c => c is IConditionFloatGetter f ? $"{f.Data.GetType().Name.Replace("ConditionData", "")}{Arg(f.Data)} {f.CompareOperator} {f.ComparisonValue}" : c.GetType().Name));
            sb.AppendLine($"idle {idle.EditorID} {idle.FormKey} event={idle.AnimationEvent} file={idle.Filename} related=[{string.Join(",", idle.RelatedIdles.Select(Name))}] flags={idle.Flags} loop=({idle.LoopingSecondsMin},{idle.LoopingSecondsMax}) replay={idle.ReplayDelay} conds=[{conds}]");
        }
        sb.AppendLine("== idle behaviour files, and whether the file exists in the extracted meshes");
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES") ?? "";
        foreach (var g in all.Where(i => i.Filename?.GivenPath is { Length: > 0 }).GroupBy(i => i.Filename!.GivenPath.Replace('/', '\\').ToLowerInvariant()).OrderBy(g => g.Key))
        {
            string rel = g.Key.StartsWith(@"meshes\") ? g.Key[7..] : g.Key;
            bool exists = File.Exists(Path.Combine(meshes, rel.Replace('\\', '/'))) || Directory.EnumerateFiles(meshes, Path.GetFileName(rel), SearchOption.AllDirectories).Any(f => f.Replace('/', '\\').EndsWith(rel, StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"  {(exists ? "ok     " : "MISSING")} {g.Count(),4} {g.Key}  e.g. {string.Join(", ", g.Take(3).Select(i => i.EditorID))}");
        }
        File.WriteAllText(Path.Combine(outDir, "sabrecat_records.txt"), sb.ToString());

        string Arg(object data)
        {
            var props = data.GetType().GetProperties().Where(p => p.Name is "Race" or "Keyword" or "FirstParameter" or "Actor" or "Faction" or "RunOnType").Select(p => $"{p.Name}={Describe(p.GetValue(data))}");
            return "(" + string.Join(",", props) + ")";
        }
        string Describe(object? v) => v switch
        {
            IFormLinkGetter l when !l.IsNull && cache.TryResolve(l.FormKey, l.Type, out var rec) => rec.EditorID ?? l.FormKey.ToString(),
            IFormLinkOrIndexGetter<IRaceGetter> r when !r.Link.IsNull && cache.TryResolve<IRaceGetter>(r.Link.FormKey, out var race) => race.EditorID ?? "",
            _ => v?.ToString() ?? "",
        };
    }
}
