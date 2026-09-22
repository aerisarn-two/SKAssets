using System.Numerics;
using System.Text;
using HKFBX.Fbx;
using HKSK.Model;
using LeanMeshIO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using HKSK.Cache;
using SKAssets.Export;
using Xunit;

namespace SKAssets.Authoring.Tests;

// The cat, made from the sabre cat: skeleton and ragdoll, body, clips from the .blend.
// Stage one is the import; CatGraph amends the behaviour afterwards.
public sealed class ZzCatCreature
{
    public static string Mod => Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
    public static string Out => Path.Combine(Mod, "Data");
    public const string Plugin = "CatSimple.esp";
    public const string Name = "HouseCat";

    /// <summary>Sabre cat bone -> cat bone, where they are called differently.</summary>
    public static readonly Dictionary<string, string> BoneMap = new()
    {
        ["Sabrecat_[pelv]"] = "Spine_base",
        ["Sabrecat_Spine[Spn0]"] = "spine_02",
        ["Sabrecat_Spine[Spn1]"] = "spine_03",
        ["Sabrecat_Spine[Spn2]"] = "spine_04",
        ["Sabrecat_Spine[Spn3]"] = "spine_05",
        ["Sabrecat_Neck[Nek2]"] = "neck",
        ["Sabrecat_Head [Head]"] = "head",
        ["Sabrecat_Head[jaw]"] = "mouth",
        ["Sabrecat_Tail0[Tal0]"] = "tail_01",
        ["Sabrecat_Tail1[Tal1]"] = "tail_02",
        ["Sabrecat_LeftThigh[LThi]"] = "hip_b.L", ["Sabrecat_RightThigh[RThi]"] = "hip_b.R",
        ["Sabrecat_LeftCalf[LClf]"] = "leg_b.L", ["Sabrecat_RightCalf[RClf]"] = "leg_b.R",
        ["Sabrecat_LeftFoot[LFot]"] = "foot_b.L", ["Sabrecat_RightFoot[RFot]"] = "foot_b.R",
        ["Sabrecat_LeftToe0[LT00]"] = "claw_b.L", ["Sabrecat_RightToe0[RT00]"] = "claw_b.R",
        ["Sabrecat_LeftClavicle[LClv]"] = "shoulder_blade.L", ["Sabrecat_RightClavicle[RClv]"] = "shoulder_blade.R",
        ["Sabrecat_LeftUpperArm[LUar]"] = "hip_f.L", ["Sabrecat_RightUpperArm[RUar]"] = "hip_f.R",
        ["Sabrecat_LeftForearm[LFar]"] = "leg_f.L", ["Sabrecat_RightForearm[RFar]"] = "leg_f.R",
        ["Sabrecat_LeftHand[LHnd]"] = "foot_f.L", ["Sabrecat_RightHand[RHnd]"] = "foot_f.R",
        ["Sabrecat_LeftFinger0[LF00]"] = "claw_f.L", ["Sabrecat_RightFinger0[RF00]"] = "claw_f.R",
    };

    /// <summary>A clip's delivered speed, in units a second, from its root motion in the clip FBX.</summary>
    public static float SpeedOf(FbxDocument clips, HKFBX.Model.Skeleton rig, string stack)
    {
        string name = new FbxScene(clips).OfClass("AnimationStack").Select(s => s.Name).First(s => ClipExchange.Names(s).Contains(stack));
        var motion = FbxAnimationReader.ReadRootMotion(clips, rig, name);
        Vector3 end = motion.TranslationAt(motion.Duration);
        return end.Length() / motion.Duration;
    }

    [MastersFact]
    public void Import()
    {
        if (Mod.Length == 0) return;
        string meshes = Game.Meshes ?? throw new InvalidOperationException("set SKASSETS_HAVOK_MESHES");
        string implementation = Path.GetDirectoryName(Mod)!;
        string skeleton = Path.Combine(implementation, "catsimple_fbx", "Cat_Simple_Ragdoll.fbx");
        string body = Path.Combine(Mod, "Cat_Simple_Body.fbx");
        string clipsPath = Path.Combine(Mod, "Cat_Simple_Clips.fbx");
        if (Directory.Exists(Out)) Directory.Delete(Out, recursive: true);

        // The movement types' speeds are the clips': walk the walk, run the fastest run,
        // back the walk backward -- the rule the shipped records were authored by.
        var clips = FbxDocument.Load(clipsPath);
        var rig = FbxAnimationReader.ReadSkeleton(clips);
        float walk = SpeedOf(clips, rig, "WalkForward"), run = SpeedOf(clips, rig, "RunFast_F_RM"), back = SpeedOf(clips, rig, "WalkBackward");
        var speeds = new MovementSpeeds(walk, run, back, back, 0, 0, 0, 0);

        var report = new StringBuilder();
        report.AppendLine($"speeds from the clips: walk {walk:F1}, run {run:F1}, back {back:F1}");

        CreatureResult made;
        using (var authoring = PluginAuthoring.Open(Game.Data!, Plugin, Out))
        {
            made = authoring.ImportCreature(new NewCreature
            {
                Template = "SabreCatRace",
                Name = Name,
                DisplayName = "Cat",
                SourceMeshes = meshes,
                Skeleton = skeleton,
                BoneMap = BoneMap,
                Body = new Dictionary<ModelSlot, string> { [ModelSlot.Main] = body },
                Animations = [clipsPath],
                OwnMovementTypes = true,
                Speeds = new Dictionary<string, MovementSpeeds> { ["SabreCatDefault"] = speeds, ["SabreCatRun"] = speeds },
                Npc = "EncSabreCat",
            });

            string folder = Path.Combine(Out, "Meshes", "actors", Name);
            var caches = SkyrimCache.Load(Path.Combine(Out, "Meshes"));
            ActorProject actor = caches.OpenActor(made.Project)!;

            // The kill moves are a human's and a sabre cat's together, on rigs the cat has not got.
            var orphaned = new List<string>();
            while (actor.Animations.FirstOrDefault(a => a.StoredName.StartsWith(@"..\", StringComparison.Ordinal)) is { } paired)
                orphaned.AddRange(actor.RemoveAnimation(paired));
            actor.SaveCharacter();
            caches.Save();
            report.AppendLine($"paired kill moves removed; {orphaned.Count} clips orphaned: {string.Join(", ", orphaned)}");

            // Every cat clip gets a state.
            new CatGraph(actor, report).Amend(folder);
            // A clip the graph no longer has -- the kill moves, the fast run's old names -- goes
            // from the cache too.
            var defined = CatGraph.ClipNames(folder);
            var stale = actor.Clips.Select(c => c.Entry.Name).Where(n => !defined.Contains(n)).ToList();
            foreach (string name in stale) actor.RemoveClip(name);
            caches.Save();
            report.AppendLine($"cache clips no behaviour defines, removed: {string.Join(", ", stale)}");
            var played = CatGraph.Played(folder);
            var manifest = File.ReadAllLines(Path.Combine(Mod, "manifest.tsv")).Select(l => l.Split('\t')[1]).ToList();
            var unplayed = manifest.Where(m => !played.Contains(m)).ToList();
            var unstored = played.Where(p => !manifest.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
            report.AppendLine($"clips played by the graph: {manifest.Count - unplayed.Count} of {manifest.Count}; unplayed: {string.Join(", ", unplayed)}; played but not the cat's: {string.Join(", ", unstored)}");

            // ---- records the graph's new events need, and a cat's measure
            var masters = SKAssets.Content.Havok.GameRecordReader.Masters.Select(f => Path.Combine(Game.Data!, f)).Where(File.Exists)
                .Select(f => (ISkyrimModGetter)SkyrimMod.CreateFromBinaryOverlay(f, SkyrimRelease.SkyrimSE)).ToList();
            var cache = masters.ToImmutableLinkCache();
            var plugin = authoring.Plugin;
            // A copy is renamed after the cat, and how depends on how the sabre cat spelled
            // itself in the name it had, so the copies are looked up by what they were copied
            // from rather than by guessing what they ended up called.
            IdleAnimation Copied(string editorId)
            {
                FormKey was = cache.Resolve<IIdleAnimationGetter>(editorId).FormKey;
                return plugin.IdleAnimations[made.Records.First(r => r.CopiedFrom == was).FormKey];
            }

            Copied("SabreCatDeath").AnimationEvent = "DeathAnimation";
            var behavior = Copied("SabreCatDeath").Filename!.GivenPath;

            IdleAnimation NewIdle(string editorId, string animationEvent, IFormLinkGetter<IIdleRelationGetter> parent, IFormLinkGetter<IIdleRelationGetter>? previous, params Condition[] conditions)
            {
                var idle = plugin.IdleAnimations.AddNew(Name + editorId);
                idle.AnimationEvent = animationEvent;
                idle.Filename = new Mutagen.Bethesda.Plugins.Assets.AssetLink<Mutagen.Bethesda.Skyrim.Assets.SkyrimBehaviorAssetType>(behavior);
                idle.RelatedIdles.Add(parent);
                idle.RelatedIdles.Add(previous ?? FormLinkGetter<IIdleRelationGetter>.Null);
                idle.Conditions.AddRange(conditions);
                return idle;
            }
            static ConditionFloat When(ConditionData data, CompareOperator op, float value) => new() { Data = data, CompareOperator = op, ComparisonValue = value };

            NewIdle("LieSide", "idleCatLieSideStart", Copied("ScatIdleRoot").ToLink<IIdleRelationGetter>(), Copied("LayDown").ToLink<IIdleRelationGetter>(),
                When(new IsInCombatConditionData(), CompareOperator.EqualTo, 0), When(new GetRandomPercentConditionData(), CompareOperator.LessThan, 30));
            foreach (var (action, ev) in new[] { ("ActionFall", "catFall"), ("ActionLand", "catLand"), ("ActionJump", "catJump") })
            {
                if (!cache.TryResolve<IActionRecordGetter>(action, out var found)) { report.AppendLine($"no action {action}"); continue; }
                NewIdle(action.Replace("Action", ""), ev, found.ToLink<IIdleRelationGetter>(), null);
            }

            var race = plugin.Races.Single();
            race.Size = Size.Small;
            race.BaseMass = 0.5f;
            // Reach is how close the combat AI has to get before it will swing, measured between
            // centres, and the two collision capsules keep it from closing much under 60 units.
            // 64 is what every small thing in the game that attacks uses -- skeever, wolf, fox --
            // and the sabre cat's 85 is a reach its size earns; below 64 the cat circles forever.
            race.UnarmedReach = 64f;
            race.UnarmedDamage = 4f;
            report.AppendLine($"race {race.EditorID}: small, mass 0.5, reach 40, unarmed damage 4; idles {plugin.IdleAnimations.Count}");

            authoring.Save();

            // ---- the caches, amended after the graph and against the plugin as it is now
            var records = SKAssets.Content.Havok.GameRecordReader.Read(
                [.. masters, SkyrimMod.CreateFromBinaryOverlay(Path.Combine(Out, Plugin), SkyrimRelease.SkyrimSE)]);
            var final = SkyrimCache.Load(Path.Combine(Out, "Meshes"));
            report.AppendLine($"caches amended: {CacheGeneration.Amend(final, made.Project, records)}");
            final.Save();
            foreach (var finding in HKSK.Validation.ConsistencyReport.Check(SkyrimCache.Load(Path.Combine(Out, "Meshes")).OpenActor(made.Project)!))
                report.AppendLine($"consistency: {finding}");
        }

        report.AppendLine($"project {made.Project}; caches {made.Caches}");
        report.AppendLine($"records {made.Records.Count}: {string.Join(", ", made.Records.GroupBy(r => r.Type).Select(g => $"{g.Key} {g.Count()}"))}");
        report.AppendLine($"clips imported {made.Clips?.Clips.Count}, ignored {made.Clips?.Ignored.Count}, failed {made.Clips?.Failed.Count}");
        foreach (var (clip, why) in made.Clips?.Failed ?? new Dictionary<string, string>()) report.AppendLine($"  FAILED {clip}: {why}");
        foreach (string note in made.Notes) report.AppendLine($"note: {note}");
        foreach (var (mesh, finding) in made.Findings) report.AppendLine($"finding {mesh}: {finding}");
        report.AppendLine($"files {made.Files.Count}");
        File.WriteAllText(Path.Combine(Mod, "import_report.txt"), report.ToString());
    }
}
