using HKSK.Cache;
using HKSK.Model;
using HKSK.Records;
using HKSK.SetData;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Records;
using SKAssets.Content.Havok;
using Xunit;

namespace SKAssets.Content.Tests
{
    /// <summary>
    /// The records HKSK needs, read from plugins and handed over in memory.
    /// </summary>
    public class GameRecordReaderTests
    {
        private const string WerewolfGraph = @"Actors\WerewolfBeast\WerewolfBeastProject.hkx";

        /// <summary>
        /// A master with a movement type, a race wearing a graph and an idle tree, and a plugin
        /// overriding the movement type -- the smallest load order that has a winner to pick.
        /// </summary>
        private static (ISkyrimModGetter Master, ISkyrimModGetter Plugin) LoadOrder()
        {
            var master = new SkyrimMod(ModKey.FromName("Beasts", ModType.Master), SkyrimRelease.SkyrimSE);

            var walk = master.MovementTypes.AddNew("Werewolf_Default_MT");
            walk.Name = "WerewolfDefault";
            walk.ForwardWalk = 100f;
            walk.ForwardRun = 300f;

            var race = master.Races.AddNew("WerewolfBeastRace");
            race.BehaviorGraph = new GenderedItem<Model?>(new Model { File = WerewolfGraph }, new Model { File = WerewolfGraph });
            race.BaseMovementDefaultWalk.SetTo(walk);
            race.BaseMovementDefaultRun.SetTo(walk);
            race.Attacks.Add(new Attack { AttackEvent = "attackPowerStartLeft" });
            race.Attacks.Add(new Attack { AttackEvent = "attackPowerStartLeftRunning" });

            // The werewolf's power attacks: the standing one asks for a speed of at most one,
            // and the running one, tried after it, has no condition of its own.
            var root = master.IdleAnimations.AddNew("WerewolfAttackRoot");
            root.Filename = @"Actors\WerewolfBeast\Behaviors\WerewolfBehavior.hkx";

            var stand = master.IdleAnimations.AddNew("WerewolfLeftPowerAttack");
            stand.AnimationEvent = "attackPowerStartLeft";
            stand.RelatedIdles.Add(root.ToLink<IIdleRelationGetter>());
            stand.Conditions.Add(new ConditionFloat
            {
                CompareOperator = CompareOperator.LessThanOrEqualTo,
                ComparisonValue = 1f,
                Data = new GetMovementSpeedConditionData(),
            });

            var run = master.IdleAnimations.AddNew("WerewolfLeftRunningPowerAttack");
            run.AnimationEvent = "attackPowerStartLeftRunning";
            run.RelatedIdles.Add(root.ToLink<IIdleRelationGetter>());
            run.RelatedIdles.Add(stand.ToLink<IIdleRelationGetter>());

            master.Actions.AddNew("ActionRightPowerAttack");

            var plugin = new SkyrimMod(ModKey.FromName("FasterWolves", ModType.Plugin), SkyrimRelease.SkyrimSE);
            var faster = plugin.MovementTypes.GetOrAddAsOverride(walk);
            faster.ForwardRun = 450f;

            return (master, plugin);
        }

        [Fact]
        public void TheRecordsGoAcrossAsThePluginStatesThem()
        {
            var (master, plugin) = LoadOrder();
            GameRecords records = GameRecordReader.Read([master, plugin]);

            MovementTypeRecord movement = Assert.Single(records.MovementTypes);
            Assert.Equal(("Werewolf_Default_MT", "WerewolfDefault"), (movement.EditorID, movement.Name));

            RaceRecord race = Assert.Single(records.Races);
            Assert.Equal(WerewolfGraph, race.MaleBehavior, ignoreCase: true);
            Assert.Equal(["attackPowerStartLeft", "attackPowerStartLeftRunning"], race.AttackEvents);
            Assert.Equal(movement.Id, race.DefaultMovements[MovementRole.Walk]);
            Assert.False(race.DefaultMovements.ContainsKey(MovementRole.Swim));

            IdleRecord stand = records.Idles.Single(i => i.EditorID == "WerewolfLeftPowerAttack");
            IdleRecord run = records.Idles.Single(i => i.EditorID == "WerewolfLeftRunningPowerAttack");
            Assert.Equal(stand.Parent, run.Parent);
            Assert.Equal(stand.Id, run.PreviousSibling);
            Assert.Equal(new IdleCondition("GetMovementSpeed", ConditionOperator.LessThanOrEqualTo, 1f), Assert.Single(stand.Conditions));

            Assert.Equal("ActionRightPowerAttack", Assert.Single(records.Actions).EditorID);
        }

        /// <summary>The later plugin's record is the one handed over, whole.</summary>
        [Fact]
        public void TheLoadOrdersWinnerIsTheRecord()
        {
            var (master, plugin) = LoadOrder();

            Assert.Equal(300f, Assert.Single(GameRecordReader.Read([master]).MovementTypes).ForwardRun);
            Assert.Equal(450f, Assert.Single(GameRecordReader.Read([master, plugin]).MovementTypes).ForwardRun);
            Assert.Equal(300f, Assert.Single(GameRecordReader.Read([plugin, master]).MovementTypes).ForwardRun);
        }

        /// <summary>
        /// What the records mean is HKSK's to say: the running power attack is chosen on the
        /// move by elimination, the movement type goes by the name the graph uses, and a race
        /// wearing the project makes it an actor.
        /// </summary>
        [Fact]
        public void HkskReadsTheRulesOffTheRecords()
        {
            var (master, plugin) = LoadOrder();
            GameRecords records = GameRecordReader.Read([master, plugin]);

            GameEvents events = GameRecordRules.Events(records);
            Assert.Equal(["attackPowerStartLeftRunning"], events.MovingAttacks!["WerewolfBeastProject"]);
            Assert.Equal(2, events.Attacks["WerewolfBeastProject"].Count);

            Assert.Equal(450f, GameRecordRules.MovementTypes(records)["WerewolfDefault"].ForwardRun);
            Assert.Equal(["WerewolfBeastProject"], GameRecordRules.ActorProjects(records));
        }

        /// <summary>
        /// The shipped masters read here give HKSK what its own reader of them gives it: the same
        /// counts HKSK's tests measured, record for record and event for event.
        /// </summary>
        [MastersFact]
        public void TheMastersGiveHkskWhatItMeasured()
        {
            GameRecords records = GameRecordReader.Read(Masters.Paths);

            Assert.Equal((107, 161, 4154, 71),
                (records.MovementTypes.Count, records.Races.Count, records.Idles.Count, records.Actions.Count));

            GameEvents events = GameRecordRules.Events(records);
            Assert.Equal((1246, 36), (events.Idle.Count, events.Equip.Count));
            Assert.Equal((48, 437), (events.Attacks.Count, events.Attacks.Values.Sum(a => a.Count)));
            Assert.Equal((7, 51), (events.MovingAttacks!.Count, events.MovingAttacks.Values.Sum(a => a.Count)));

            Assert.Equal(107, GameRecordRules.MovementTypes(records).Count);
            Assert.Equal(48, GameRecordRules.ActorProjects(records).Count);
        }

        /// <summary>
        /// The whole way: the masters read here, and HKSK putting a creature the caches have lost
        /// back into all three of them, as the actor its race wears.
        /// </summary>
        [HavokMastersFact]
        public void HkskPutsACreatureBackIntoTheCachesFromTheseRecords()
        {
            GameRecords records = GameRecordReader.Read(Masters.Paths);
            SkyrimCache cache = SkyrimCache.Load(Masters.Meshes!);

            cache.AnimationData.Projects.Remove(cache.AnimationData.Project("WolfProject")!);
            cache.SetData.Projects.Remove(cache.SetData.Project("WolfProject")!);
            int speed = cache.SpeedData!.Projects.FindIndex(p => SpeedDataFile.StemOf(p) == "WolfProject");
            cache.SpeedData.Projects.RemoveAt(speed);
            cache.SpeedData.Blocks.RemoveAt(speed);

            CacheAmendment done = CacheGeneration.Amend(cache, "WolfProject", records);

            Assert.Equal(new CacheAmendment("WolfProject", Amendment.Added, Amendment.Added, Amendment.Added), done);
            Assert.True(cache.AnimationData.Projects[^1].Block.HasAnimationCache);
        }
    }

    /// <summary>The five masters in the game's Data folder, when one is to hand.</summary>
    internal static class Masters
    {
        public const string DataVar = "SKASSETS_SKYRIM_DATA";

        public static string? Data
        {
            get
            {
                string? configured = Environment.GetEnvironmentVariable(DataVar);
                if (string.IsNullOrWhiteSpace(configured)) return null;

                Assert.True(Directory.Exists(configured), $"{DataVar} is not a folder: {configured}");
                return configured;
            }
        }

        public const string MeshesVar = "SKASSETS_HAVOK_MESHES";

        /// <summary>An extracted meshes folder holding the animation cache, or null.</summary>
        public static string? Meshes
        {
            get
            {
                string? configured = Environment.GetEnvironmentVariable(MeshesVar);
                if (string.IsNullOrWhiteSpace(configured)) return null;

                Assert.True(Directory.Exists(configured), $"{MeshesVar} is not a folder: {configured}");
                return configured;
            }
        }

        public static IEnumerable<string> Paths =>
            new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" }
                .Select(master => Path.Combine(Data!, master));
    }

    /// <summary>A fact about the masters and the extracted animation cache together.</summary>
    public sealed class HavokMastersFactAttribute : FactAttribute
    {
        public HavokMastersFactAttribute()
        {
            if (Masters.Data is null) Skip = $"set {Masters.DataVar} to the game's Data folder to run this";
            else if (Masters.Meshes is null) Skip = $"set {Masters.MeshesVar} to an extracted meshes folder to run this";
        }
    }

    /// <summary>A fact about the shipped masters, which skips rather than passes without them.</summary>
    public sealed class MastersFactAttribute : FactAttribute
    {
        public MastersFactAttribute()
        {
            if (Masters.Data is null) Skip = $"set {Masters.DataVar} to the game's Data folder to run this";
        }
    }
}
