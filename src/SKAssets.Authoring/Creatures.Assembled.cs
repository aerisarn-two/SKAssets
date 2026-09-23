using HKSK.Model;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using SKAssets.Content.Assets;

namespace SKAssets.Authoring
{
    /// <summary>A creature whose Havok project was built rather than copied.</summary>
    /// <param name="Template">
    /// The race to take the records from, by editor id or form key. Its project is not used;
    /// only its records are, and the nearer a creature it is the less has to be corrected.
    /// </param>
    /// <param name="Name">
    /// The creature's name, which its records are named after: <c>BoneSkeleton</c> gives
    /// <c>BoneSkeletonRace</c>, with the plugin's prefix.
    /// </param>
    /// <param name="ProjectFolder">
    /// Where the creature's Havok files sit, from <c>Meshes</c>: <c>actors\BoneSkeleton</c>.
    /// </param>
    /// <param name="ProjectFile">The project packfile the race names, inside that folder.</param>
    /// <param name="BehaviourFile">
    /// The behaviour packfile inside that folder, which is what the creature's idle records
    /// are matched to it by.
    /// </param>
    /// <param name="TemplateFolder">
    /// Where the template's own Havok files sit, from <c>Meshes</c>, which is how its idle
    /// records are recognised: <c>actors\draugr</c>.
    /// </param>
    /// <param name="SkeletonModel">The render skeleton, from <c>Meshes</c>, where there is one.</param>
    /// <param name="Body">The creature's body meshes, by slot, where they are its own.</param>
    /// <param name="Movement">
    /// The movement type the creature's clips describe, named as its graph declares it. Its
    /// speeds are what the clips deliver, which is the way round an assembled creature works:
    /// the record is authored from the animations and not the animations from the record.
    /// </param>
    /// <param name="Attacks">
    /// The attack events the creature's graph answers, as its plan names them. The race's own
    /// list is replaced by them, because a race's attack list is what combat chooses from: it
    /// picks one and sends its event, and a creature whose list names events its graph has
    /// never heard of is ordered to attack and stands there. Empty leaves the template's list
    /// and says so.
    /// </param>
    /// <param name="Npc">An NPC to copy as the creature's first, by editor id.</param>
    /// <param name="DisplayName">The race's name in game.</param>
    /// <param name="Prefix">Put before every name written; the plugin's prefix when null.</param>
    public sealed record AssembledCreature(
        string Template,
        string Name,
        string ProjectFolder,
        string ProjectFile,
        string BehaviourFile,
        string TemplateFolder,
        string? SkeletonModel = null,
        IReadOnlyDictionary<ModelSlot, string>? Body = null,
        (string Name, MovementSpeeds Speeds)? Movement = null,
        IReadOnlyList<string>? Attacks = null,
        string? Npc = null,
        string? DisplayName = null,
        string? Prefix = null);

    public sealed partial class PluginAuthoring
    {
        /// <summary>
        /// Writes the records a creature needs, for one whose Havok project was built rather
        /// than copied from another creature.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="ImportCreature"/> makes a creature by copying one: its project, its
        /// graphs, its animations and its records all come from a template and are renamed.
        /// A creature assembled from an FBX has the first four already and wants only the
        /// last, so copying would overwrite the very thing that was built.
        /// </para>
        /// <para>
        /// The records still come from a template, because there is nowhere else for them to
        /// come from. A race carries around a hundred fields -- how the creature is lit, what
        /// it is made of, what it hears, which of its bones a spell is cast from -- and none
        /// of that is in an FBX or worth authoring by hand. So a race is copied and the few
        /// fields that name the creature's own files are repointed at the built ones.
        /// </para>
        /// <para>
        /// The idles are the part that matters most and the part that goes wrong quietly. An
        /// idle is matched to an actor by the behaviour file it names, so a creature whose
        /// idle records still name the template's file gets none of its own: no
        /// <c>moveStart</c>, so it slides where it walks, and no <c>combatStanceStart</c>, so
        /// it never draws and the combat AI never orders an attack. The same copying the
        /// template route uses is used here, pointed at the built behaviour.
        /// </para>
        /// </remarks>
        public CreatureResult ImportAssembledCreature(AssembledCreature request)
        {
            ArgumentNullException.ThrowIfNull(request);

            string id = (request.Prefix ?? Prefix) + request.Name;
            var notes = new List<string>();
            var records = new List<AuthoredRecord>();
            var files = new List<string>();
            var findings = new List<(string, MeshFinding)>();

            IRaceGetter race = Resolve<IRaceGetter>(request.Template, nameof(request));
            string folder = request.ProjectFolder.Replace('/', '\\').TrimEnd('\\');

            // ---- the race, pointed at what was built rather than at what was copied
            Race copy = Plugin.Races.DuplicateInAsNewRecord<Race, IRaceGetter>(race, id + "Race");
            records.Add(new AuthoredRecord(copy.FormKey, nameof(Race), copy.EditorID!, race.FormKey));
            if (request.DisplayName is not null) copy.Name = request.DisplayName;

            string graph = Path.Combine(folder, request.ProjectFile).Replace('/', '\\');
            copy.BehaviorGraph = new GenderedItem<Model?>(new Model { File = graph }, new Model { File = graph });
            notes.Add($"the race runs '{graph}', which was assembled rather than copied");

            if (request.SkeletonModel is { Length: > 0 } rig)
            {
                string model = rig.Replace('/', '\\');
                copy.SkeletalModel = new GenderedItem<SimpleModel?>(new SimpleModel { File = model }, new SimpleModel { File = model });
                notes.Add($"the race wears '{model}'");
            }

            // ---- a movement type of its own, whose speeds the clips measured
            if (request.Movement is { } movement)
            {
                var byName = new Dictionary<string, IMovementTypeGetter>(StringComparer.OrdinalIgnoreCase);
                foreach (var movt in _cache.PriorityOrder.WinningOverrides<IMovementTypeGetter>())
                    if ((movt.Name ?? movt.EditorID) is { Length: > 0 } name) byName.TryAdd(name, movt);

                // A record of its own, under the name the assembled graph declares as its
                // iState constant -- which is what the engine looks the movement type up by.
                IMovementTypeGetter from = byName.GetValueOrDefault(movement.Name)
                    ?? byName.GetValueOrDefault("NPCDefaultMovementType")
                    ?? byName.Values.First();

                var made = Plugin.MovementTypes.DuplicateInAsNewRecord<MovementType, IMovementTypeGetter>(from, id + "MovementType");
                made.Name = movement.Name;
                (made.ForwardWalk, made.ForwardRun, made.BackWalk, made.BackRun,
                 made.LeftWalk, made.LeftRun, made.RightWalk, made.RightRun) =
                    (movement.Speeds.ForwardWalk, movement.Speeds.ForwardRun,
                     movement.Speeds.BackWalk, movement.Speeds.BackRun,
                     movement.Speeds.LeftWalk, movement.Speeds.LeftRun,
                     movement.Speeds.RightWalk, movement.Speeds.RightRun);

                records.Add(new AuthoredRecord(made.FormKey, nameof(MovementType), made.EditorID!, from.FormKey));

                // Every default the race names follows to it: the creature moves at its own
                // speeds whichever of them the engine asks for.
                foreach (var link in new[]
                         {
                             copy.BaseMovementDefaultWalk, copy.BaseMovementDefaultRun,
                             copy.BaseMovementDefaultSwim, copy.BaseMovementDefaultFly,
                             copy.BaseMovementDefaultSneak, copy.BaseMovementDefaultSprint,
                         })
                    if (!link.IsNull) link.SetTo(made);

                notes.Add($"a movement type '{movement.Name}' of its own, at the speeds its clips deliver");
            }

            // ---- the skin, and a body of its own where one was given
            if (!race.Skin.IsNull && _cache.TryResolve<IArmorGetter>(race.Skin.FormKey, out var skin))
            {
                Armor dressed;
                if (request.Body is { Count: > 0 } body)
                {
                    ImportResult worn = Import(new AssetImport
                    {
                        Kind = AuthoredKind.Armor,
                        Template = skin.FormKey.ToString(),
                        EditorId = id + "Skin",
                        Prefix = "",
                        Fbx = body,
                        MeshFolder = folder,
                        Recipes = false,
                        DropUnlistedAddons = true,
                    });

                    records.AddRange(worn.Records);
                    files.AddRange(worn.Meshes.Concat(worn.Textures));
                    findings.AddRange(worn.Findings);
                    dressed = Plugin.Armors[worn.Record.FormKey];
                }
                else
                {
                    dressed = Plugin.Armors.DuplicateInAsNewRecord<Armor, IArmorGetter>(skin, id + "Skin");
                    records.Add(new AuthoredRecord(dressed.FormKey, nameof(Armor), dressed.EditorID!, skin.FormKey));

                    for (int i = 0; i < dressed.Armature.Count; i++)
                    {
                        if (!_cache.TryResolve<IArmorAddonGetter>(dressed.Armature[i].FormKey, out var addon)) continue;
                        var aa = Plugin.ArmorAddons.DuplicateInAsNewRecord<ArmorAddon, IArmorAddonGetter>(
                            addon, $"{id}SkinAA{(dressed.Armature.Count > 1 ? i : "")}");
                        records.Add(new AuthoredRecord(aa.FormKey, nameof(ArmorAddon), aa.EditorID!, addon.FormKey));
                        dressed.Armature[i] = aa.ToLink<IArmorAddonGetter>();
                    }
                }

                // An addon dresses the races it names, so the copies name the new one.
                dressed.Race.SetTo(copy);
                foreach (var link in dressed.Armature)
                    if (Plugin.ArmorAddons.TryGetValue(link.FormKey, out var aa))
                    {
                        aa.Race.SetTo(copy);
                        aa.AdditionalRaces.Clear();
                    }

                copy.Skin.SetTo(dressed);
            }
            else notes.Add($"'{race.EditorID}' wears no skin, so the creature has none either");

            // ---- the body parts, which name skeleton nodes and a skeleton model
            if (request.SkeletonModel is { Length: > 0 } model2 && !race.BodyPartData.IsNull
                && _cache.TryResolve<IBodyPartDataGetter>(race.BodyPartData.FormKey, out var parts))
            {
                var bptd = Plugin.BodyParts.DuplicateInAsNewRecord<BodyPartData, IBodyPartDataGetter>(parts, id + "BodyPartData");
                bptd.Model = new Model { File = model2.Replace('/', '\\') };
                copy.BodyPartData.SetTo(bptd);
                records.Add(new AuthoredRecord(bptd.FormKey, nameof(BodyPartData), bptd.EditorID!, parts.FormKey));
            }

            // ---- the attacks the race offers combat, which must be ones the graph answers
            if (request.Attacks is { Count: > 0 } attacks)
            {
                if (copy.Attacks.Count == 0)
                    notes.Add($"'{race.EditorID}' offers combat no attacks, so neither does the creature");
                else
                {
                    // One of the template's kept as the pattern: an attack entry carries a spell,
                    // the angles it may be started at and a damage multiplier, none of which an
                    // animation knows and all of which combat reads.
                    Attack pattern = copy.Attacks[0].DeepCopy();
                    var was = copy.Attacks.Select(a => a.AttackEvent).ToList();

                    copy.Attacks.Clear();
                    foreach (string sends in attacks)
                    {
                        Attack made = pattern.DeepCopy();
                        made.AttackEvent = sends;
                        copy.Attacks.Add(made);
                    }

                    notes.Add($"the race offers combat {attacks.Count} attacks -- {string.Join(", ", attacks)} -- "
                        + $"in place of the template's {was.Count}, which name events this graph never hears");
                }
            }
            else if (copy.Attacks.Count > 0)
                notes.Add($"the race still offers combat the template's {copy.Attacks.Count} attacks, whose events "
                    + "this creature's graph may not answer: it would be ordered to attack and stand there");

            // ---- the idles, which is how the creature is told to do anything at all
            string behaviour = request.BehaviourFile.Replace('/', '\\').TrimStart('\\');

            CopyIdles(
                request.TemplateFolder, folder, race, copy, id,
                // Every behaviour of the template's is the creature's one behaviour: an
                // assembled creature has a single graph where a copied one may have four.
                _ => behaviour,
                new Dictionary<string, string>(),
                records, notes);

            if (request.Npc is { Length: > 0 })
            {
                INpcGetter npc = Resolve<INpcGetter>(request.Npc, nameof(request));
                var made = Plugin.Npcs.DuplicateInAsNewRecord<Npc, INpcGetter>(npc, id + "Npc");
                made.Race.SetTo(copy);

                // An actor takes its attacks from its attack race where it names one, and the
                // template's names the template: the creature would swing by the template's
                // reach and damage and ignore its own.
                if (made.AttackRace.FormKey == race.FormKey) made.AttackRace.SetTo(copy);
                if (!made.WornArmor.IsNull) made.WornArmor.SetTo(copy.Skin.FormKey);
                if (request.DisplayName is not null) made.Name = request.DisplayName;

                records.Add(new AuthoredRecord(made.FormKey, nameof(Npc), made.EditorID!, npc.FormKey));
            }

            return new CreatureResult(
                records[0], records, Path.GetFileNameWithoutExtension(request.ProjectFile),
                [.. files.Distinct()],
                // The caches were amended when the creature was installed, which is HKSK's
                // business and happened before any of this: nothing here touches them.
                new CacheAmendment(Path.GetFileNameWithoutExtension(request.ProjectFile),
                    Amendment.Unchanged, Amendment.Unchanged, Amendment.Unchanged),
                null, findings, notes);
        }
    }
}
