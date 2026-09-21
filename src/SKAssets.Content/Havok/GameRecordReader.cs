using HKSK.Records;
using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using SKAssets.Plugins;
using ActionRecord = HKSK.Records.ActionRecord;

namespace SKAssets.Content.Havok
{
    /// <summary>
    /// What HKSK needs from the plugins to write the animation caches, read from a load order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HKSK writes <c>animationdatasinglefile.txt</c>, <c>animationsetdatasinglefile.txt</c> and
    /// <c>speeddatasinglefile.txt</c>, and three of their inputs are plugin records: the movement
    /// types the speed table is swept for, the races that wear each project and send its
    /// attacks, and the idle tree the set data is keyed on. HKSK does not open plugins. It
    /// states what it needs as <see cref="IGameRecords"/>, and this fills it, in memory:
    /// </para>
    /// <code>
    /// GameRecords records = GameRecordReader.Read(loadOrder);
    /// SkyrimCache cache = SkyrimCache.Load(meshesFolder);
    ///
    /// CacheGeneration.Amend(cache, "MyCreatureProject", records);
    /// cache.Save();
    /// </code>
    /// <para>
    /// <strong>This reads and does not decide.</strong> Which idles equip, which attacks are
    /// chosen on the move, which projects are actors -- those are the engine's rules, and
    /// HKSK's <see cref="GameRecordRules"/> applies them. The records go across as the plugin
    /// states them: both of a movement type's names, an idle's conditions by function, the
    /// behaviour paths as stored.
    /// </para>
    /// <para>
    /// A later plugin's record replaces an earlier one's, which is what a load order does for
    /// the three record types read here: nothing merges a <c>MOVT</c>, a <c>RACE</c> or an
    /// <c>IDLE</c> field by field. Ids are form keys, which HKSK only compares.
    /// </para>
    /// </remarks>
    public static class GameRecordReader
    {
        /// <summary>The game's masters, in load order.</summary>
        public static IReadOnlyList<string> Masters { get; } =
            ["Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm"];

        /// <summary>The records of plugins already open, in load order.</summary>
        public static GameRecords Read(IEnumerable<ISkyrimModGetter> loadOrder)
        {
            ArgumentNullException.ThrowIfNull(loadOrder);

            var movements = new Dictionary<FormKey, MovementTypeRecord>();
            var races = new Dictionary<FormKey, RaceRecord>();
            var idles = new Dictionary<FormKey, IdleRecord>();
            var actions = new Dictionary<FormKey, ActionRecord>();

            foreach (ISkyrimModGetter mod in loadOrder)
            {
                foreach (IMovementTypeGetter movement in mod.MovementTypes)
                    movements[movement.FormKey] = Movement(movement);

                foreach (IRaceGetter race in mod.Races)
                    races[race.FormKey] = Race(race);

                foreach (IIdleAnimationGetter idle in mod.IdleAnimations)
                    idles[idle.FormKey] = Idle(idle);

                foreach (IActionRecordGetter action in mod.Actions)
                    actions[action.FormKey] = new ActionRecord { Id = Id(action.FormKey), EditorID = action.EditorID };
            }

            return new GameRecords
            {
                MovementTypes = [.. movements.Values],
                Races = [.. races.Values],
                Idles = [.. idles.Values],
                Actions = [.. actions.Values],
            };
        }

        /// <summary>The records of plugin files, opened in the order given and closed again.</summary>
        /// <param name="pluginPaths">The plugins, masters first: <c>Skyrim.esm</c>, <c>Update.esm</c>, ...</param>
        /// <param name="release">The game they are for.</param>
        public static GameRecords Read(IEnumerable<string> pluginPaths, SkyrimRelease release = SkyrimRelease.SkyrimSE)
        {
            ArgumentNullException.ThrowIfNull(pluginPaths);
            MutagenRuntime.Prepare();

            var mods = new List<ISkyrimModDisposableGetter>();
            try
            {
                foreach (string path in pluginPaths)
                    mods.Add(SkyrimMod.CreateFromBinaryOverlay(path, release));

                return Read(mods);
            }
            finally
            {
                foreach (var mod in mods) mod.Dispose();
            }
        }

        private static string Id(FormKey key) => key.ToString();

        private static MovementTypeRecord Movement(IMovementTypeGetter m) => new()
        {
            Id = Id(m.FormKey),
            EditorID = m.EditorID,
            Name = m.Name,
            ForwardWalk = m.ForwardWalk,
            ForwardRun = m.ForwardRun,
            BackWalk = m.BackWalk,
            BackRun = m.BackRun,
            LeftWalk = m.LeftWalk,
            LeftRun = m.LeftRun,
            RightWalk = m.RightWalk,
            RightRun = m.RightRun,
        };

        private static RaceRecord Race(IRaceGetter race)
        {
            var defaults = new Dictionary<MovementRole, string>();
            foreach ((MovementRole role, IFormLinkNullableGetter<IMovementTypeGetter> link) in new[]
                     {
                         (MovementRole.Walk, race.BaseMovementDefaultWalk),
                         (MovementRole.Run, race.BaseMovementDefaultRun),
                         (MovementRole.Swim, race.BaseMovementDefaultSwim),
                         (MovementRole.Fly, race.BaseMovementDefaultFly),
                         (MovementRole.Sneak, race.BaseMovementDefaultSneak),
                         (MovementRole.Sprint, race.BaseMovementDefaultSprint),
                     })
                if (!link.IsNull) defaults[role] = Id(link.FormKey);

            return new RaceRecord
            {
                Id = Id(race.FormKey),
                EditorID = race.EditorID,
                MaleBehavior = race.BehaviorGraph.Male?.File.GivenPath,
                FemaleBehavior = race.BehaviorGraph.Female?.File.GivenPath,
                AttackEvents = [.. race.Attacks.Select(a => a.AttackEvent).OfType<string>()],
                DefaultMovements = defaults,
            };
        }

        /// <summary>An idle and its two links: the first related idle is its parent, the second the sibling before it.</summary>
        private static IdleRecord Idle(IIdleAnimationGetter idle) => new()
        {
            Id = Id(idle.FormKey),
            EditorID = idle.EditorID,
            AnimationEvent = string.IsNullOrEmpty(idle.AnimationEvent) ? null : idle.AnimationEvent,
            BehaviorFile = idle.Filename?.GivenPath,
            Parent = Link(idle, 0),
            PreviousSibling = Link(idle, 1),
            Conditions = [.. idle.Conditions.Select(Condition)],
        };

        private static string? Link(IIdleAnimationGetter idle, int slot) =>
            slot < idle.RelatedIdles.Count && !idle.RelatedIdles[slot].IsNull ? Id(idle.RelatedIdles[slot].FormKey) : null;

        /// <summary>
        /// A condition by its function's editor name. Mutagen gives each function its own data
        /// type rather than a field, so the name is the type's: <c>IsSprintingConditionData</c>
        /// is <c>IsSprinting</c>, whether the record was read from a file or built in memory.
        /// </summary>
        internal static IdleCondition Condition(IConditionGetter condition)
        {
            const string suffix = "ConditionData";
            string type = ((ILoquiObject)condition.Data).Registration.Name;

            return new IdleCondition(
                type.EndsWith(suffix, StringComparison.Ordinal) ? type[..^suffix.Length] : type,
                (ConditionOperator)(int)condition.CompareOperator,
                condition is IConditionFloatGetter number ? number.ComparisonValue : float.NaN,
                condition.Flags.HasFlag(Mutagen.Bethesda.Skyrim.Condition.Flag.OR));
        }
    }
}
