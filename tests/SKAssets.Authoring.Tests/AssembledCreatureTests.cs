using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using SKAssets.Authoring;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    /// <summary>
    /// The records for a creature whose Havok project was assembled rather than copied.
    /// </summary>
    public class AssembledCreatureTests : IDisposable
    {
        private readonly string _folder =
            Path.Combine(Path.GetTempPath(), $"sk-assembled-{Guid.NewGuid():N}");

        public AssembledCreatureTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
        }

        private PluginAuthoring Authoring(out SkyrimMod master)
        {
            master = new SkyrimMod(ModKey.FromName("Master", ModType.Master), SkyrimRelease.SkyrimSE);
            return new PluginAuthoring([master], "MyMod.esp", _folder);
        }

        /// <summary>A race with what an assembled creature's route reads off one.</summary>
        private static Race Template(SkyrimMod master)
        {
            var movement = master.MovementTypes.AddNew("TemplateMovement");
            movement.Name = "TemplateDefault";

            var race = master.Races.AddNew("TemplateRace");
            race.BehaviorGraph = new GenderedItem<Model?>(
                new Model { File = @"actors\template\TemplateProject.hkx" },
                new Model { File = @"actors\template\TemplateProject.hkx" });
            race.BaseMovementDefaultWalk.SetTo(movement);
            race.BaseMovementDefaultRun.SetTo(movement);

            // Two attacks, so replacing them is visible.
            foreach (string sends in new[] { "attackStart1HM", "bashStart" })
                race.Attacks.Add(new Attack { AttackEvent = sends, AttackData = new AttackData { DamageMult = 1.5f } });

            return race;
        }

        private static AssembledCreature Request(IReadOnlyList<string>? attacks = null) => new(
            Template: "TemplateRace",
            Name: "Bonewalker",
            ProjectFolder: @"actors\Bonewalker",
            ProjectFile: "BonewalkerProject.hkx",
            BehaviourFile: @"behaviors\BonewalkerBehavior.hkx",
            TemplateFolder: @"actors\template",
            SkeletonModel: @"actors\Bonewalker\character assets\skeleton.nif",
            Movement: ("BonewalkerDefault", new MovementSpeeds(70.9f, 160.7f, 33.6f, 33.6f, 12.1f, 12.1f, 14.1f, 14.1f)),
            Attacks: attacks);

        /// <summary>
        /// The race runs the project that was built, not a copy of the template's, and wears
        /// the skeleton that was built beside it.
        /// </summary>
        [Fact]
        public void TheRaceRunsWhatWasAssembled()
        {
            using PluginAuthoring authoring = Authoring(out SkyrimMod master);
            Template(master);

            CreatureResult made = authoring.ImportAssembledCreature(Request());
            Race race = authoring.Plugin.Races.First();

            Assert.Equal(@"actors\Bonewalker\BonewalkerProject.hkx", race.BehaviorGraph!.Male!.File.GivenPath);
            Assert.Equal(@"actors\Bonewalker\character assets\skeleton.nif", race.SkeletalModel!.Male!.File.GivenPath);
            Assert.Contains(made.Notes, n => n.Contains("assembled rather than copied", StringComparison.Ordinal));
        }

        /// <summary>
        /// The creature's movement type carries the speeds its clips delivered, and every
        /// default the race names follows to it -- so it moves at its own speeds whichever of
        /// them the engine asks for.
        /// </summary>
        [Fact]
        public void TheMovementTypeCarriesTheSpeedsTheClipsDelivered()
        {
            using PluginAuthoring authoring = Authoring(out SkyrimMod master);
            Template(master);

            authoring.ImportAssembledCreature(Request());

            MovementType made = Assert.Single(authoring.Plugin.MovementTypes);
            Assert.Equal("BonewalkerDefault", made.Name);
            Assert.Equal(70.9f, made.ForwardWalk);
            Assert.Equal(160.7f, made.ForwardRun);
            Assert.Equal(33.6f, made.BackWalk);

            Race race = authoring.Plugin.Races.First();
            Assert.Equal(made.FormKey, race.BaseMovementDefaultWalk.FormKey);
            Assert.Equal(made.FormKey, race.BaseMovementDefaultRun.FormKey);
        }

        /// <summary>
        /// A race's attack list is what combat chooses from: it picks one and sends its event.
        /// A creature carrying the template's list is ordered to attack with events its own
        /// graph has never heard of, and stands there -- so the list is replaced by the events
        /// the graph answers, keeping the template's own entry as the pattern for everything
        /// an animation does not know.
        /// </summary>
        [Fact]
        public void TheRaceOffersCombatTheAttacksItsGraphAnswers()
        {
            using PluginAuthoring authoring = Authoring(out SkyrimMod master);
            Template(master);

            CreatureResult made = authoring.ImportAssembledCreature(
                Request(["SwingHeavy", "SwingNormal", "SwingQuick"]));

            Race race = authoring.Plugin.Races.First();

            Assert.Equal(["SwingHeavy", "SwingNormal", "SwingQuick"], race.Attacks.Select(a => a.AttackEvent));
            Assert.DoesNotContain(race.Attacks, a => a.AttackEvent == "attackStart1HM");

            // What the animation cannot say is kept from the template's own entry.
            Assert.All(race.Attacks, a => Assert.Equal(1.5f, a.AttackData!.DamageMult));
            Assert.Contains(made.Notes, n => n.Contains("never hears", StringComparison.Ordinal));
        }

        /// <summary>
        /// Leaving the template's attacks is allowed and is said out loud, since it is the
        /// shape of a creature that reaches its enemy and does nothing.
        /// </summary>
        [Fact]
        public void KeepingTheTemplatesAttacksIsReported()
        {
            using PluginAuthoring authoring = Authoring(out SkyrimMod master);
            Template(master);

            CreatureResult made = authoring.ImportAssembledCreature(Request());

            Assert.Equal(2, authoring.Plugin.Races.First().Attacks.Count);
            Assert.Contains(made.Notes, n => n.Contains("stand there", StringComparison.Ordinal));
        }
    }
}
