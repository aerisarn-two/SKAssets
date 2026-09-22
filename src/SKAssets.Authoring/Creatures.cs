using HKFBX.Hkx;
using HKFBX.Model;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using LeanMeshIO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using NIFSharp;
using SKAssets.Content.Assets;
using SKAssets.Content.Havok;
using SKAssets.Export;

namespace SKAssets.Authoring
{
    /// <summary>A new creature, made from an existing one and whatever of it is replaced.</summary>
    public sealed record NewCreature
    {
        /// <summary>The race to start from, by editor id or form key: <c>WolfRace</c>.</summary>
        public required string Template { get; init; }

        /// <summary>
        /// The creature's name, which its files and records are named from: <c>Direwolf</c> gives
        /// <c>Actors\...\Direwolf\DirewolfProject.hkx</c> and <c>DirewolfRace</c>, with the prefix.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>The race's name in game, where it shows one.</summary>
        public string? DisplayName { get; init; }

        /// <summary>
        /// An extracted <c>meshes</c> folder holding the three merged caches and the template's Havok
        /// project, its behaviours, its character and its animations.
        /// </summary>
        public required string SourceMeshes { get; init; }

        /// <summary>
        /// An FBX holding the skeleton: the rig, the ragdoll and the skeleton mesh, as
        /// <c>SKAssets.Export</c> writes one. Without it the creature keeps the template's.
        /// </summary>
        public string? Skeleton { get; init; }

        /// <summary>
        /// The template's bones by the names they have in <see cref="Skeleton"/>, for those not
        /// called the same: <c>Sabrecat_Head [Head]</c> to <c>head</c>. Read only when the
        /// skeleton is a rig of its own, to rebind the bones the behaviour names by index.
        /// </summary>
        public IReadOnlyDictionary<string, string>? BoneMap { get; init; }

        /// <summary>FBX meshes for the body the creature's skin wears. Without them, the template's.</summary>
        public IReadOnlyDictionary<ModelSlot, string>? Body { get; init; }

        /// <summary>
        /// FBX files of clips, each stack named for the animation it replaces or adds, as
        /// <c>SKAssets.Export</c>'s clip exchange reads them. Each is written uncompressed: exact,
        /// and with no Havok codec to run.
        /// </summary>
        public IReadOnlyList<string> Animations { get; init; } = [];

        /// <summary>
        /// Whether the creature gets movement types of its own rather than sharing the template's.
        /// The behaviour graph names them by its <c>iState_&lt;name&gt;</c> constants, so the constants
        /// are renamed in the copied graph and the records copied under the new names -- which is
        /// what lets its speeds differ. Implied by <see cref="Speeds"/>.
        /// </summary>
        public bool OwnMovementTypes { get; init; }

        /// <summary>
        /// Speeds for the creature's own movement types, by the template's movement type name as the
        /// graph spells it (<c>WolfDefault</c>); one not given keeps the template's speeds.
        /// </summary>
        public IReadOnlyDictionary<string, MovementSpeeds>? Speeds { get; init; }

        /// <summary>
        /// Sounds of its own, by the animation event that plays them (<c>NPCWolfBark</c>,
        /// <c>FootFront</c>): the audio files for each, <c>.wav</c> or <c>.xwm</c>. A creature's
        /// voice and feet are its body's footstep set -- a tag, an impact set and a sound for each
        /// event -- so the chain is copied for the events given and the rest stay the template's.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>>? Sounds { get; init; }

        /// <summary>An NPC to copy as the creature's first, by editor id: <c>EncWolf</c>.</summary>
        public string? Npc { get; init; }

        /// <summary>Put before every name written; the plugin's prefix when null.</summary>
        public string? Prefix { get; init; }
    }

    /// <summary>A movement type's eight speeds, in game units per second.</summary>
    public sealed record MovementSpeeds(
        float ForwardWalk, float ForwardRun, float BackWalk, float BackRun,
        float LeftWalk, float LeftRun, float RightWalk, float RightRun)
    {
        /// <summary>The same walk and run in every direction.</summary>
        public static MovementSpeeds Uniform(float walk, float run) => new(walk, run, walk, run, walk, run, walk, run);
    }

    /// <summary>What making a creature wrote.</summary>
    /// <param name="Race">The new race.</param>
    /// <param name="Records">Every record written for it.</param>
    /// <param name="Project">The Havok project's name, as the caches list it.</param>
    /// <param name="Files">Every file written, relative to the output folder.</param>
    /// <param name="Caches">What happened to each of the three merged caches.</param>
    /// <param name="Clips">The animations imported, when any were given.</param>
    /// <param name="Findings">What the mesh rules found wrong with the meshes written.</param>
    /// <param name="Notes">Choices made on the caller's behalf.</param>
    public sealed record CreatureResult(
        AuthoredRecord Race,
        IReadOnlyList<AuthoredRecord> Records,
        string Project,
        IReadOnlyList<string> Files,
        CacheAmendment Caches,
        ImportReport? Clips,
        IReadOnlyList<(string Mesh, MeshFinding Finding)> Findings,
        IReadOnlyList<string> Notes);

    public sealed partial class PluginAuthoring
    {
        private SkyrimCache? _caches;

        /// <summary>
        /// Makes a new creature from an existing one: its own Havok project, race, skin and
        /// entries in the three merged caches, with its skeleton, body and animations replaced
        /// from FBX where they are given.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A creature is a skeleton, a Havok project and a dozen records (<c>docs/new-race.md</c>),
        /// and the behaviour graph is the part nobody writes from nothing. So a new creature starts
        /// as a copy of one the game has, under a name of its own: the project, character,
        /// behaviours, rig and animations are copied into a folder beside the template's -- at the
        /// same depth, so a path the files spell with <c>..</c> still reaches what it did -- and the
        /// project is given its own entry in each cache, with the template's root motion, which is
        /// in no Havok file.
        /// </para>
        /// <para>
        /// The race is copied to wear the new project and skeleton. Its skin is copied to dress the
        /// new race, since an armour addon names the races it fits; the body part data is copied
        /// when the skeleton is replaced. The movement types and sound descriptors the behaviour
        /// names are shared with the template: the graph is unchanged, so the names it uses still
        /// find them.
        /// </para>
        /// <para>
        /// The merged caches are written into the output's <c>Meshes</c>, holding the game's
        /// entries and every creature made in this plugin. They replace the game's files, and a
        /// load order with another creature mod has to have them rebuilt over both -- which
        /// <c>animgen</c> does.
        /// </para>
        /// </remarks>
        public CreatureResult ImportCreature(NewCreature request)
        {
            ArgumentNullException.ThrowIfNull(request);

            string id = (request.Prefix ?? Prefix) + request.Name;
            var notes = new List<string>();
            var records = new List<AuthoredRecord>();
            var files = new List<string>();
            var findings = new List<(string, MeshFinding)>();
            string meshes = Path.Combine(OutputFolder, "Meshes");

            IRaceGetter race = Resolve<IRaceGetter>(request.Template, nameof(request));
            string graph = race.BehaviorGraph.Male?.File.GivenPath
                ?? throw new ArgumentException($"'{race.EditorID}' names no behaviour graph", nameof(request));
            string templateProject = Path.GetFileNameWithoutExtension(graph.Replace('\\', '/'));

            // Everything a request names is checked before anything is written.
            if (request.Sounds is { Count: > 0 } asked)
            {
                var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!race.Skin.IsNull && _cache.TryResolve<IArmorGetter>(race.Skin.FormKey, out var body))
                    foreach (var link in body.Armature)
                        if (_cache.TryResolve<IArmorAddonGetter>(link.FormKey, out var aa) && _cache.TryResolve<IFootstepSetGetter>(aa.FootstepSound.FormKey, out var steps))
                            foreach (var step in steps.EnumerateFormLinks())
                                if (_cache.TryResolve<IFootstepGetter>(step.FormKey, out var f) && f.Tag is { } tag) tags.Add(tag);

                if (asked.Keys.FirstOrDefault(k => !tags.Contains(k)) is { } stray)
                    throw new ArgumentException($"'{race.EditorID}' sounds no event '{stray}'; its body's footstep set has "
                        + (tags.Count == 0 ? "none" : string.Join(", ", tags.Order())), nameof(request));
            }

            // ------------------------------------------------ the Havok files

            SkyrimCache source = SkyrimCache.Load(request.SourceMeshes);
            AnimationDataProject entry = source.AnimationData.Project(templateProject) is { Block.HasAnimationCache: true } found
                ? found
                : throw new ArgumentException($"the caches in {request.SourceMeshes} list no actor '{templateProject}'", nameof(request));
            string projectFile = source.FindProjectFile(templateProject)
                ?? throw new ArgumentException($"'{templateProject}' has no project file under {request.SourceMeshes}", nameof(request));

            string sourceFolder = Path.GetDirectoryName(projectFile)!;
            string relativeFolder = Path.GetRelativePath(request.SourceMeshes, sourceFolder);
            string folder = Path.Combine(Path.GetDirectoryName(relativeFolder) ?? "", id);
            string project = id + "Project";
            string target = Path.Combine(meshes, folder);

            void Copy(string from, string relative)
            {
                string to = Path.Combine(target, relative.Replace('\\', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to, overwrite: true);
                files.Add(Path.Combine("Meshes", folder, relative).Replace('\\', '/'));
            }

            Copy(projectFile, project + ".hkx");

            foreach (string stored in entry.Block.Files)
            {
                if (stored.Replace('/', '\\').StartsWith(@"..\", StringComparison.Ordinal)) continue;
                if (HavokPath.Resolve(sourceFolder, stored) is not { } path) { notes.Add($"'{stored}' is not under {sourceFolder} and was not copied"); continue; }
                Copy(path, stored);
            }

            CharacterFile? character = ProjectFile.Load(projectFile).CharacterFiles
                .Select(f => HavokPath.Resolve(sourceFolder, f)).OfType<string>().Select(CharacterFile.Load).FirstOrDefault();

            int shared = 0;
            foreach (string animation in character?.AnimationNames ?? [])
            {
                if (animation.Replace('/', '\\').StartsWith(@"..\", StringComparison.Ordinal)) { shared++; continue; }
                if (HavokPath.Resolve(sourceFolder, animation) is { } path) Copy(path, animation);
                else notes.Add($"the animation '{animation}' is not under {sourceFolder} and was not copied");
            }
            if (shared > 0) notes.Add($"{shared} animations live outside the template's folder and are shared, not copied");

            // The skeleton mesh sits beside the rig; the race names it, from Meshes, in the
            // record's own case, which the disc need not share.
            string? newSkeletonModel = null;
            bool rigReplaced = false;
            if (race.SkeletalModel?.Male?.File.GivenPath is { } skeletonModel)
            {
                string fromMeshes = skeletonModel.Replace('\\', '/');
                if (fromMeshes.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)) fromMeshes = fromMeshes[7..];
                string under = relativeFolder.Replace('\\', '/').TrimEnd('/') + "/";

                if (fromMeshes.StartsWith(under, StringComparison.OrdinalIgnoreCase)
                    && HavokPath.Resolve(request.SourceMeshes, fromMeshes) is { } path)
                {
                    string inFolder = fromMeshes[under.Length..];
                    Copy(path, inFolder);
                    newSkeletonModel = Path.Combine(folder, inFolder).Replace('/', '\\');
                }
                else notes.Add("the template's skeleton mesh is not beside its project, and the race keeps it");
            }

            // ------------------------------------------------ a skeleton of its own

            if (request.Skeleton is not null)
            {
                FbxDocument document = FbxDocument.Load(request.Skeleton);
                NifXmlDatabase schema = NifXmlDatabase.LoadEmbedded();

                if (newSkeletonModel is not null)
                {
                    NifModel mesh = SkeletonExchange.ImportMesh(document, schema);
                    string path = Path.Combine(meshes, newSkeletonModel.Replace('\\', Path.DirectorySeparatorChar));
                    mesh.Save(path);
                    foreach (MeshFinding finding in MeshRules.Check(nameof(Race), Content.Nif.NifProfileReader.Read(mesh)))
                        findings.Add((newSkeletonModel, finding));
                }

                if (character is not null && !string.IsNullOrEmpty(character.RigName))
                {
                    string rig = Path.Combine(target, character.RigName.Replace('\\', Path.DirectorySeparatorChar));
                    SkeletonFile imported = SkeletonExchange.ImportHavok(document);
                    SkeletonFile template = HkxSkeletonFile.Read(rig);

                    if (SameStructure(template, imported))
                        HkxSkeletonFile.Write(rig, imported, rig);
                    else
                    {
                        // A rig of its own: the file is rebuilt around it, and every bone the
                        // copied character and behaviours name by index is found again by name.
                        HkxSkeletonBuilder.Write(rig, imported, rig);

                        var copied = entry.Block.Files
                            .Where(f => !f.Replace('/', '\\').StartsWith(@"..\", StringComparison.Ordinal))
                            .Select(f => HavokPath.Resolve(target, f)).OfType<string>()
                            .Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(rig), StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        RigRemap.Report remapped = RigRemap.Apply(copied, template.Rig, imported.Rig, template.Ragdoll, imported.Ragdoll, request.BoneMap);
                        notes.Add($"a rig of its own: {template.Rig.Count} bones became {imported.Rig.Count}, "
                            + $"{template.Bodies.Count} bodies {imported.Bodies.Count}; {remapped.Files.Count} files rebound by bone name");
                        notes.AddRange(remapped.Notes);
                        rigReplaced = true;
                    }
                }
            }

            // ------------------------------------------------ movement types of its own

            var ownTypes = new Dictionary<string, MovementType>(StringComparer.OrdinalIgnoreCase);
            if (request.OwnMovementTypes || request.Speeds is { Count: > 0 })
            {
                var byName = new Dictionary<string, IMovementTypeGetter>(StringComparer.OrdinalIgnoreCase);
                foreach (var movt in _cache.PriorityOrder.WinningOverrides<IMovementTypeGetter>())
                    if ((movt.Name ?? movt.EditorID) is { Length: > 0 } name) byName.TryAdd(name, movt);

                // The engine finds a movement type through the constants of the creature's root
                // graph; a shared graph beside it declares every species' (the quadruped graph 17),
                // so only the root's are the creature's. They are renamed in every copied graph,
                // since the graphs a project joins share variables by name.
                var walk = HKSK.Behavior.ProjectWalk.Of(projectFile);
                var own = HKSK.Behavior.BehaviorRoot.Of(projectFile) is { } root
                    ? HKSK.Behavior.StateConstants.Of(walk, root).Keys.Select(k => k["iState_".Length..]).Where(byName.ContainsKey).ToList()
                    : [];

                var renamed = RenameStateConstants(target, entry.Block.Files, own, id);
                foreach ((string old, string renamedTo) in renamed)
                {
                    var made = Plugin.MovementTypes.DuplicateInAsNewRecord<MovementType, IMovementTypeGetter>(byName[old], renamedTo);
                    made.Name = renamedTo;
                    if (request.Speeds?.GetValueOrDefault(old) is { } speeds)
                        (made.ForwardWalk, made.ForwardRun, made.BackWalk, made.BackRun, made.LeftWalk, made.LeftRun, made.RightWalk, made.RightRun) =
                            (speeds.ForwardWalk, speeds.ForwardRun, speeds.BackWalk, speeds.BackRun, speeds.LeftWalk, speeds.LeftRun, speeds.RightWalk, speeds.RightRun);
                    ownTypes[old] = made;
                    records.Add(new AuthoredRecord(made.FormKey, nameof(MovementType), made.EditorID!, byName[old].FormKey));
                }

                if (request.Speeds?.Keys.FirstOrDefault(k => !renamed.ContainsKey(k)) is { } stray)
                    throw new ArgumentException($"the template's graph names no movement type '{stray}'", nameof(request));
                notes.Add($"{renamed.Count} movement types of its own: {string.Join(", ", renamed.Values)}");
            }

            // ------------------------------------------------ the records

            Race copy = Plugin.Races.DuplicateInAsNewRecord<Race, IRaceGetter>(race, id + "Race");
            records.Add(new AuthoredRecord(copy.FormKey, nameof(Race), copy.EditorID!, race.FormKey));
            if (request.DisplayName is not null) copy.Name = request.DisplayName;

            string behavior = Path.Combine(folder, project + ".hkx").Replace('/', '\\');
            copy.BehaviorGraph = new GenderedItem<Model?>(new Model { File = behavior }, new Model { File = behavior });
            if (newSkeletonModel is not null)
                copy.SkeletalModel = new GenderedItem<SimpleModel?>(new SimpleModel { File = newSkeletonModel }, new SimpleModel { File = newSkeletonModel });

            // A race's default movement types follow the graph's to the creature's own copies.
            foreach (var link in new[] { copy.BaseMovementDefaultWalk, copy.BaseMovementDefaultRun, copy.BaseMovementDefaultSwim,
                                         copy.BaseMovementDefaultFly, copy.BaseMovementDefaultSneak, copy.BaseMovementDefaultSprint })
                if (!link.IsNull && _cache.TryResolve<IMovementTypeGetter>(link.FormKey, out var movement)
                    && ownTypes.GetValueOrDefault(movement.Name ?? movement.EditorID ?? "") is { } own)
                    link.SetTo(own);

            if (!race.Skin.IsNull && _cache.TryResolve<IArmorGetter>(race.Skin.FormKey, out var skin))
            {
                Armor dressed;
                if (request.Body is { Count: > 0 } body)
                {
                    ImportResult worn = Import(new AssetImport
                    {
                        Kind = AuthoredKind.Armor, Template = skin.FormKey.ToString(), EditorId = id + "Skin", Prefix = "",
                        Fbx = body, MeshFolder = folder.Replace('/', '\\'), Recipes = false,
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
                        var aa = Plugin.ArmorAddons.DuplicateInAsNewRecord<ArmorAddon, IArmorAddonGetter>(addon, $"{id}SkinAA{(dressed.Armature.Count > 1 ? i : "")}");
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

                if (request.Sounds is { Count: > 0 } sounds)
                    Voice(dressed, sounds, id, records, files, notes);
            }
            else if (request.Sounds is { Count: > 0 })
                throw new ArgumentException($"'{race.EditorID}' wears no skin, so its body has no footstep set to give sounds to", nameof(request));

            if (request.Skeleton is not null && newSkeletonModel is not null
                && !race.BodyPartData.IsNull && _cache.TryResolve<IBodyPartDataGetter>(race.BodyPartData.FormKey, out var parts))
            {
                var bptd = Plugin.BodyParts.DuplicateInAsNewRecord<BodyPartData, IBodyPartDataGetter>(parts, id + "BodyPartData");
                bptd.Model = new Model { File = newSkeletonModel };

                // A part names skeleton nodes -- the torso Sabrecat_[pelv], VATS aiming at
                // Sabrecat_Head [Head] -- which a rig of its own calls something else.
                if (request.BoneMap is { Count: > 0 } map)
                    foreach (var part in bptd.Parts)
                    {
                        if (map.TryGetValue(part.PartNode, out string? node)) part.PartNode = node;
                        if (map.TryGetValue(part.VatsTarget, out string? vats)) part.VatsTarget = vats;
                    }

                copy.BodyPartData.SetTo(bptd);
                records.Add(new AuthoredRecord(bptd.FormKey, nameof(BodyPartData), bptd.EditorID!, parts.FormKey));
            }

            // ------------------------------------------------ idle records

            CopyIdles(relativeFolder, folder, race, copy, id, records, notes);

            if (request.Npc is not null)
            {
                INpcGetter npc = Resolve<INpcGetter>(request.Npc, nameof(request));
                var made = Plugin.Npcs.DuplicateInAsNewRecord<Npc, INpcGetter>(npc, id + "Npc");
                made.Race.SetTo(copy);
                if (!made.WornArmor.IsNull) made.WornArmor.SetTo(copy.Skin.FormKey);
                if (request.DisplayName is not null) made.Name = request.DisplayName;
                records.Add(new AuthoredRecord(made.FormKey, nameof(Npc), made.EditorID!, npc.FormKey));
            }

            // ------------------------------------------------ the caches

            SkyrimCache caches = Caches(request.SourceMeshes, meshes, files);
            if (caches.AnimationData.Project(project) is null)
                caches.AnimationData.Projects.Add(new AnimationDataProject
                {
                    Name = project + ".txt",
                    Block = entry.Block.Clone(),
                    Movements = entry.Movements?.Clone() ?? new ProjectDataBlock(),
                });

            // The cache finds a project's files through an index it builds once; read afresh, it
            // sees the files just copied.
            caches.Save();
            caches = SkyrimCache.Load(meshes);

            ImportReport? clips = null;
            if (request.Animations.Count > 0 && caches.OpenActor(project) is { } actor)
            {
                var imported = new List<ImportedClip>();
                var ignored = new List<string>();
                var failed = new Dictionary<string, string>();

                foreach (string fbx in request.Animations)
                {
                    ImportReport report = ClipExchange.ImportClips(FbxDocument.Load(fbx), actor);
                    imported.AddRange(report.Clips);
                    ignored.AddRange(report.Ignored);
                    foreach (var (clip, why) in report.Failed) failed[clip] = why;
                }

                if (actor.CharacterModified) actor.SaveCharacter();
                clips = new ImportReport(imported, ignored, failed);

                // An animation the clips did not replace is still the template's, bound to a rig
                // the creature no longer has.
                if (rigReplaced)
                {
                    var replaced = imported.Select(c => c.StoredName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var stale = actor.Animations.Where(a => !replaced.Contains(a.StoredName)).Select(a => a.StoredName).ToList();
                    if (stale.Count > 0)
                        notes.Add($"{stale.Count} of the template's animations were not replaced and are bound to its rig: {string.Join(", ", stale)}");
                }
            }

            var gameRecords = GameRecordReader.Read([.. _cache.ListedOrder.OfType<ISkyrimModGetter>(), Plugin]);
            CacheAmendment amended = CacheGeneration.Amend(caches, project, gameRecords);
            caches.Save();
            _caches = caches;

            return new CreatureResult(records[0], records, project, [.. files.Distinct()], amended, clips, findings, notes);
        }

        /// <summary>
        /// Gives the new behaviour the template's idle records: every <c>IDLE</c> naming a
        /// behaviour file under the template's folder, copied to name the same file under the
        /// new one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An idle record is how the AI's actions reach a graph -- <c>ActionMoveStart</c> becomes
        /// <c>moveStart</c> by way of an idle under it -- and it serves only the actors whose
        /// behaviour is the file it names. The sabre cat's 49 are all
        /// <c>Meshes\Actors\SabreCat\Behaviors\SabreCatBehavior.hkx</c>: moving, turning,
        /// swimming, staggering, recoiling, bleeding out, sitting, lying down, dying. A copy of
        /// its graph under another folder is none of those files, so without copies of its idles
        /// the creature stands where it is placed and never takes a step.
        /// </para>
        /// <para>
        /// Each copy keeps its place in its tree: an idle's two links are its parent and the
        /// sibling checked before it, and where either was copied too the copy links to the copy.
        /// A copy whose parent is an action or another creature's idle hangs beside the original,
        /// which the engine passes over for any actor but the template. A condition on the
        /// template's race is moved to the new one.
        /// </para>
        /// </remarks>
        private void CopyIdles(string templateFolder, string newFolder, IRaceGetter templateRace, Race newRace, string id,
            List<AuthoredRecord> records, List<string> notes)
        {
            static string Normal(string path)
            {
                string p = path.Replace('/', '\\').TrimStart('\\');
                return p.StartsWith(@"meshes\", StringComparison.OrdinalIgnoreCase) ? p[7..] : p;
            }

            string from = Normal(templateFolder).TrimEnd('\\') + "\\";
            string to = Normal(newFolder).TrimEnd('\\') + "\\";

            bool Names(IIdleAnimationGetter idle) =>
                idle.Filename?.GivenPath is { Length: > 0 } f && Normal(f).StartsWith(from, StringComparison.OrdinalIgnoreCase);

            // Its own file, or an ancestor's: an idle that names no file serves the graph its
            // parent does, which is how se-cmd's retarget reads a chain too.
            bool Serves(IIdleAnimationGetter idle)
            {
                var seen = new HashSet<FormKey>();
                for (IIdleAnimationGetter? at = idle; at is not null && seen.Add(at.FormKey);)
                {
                    if (Names(at)) return true;
                    if (at.Filename?.GivenPath is { Length: > 0 }) return false;
                    at = at.RelatedIdles.Count > 0 && _cache.TryResolve<IIdleAnimationGetter>(at.RelatedIdles[0].FormKey, out var parent) ? parent : null;
                }
                return false;
            }

            var originals = _cache.PriorityOrder.WinningOverrides<IIdleAnimationGetter>().Where(Serves).ToList();

            var copies = new Dictionary<FormKey, IdleAnimation>();
            foreach (IIdleAnimationGetter idle in originals)
            {
                string editorId = id + (idle.EditorID ?? idle.FormKey.ID.ToString("X6"));
                IdleAnimation made = Plugin.IdleAnimations.DuplicateInAsNewRecord<IdleAnimation, IIdleAnimationGetter>(idle, editorId);

                if (Names(idle))
                {
                    string given = idle.Filename!.GivenPath;
                    char separator = given.Contains('/') ? '/' : '\\';
                    string rest = Normal(given)[from.Length..];
                    made.Filename = new Mutagen.Bethesda.Plugins.Assets.AssetLink<Mutagen.Bethesda.Skyrim.Assets.SkyrimBehaviorAssetType>(
                        ("Meshes\\" + to + rest).Replace('\\', separator));
                }

                foreach (IConditionGetter condition in idle.Conditions.Select((c, i) => made.Conditions[i]))
                    if (condition is Condition { Data: GetIsRaceConditionData isRace } && isRace.Race.Link.FormKey == templateRace.FormKey)
                        isRace.Race.Link.SetTo(newRace);

                copies[idle.FormKey] = made;
                records.Add(new AuthoredRecord(made.FormKey, nameof(IdleAnimation), made.EditorID!, idle.FormKey));
            }

            foreach (IdleAnimation made in copies.Values)
                for (int i = 0; i < made.RelatedIdles.Count; i++)
                    if (copies.TryGetValue(made.RelatedIdles[i].FormKey, out IdleAnimation? linked))
                        made.RelatedIdles[i] = linked.ToLink<IIdleRelationGetter>();

            notes.Add($"{copies.Count} idle records copied onto the new behaviour");
        }

        /// <summary>
        /// Whether a skeleton has the template's structure -- the same bones in the same order and
        /// the same bodies -- so that editing the template by name writes all of it.
        /// </summary>
        private static bool SameStructure(SkeletonFile template, SkeletonFile imported) =>
            template.Rig.Bones.Select(b => b.Name).SequenceEqual(imported.Rig.Bones.Select(b => b.Name), StringComparer.OrdinalIgnoreCase)
            && template.Bodies.Select(b => b.Name).Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(imported.Bodies.Select(b => b.Name).Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gives a creature's body sounds of its own: its footstep set copied, and for each event
        /// given, the footstep with that tag, its impact set, the impacts in it and the sound they
        /// play -- the last naming the new audio files.
        /// </summary>
        /// <remarks>
        /// The chain is the one the masters have for every creature sound: the wolf's footstep
        /// <c>NPCWolfBarkFootstep</c> is tagged <c>NPCWolfBark</c>, the event its animations send, and
        /// its impact set's 78 entries, one per material, all play one impact whose sound is
        /// <c>NPCWolfBark</c>.
        /// </remarks>
        private void Voice(Armor skin, IReadOnlyDictionary<string, IReadOnlyList<string>> sounds, string id,
            List<AuthoredRecord> records, List<string> files, List<string> notes)
        {
            var addons = skin.Armature.Select(l => Plugin.ArmorAddons.TryGetValue(l.FormKey, out var aa) ? aa : null)
                .OfType<ArmorAddon>().Where(aa => !aa.FootstepSound.IsNull).ToList();
            if (addons.Count == 0) throw new ArgumentException("the creature's body names no footstep set to give sounds to");
            if (!_cache.TryResolve<IFootstepSetGetter>(addons[0].FootstepSound.FormKey, out var shared))
                throw new ArgumentException("the creature's footstep set is not in the load order");

            var set = Plugin.FootstepSets.DuplicateInAsNewRecord<FootstepSet, IFootstepSetGetter>(shared, id + "FootstepSet");
            records.Add(new AuthoredRecord(set.FormKey, nameof(FootstepSet), set.EditorID!, shared.FormKey));
            foreach (var aa in addons) aa.FootstepSound.SetTo(set);

            var done = new Dictionary<FormKey, Footstep>();
            var voiced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var list in new[] { set.WalkForwardFootsteps, set.RunForwardFootsteps, set.WalkForwardAlternateFootsteps,
                                         set.RunForwardAlternateFootsteps, set.WalkForwardAlternateFootsteps2 })
                for (int i = 0; i < list.Count; i++)
                {
                    if (!_cache.TryResolve<IFootstepGetter>(list[i].FormKey, out var step)) continue;
                    if (step.Tag is not { } tag || !sounds.TryGetValue(tag, out var audio)) continue;

                    if (!done.TryGetValue(step.FormKey, out Footstep? made))
                    {
                        done[step.FormKey] = made = Own(step, tag, audio);
                        voiced.Add(tag);
                    }
                    list[i] = made.ToLink<IFootstepGetter>();
                }

            if (sounds.Keys.FirstOrDefault(k => !voiced.Contains(k)) is { } stray)
                throw new ArgumentException($"the creature's footstep set has no event '{stray}'; it has "
                    + string.Join(", ", shared.EnumerateFormLinks().Select(l => _cache.TryResolve<IFootstepGetter>(l.FormKey, out var f) ? f.Tag : null).OfType<string>().Distinct()));
            notes.Add($"{voiced.Count} sounds of its own: {string.Join(", ", voiced)}");

            Footstep Own(IFootstepGetter step, string tag, IReadOnlyList<string> audio)
            {
                string name = id + tag;
                var footstep = Plugin.Footsteps.DuplicateInAsNewRecord<Footstep, IFootstepGetter>(step, name + "Footstep");
                records.Add(new AuthoredRecord(footstep.FormKey, nameof(Footstep), footstep.EditorID!, step.FormKey));

                if (!_cache.TryResolve<IImpactDataSetGetter>(step.ImpactDataSet.FormKey, out var impacts)) return footstep;
                var set = Plugin.ImpactDataSets.DuplicateInAsNewRecord<ImpactDataSet, IImpactDataSetGetter>(impacts, name + "ImpactSet");
                records.Add(new AuthoredRecord(set.FormKey, nameof(ImpactDataSet), set.EditorID!, impacts.FormKey));
                footstep.ImpactDataSet.SetTo(set);

                // The files, under Sound\FX as vanilla keeps them, and one sound naming them all.
                var paths = new List<string>();
                foreach (string file in audio)
                {
                    string relative = Path.Combine("Sound", "FX", id, tag, Path.GetFileName(file));
                    string to = Path.Combine(OutputFolder, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(file, to, overwrite: true);
                    files.Add(relative.Replace('\\', '/'));
                    paths.Add(Path.Combine("Data", relative).Replace('/', '\\'));
                }

                SoundDescriptor? sound = null;
                var impactCopies = new Dictionary<FormKey, Impact>();
                for (int i = 0; i < set.Impacts.Count; i++)
                {
                    FormKey was = set.Impacts[i].Impact.FormKey;
                    if (!impactCopies.TryGetValue(was, out Impact? impact))
                    {
                        if (!_cache.TryResolve<IImpactGetter>(was, out var from)) continue;
                        impact = Plugin.Impacts.DuplicateInAsNewRecord<Impact, IImpactGetter>(from, $"{name}Impact{(impactCopies.Count == 0 ? "" : impactCopies.Count)}");
                        records.Add(new AuthoredRecord(impact.FormKey, nameof(Impact), impact.EditorID!, from.FormKey));

                        if (sound is null && _cache.TryResolve<ISoundDescriptorGetter>(from.Sound1.FormKey, out var played))
                        {
                            sound = Plugin.SoundDescriptors.DuplicateInAsNewRecord<SoundDescriptor, ISoundDescriptorGetter>(played, name);
                            sound.SoundFiles.Clear();
                            foreach (string path in paths)
                                sound.SoundFiles.Add(new Mutagen.Bethesda.Plugins.Assets.AssetLink<Mutagen.Bethesda.Skyrim.Assets.SkyrimSoundAssetType>(path));
                            records.Add(new AuthoredRecord(sound.FormKey, nameof(SoundDescriptor), sound.EditorID!, played.FormKey));
                        }
                        if (sound is not null) impact.Sound1.SetTo(sound);
                        impactCopies[was] = impact;
                    }
                    set.Impacts[i].Impact.SetTo(impact);
                }

                return footstep;
            }
        }

        /// <summary>
        /// Renames, in the copied behaviour files, every <c>iState_&lt;name&gt;</c> constant whose name
        /// is a movement type's -- in the variable names, and wherever an expression or a transition's
        /// condition spells it -- to one of the creature's own.
        /// </summary>
        /// <returns>The movement type names renamed, old to new.</returns>
        private static Dictionary<string, string> RenameStateConstants(
            string folder, IEnumerable<string> files, IEnumerable<string> movementTypes, string id)
        {
            var known = new HashSet<string>(movementTypes, StringComparer.OrdinalIgnoreCase);
            var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            const string Prefix = "iState_";

            foreach (string stored in files)
            {
                if (stored.Replace('/', '\\').StartsWith(@"..\", StringComparison.Ordinal)) continue;
                if (HavokPath.Resolve(folder, stored) is not { } path) continue;

                HavokFile file;
                try { file = HavokFile.Load(path); }
                catch (Exception e) when (e is not OutOfMemoryException) { continue; }

                bool changed = false;
                foreach (var strings in file.All<HKX2.hkbBehaviorGraphStringData>())
                    for (int i = 0; i < strings.m_variableNames.Count; i++)
                    {
                        string name = strings.m_variableNames[i];
                        if (!name.StartsWith(Prefix, StringComparison.Ordinal)) continue;

                        string movement = name[Prefix.Length..];
                        if (!known.Contains(movement)) continue;

                        if (!renamed.TryGetValue(movement, out string? to)) renamed[movement] = to = $"{id}_{movement}";
                        strings.m_variableNames[i] = Prefix + to;
                        changed = true;
                    }

                foreach (var expression in file.All<HKX2.hkbExpressionData>())
                    changed |= Rewrite(expression.m_expression, e => expression.m_expression = e);
                foreach (var condition in file.All<HKX2.hkbExpressionCondition>())
                    changed |= Rewrite(condition.m_expression, e => condition.m_expression = e);

                if (changed) file.Save(path);
            }

            return renamed;

            bool Rewrite(string? text, Action<string> set)
            {
                if (string.IsNullOrEmpty(text)) return false;
                string result = text;
                foreach ((string from, string to) in renamed)
                    result = System.Text.RegularExpressions.Regex.Replace(
                        result, $@"\b{Prefix}{System.Text.RegularExpressions.Regex.Escape(from)}\b", Prefix + to);
                if (result == text) return false;
                set(result);
                return true;
            }
        }

        /// <summary>
        /// The merged caches being written: the game's, copied into the output the first time a
        /// creature is made, and the same files every time after.
        /// </summary>
        private SkyrimCache Caches(string sourceMeshes, string meshes, List<string> files)
        {
            if (_caches is not null) return _caches;

            Directory.CreateDirectory(meshes);
            foreach (string name in new[] { SkyrimCache.AnimationDataFileName, SkyrimCache.AnimationSetDataFileName, SkyrimCache.SpeedDataFileName })
            {
                string from = Path.Combine(sourceMeshes, name);
                if (!File.Exists(from)) continue;
                File.Copy(from, Path.Combine(meshes, name), overwrite: true);
                files.Add(Path.Combine("Meshes", name).Replace('\\', '/'));
            }

            return _caches = SkyrimCache.Load(meshes);
        }
    }
}
