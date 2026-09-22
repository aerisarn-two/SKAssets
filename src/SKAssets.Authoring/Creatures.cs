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

            // The template's files carry its name -- SabreCat.hkx, SabreCatBehavior.hkx -- and a
            // copy that keeps them reads as the sabre cat's everywhere a path is shown: the race's
            // graph, the Creation Kit's idle list, the cache. They are renamed after the new
            // creature, and the two places one file names another are rewritten below.
            var named = new[] { Path.GetFileName(relativeFolder.TrimEnd('/', '\\')), race.EditorID?.Replace("Race", "") ?? "" }
                .Where(n => n.Length > 2).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length).ToList();

            string Renamed(string stored)
            {
                // A path out of the folder is another creature's file, shared, not copied.
                if (stored.Replace('/', '\\').StartsWith(@"..\", StringComparison.Ordinal)) return stored;
                // Only the file's own name, and the folders are the game's own separator, which
                // is not this platform's: the paths are split here rather than by Path.
                int slash = stored.LastIndexOfAny(['\\', '/']);
                string name = stored[(slash + 1)..];
                foreach (string spelling in named)
                {
                    int at = name.IndexOf(spelling, StringComparison.OrdinalIgnoreCase);
                    if (at < 0) continue;
                    return stored[..(slash + 1)] + name[..at] + id + name[(at + spelling.Length)..];
                }

                return stored;
            }

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
                Copy(path, Renamed(stored));
            }

            // Everything downstream -- the rig rebind, the state constants, the cache block --
            // reads the project's file list, and the list is now the copies' names.
            var storedFiles = entry.Block.Files.Select(Renamed).ToList();

            // The project names its character file and the character its behaviour, both by the
            // names they had. Rewritten in the copies, which are the files the game will read.
            if (named.Count > 0)
            {
                ProjectFile copiedProject = ProjectFile.Load(Path.Combine(target, project + ".hkx"));
                for (int i = 0; i < copiedProject.CharacterFiles.Count; i++)
                    copiedProject.CharacterFiles[i] = Renamed(copiedProject.CharacterFiles[i]);
                copiedProject.File.Save(copiedProject.File.Path);

                foreach (string named_ in copiedProject.CharacterFiles)
                {
                    if (HavokPath.Resolve(target, named_) is not { } path) continue;
                    CharacterFile copiedCharacter = CharacterFile.Load(path);
                    if (copiedCharacter.Data.m_stringData is not { } strings) continue;
                    strings.m_behaviorFilename = Renamed(strings.m_behaviorFilename);
                    copiedCharacter.File!.Save(path);
                }
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
            string? templateSkeletonNif = null;
            bool rigReplaced = false;
            NifModel? skeletonNif = null;
            SkeletonFile? skeletonHavok = null;
            var bodyNifs = new List<(string Nif, string Fbx)>();
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
                    templateSkeletonNif = path;
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
                    skeletonNif = mesh;
                    foreach (MeshFinding finding in MeshRules.Check(nameof(Race), Content.Nif.NifProfileReader.Read(mesh)))
                        findings.Add((newSkeletonModel, finding));
                }

                if (character is not null && !string.IsNullOrEmpty(character.RigName))
                {
                    string rig = Path.Combine(target, character.RigName.Replace('\\', Path.DirectorySeparatorChar));
                    SkeletonFile imported = SkeletonExchange.ImportHavok(document);
                    skeletonHavok = imported;
                    SkeletonFile template = HkxSkeletonFile.Read(rig);

                    if (SameStructure(template, imported))
                        HkxSkeletonFile.Write(rig, imported, rig);
                    else
                    {
                        // A rig of its own: the file is rebuilt around it, and every bone the
                        // copied character and behaviours name by index is found again by name.
                        HkxSkeletonBuilder.Write(rig, imported, rig);

                        var copied = storedFiles
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

                var renamed = RenameStateConstants(target, storedFiles, own, id);
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
                    // The addons of the template's own race, and no others. A skin's addons are
                    // one per race it dresses -- the sabre cat's two are the sabre cat's body
                    // and the snowy sabre cat's -- and re-racing all of them to the new race
                    // leaves two addons covering one slot for one race at the same priority,
                    // which the Creation Kit reports and the game resolves by picking one.
                    var variants = AddonsOf(skin.FormKey.ToString());
                    var mine = variants
                        .Where(a => a.Races.Contains(race.EditorID ?? "", StringComparer.OrdinalIgnoreCase))
                        .ToList();
                    if (mine.Count == 0) mine = [.. variants.Take(1)];

                    var only = mine.Count > 0
                        ? mine.ToDictionary(a => a.EditorId, _ => (IReadOnlyDictionary<ModelSlot, string>)body)
                        : null;

                    if (variants.Count > mine.Count)
                        notes.Add($"the template's skin has {variants.Count} addons, one per race it dresses; "
                            + $"{string.Join(", ", mine.Select(a => a.EditorId))} dresses '{race.EditorID}' and the rest are left out");

                    ImportResult worn = Import(new AssetImport
                    {
                        Kind = AuthoredKind.Armor, Template = skin.FormKey.ToString(), EditorId = id + "Skin", Prefix = "",
                        Fbx = body, MeshFolder = folder.Replace('/', '\\'), Recipes = false,
                        Addons = only, DropUnlistedAddons = variants.Count > mine.Count,
                    });
                    records.AddRange(worn.Records);
                    files.AddRange(worn.Meshes.Concat(worn.Textures));
                    if (body.TryGetValue(ModelSlot.Main, out string? mainFbx))
                        foreach (string nif in worn.Meshes)
                            bodyNifs.Add((Path.Combine(OutputFolder, nif.Replace('\\', Path.DirectorySeparatorChar)), mainFbx));
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

            // ------------------------------------------------ do the files agree

            // Each file came from its own converter's reading of the FBX; what they mean in the
            // world is compared, since each is correct on its own (CreatureChecks).
            if (skeletonNif is not null && skeletonHavok is not null)
            {
                // A skeleton rebuilt from an FBX has the bones and none of what the game reads
                // off the root beside them, which the template has and can lend.
                if (templateSkeletonNif is not null)
                {
                    var db = NifXmlDatabase.LoadEmbedded();
                    var worn = bodyNifs.Where(b => File.Exists(b.Nif) && b.Nif.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                        .Select(b => NifModel.Load(b.Nif, db)).ToList();
                    foreach (string note in SkeletonExtras.Carry(skeletonNif, NifModel.Load(templateSkeletonNif, db), request.BoneMap, worn))
                        notes.Add($"skeleton {note}");
                    skeletonNif.Save(Path.Combine(meshes, newSkeletonModel!.Replace('\\', Path.DirectorySeparatorChar)));
                }

                foreach (MeshFinding finding in CreatureChecks.Skeleton(skeletonNif, skeletonHavok))
                    findings.Add((newSkeletonModel!, finding));
                foreach (MeshFinding finding in CreatureChecks.BlockSizes(NifModel.Load(Path.Combine(meshes, newSkeletonModel!.Replace('\\', Path.DirectorySeparatorChar)), NifXmlDatabase.LoadEmbedded())))
                    findings.Add((newSkeletonModel!, finding));
            }
            foreach (var (nif, fbx) in bodyNifs.Where(b => File.Exists(b.Nif) && b.Nif.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)))
            {
                NifModel worn = NifModel.Load(nif, NifXmlDatabase.LoadEmbedded());
                string relative = Path.GetRelativePath(OutputFolder, nif);
                if (skeletonNif is not null)
                    foreach (MeshFinding finding in CreatureChecks.Skin(worn, skeletonNif)) findings.Add((relative, finding));
                foreach (MeshFinding finding in CreatureChecks.Triangles(worn, fbx)) findings.Add((relative, finding));
                foreach (MeshFinding finding in CreatureChecks.Weights(worn)) findings.Add((relative, finding));
                foreach (MeshFinding finding in CreatureChecks.WornSlots(worn)) findings.Add((relative, finding));
                foreach (MeshFinding finding in CreatureChecks.BlockSizes(worn)) findings.Add((relative, finding));
            }

            // ------------------------------------------------ idle records

            CopyIdles(relativeFolder, folder, race, copy, id, Renamed, records, notes);

            if (request.Npc is not null)
            {
                INpcGetter npc = Resolve<INpcGetter>(request.Npc, nameof(request));
                var made = Plugin.Npcs.DuplicateInAsNewRecord<Npc, INpcGetter>(npc, id + "Npc");
                made.Race.SetTo(copy);

                // An actor takes its attacks from its attack race where it names one, and the
                // template's names the template: the creature would swing by the sabre cat's
                // reach and damage and ignore its own.
                if (made.AttackRace.FormKey == race.FormKey) made.AttackRace.SetTo(copy);

                if (!made.WornArmor.IsNull) made.WornArmor.SetTo(copy.Skin.FormKey);
                if (request.DisplayName is not null) made.Name = request.DisplayName;
                records.Add(new AuthoredRecord(made.FormKey, nameof(Npc), made.EditorID!, npc.FormKey));
            }

            // ------------------------------------------------ the caches

            SkyrimCache caches = Caches(request.SourceMeshes, meshes, files);
            if (caches.AnimationData.Project(project) is null)
            {
                HKSK.Cache.ProjectBlock block = entry.Block.Clone();
                block.Files = [.. storedFiles];
                caches.AnimationData.Projects.Add(new AnimationDataProject
                {
                    Name = project + ".txt",
                    Block = block,
                    Movements = entry.Movements?.Clone() ?? new ProjectDataBlock(),
                });
            }

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
            Func<string, string> renamed, List<AuthoredRecord> records, List<string> notes)
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

            // The file an idle plays under: its own, or the nearest ancestor that names one.
            string? FileOf(IIdleAnimationGetter idle)
            {
                var seen = new HashSet<FormKey>();
                for (IIdleAnimationGetter? at = idle; at is not null && seen.Add(at.FormKey);)
                {
                    if (Names(at)) return at.Filename!.GivenPath;
                    at = at.RelatedIdles.Count > 0 && _cache.TryResolve<IIdleAnimationGetter>(at.RelatedIdles[0].FormKey, out var up) ? up : null;
                }
                return null;
            }

            // A record in the template's tree that names another creature's behaviour. The masters
            // have a few, and one of them matters here: SabreCatNoSpeed, the parent of every turn
            // in place the sabre cat has, names the skeever's file. The rule above passes it over
            // as another creature's, which would leave the copies below pointing back into the
            // template's tree; it is taken along instead and given the file its branch plays under.
            var misfiled = new Dictionary<FormKey, string>();
            var reached = originals.ToDictionary(i => i.FormKey);
            var pending = new Queue<IIdleAnimationGetter>(originals);
            while (pending.Count > 0)
            {
                foreach (IFormLinkGetter<IIdleRelationGetter> link in pending.Dequeue().RelatedIdles)
                {
                    if (link.IsNull || reached.ContainsKey(link.FormKey)) continue;
                    if (!_cache.TryResolve<IIdleAnimationGetter>(link.FormKey, out var other)) continue;
                    // No file of its own is a case Serves already judged, by the parent chain.
                    if (other.Filename?.GivenPath is not { Length: > 0 } || Names(other)) continue;

                    var up = new HashSet<FormKey>();
                    IIdleAnimationGetter? at = other;
                    while (at is not null && up.Add(at.FormKey) && !reached.ContainsKey(at.FormKey))
                        at = at.RelatedIdles.Count > 0 && _cache.TryResolve<IIdleAnimationGetter>(at.RelatedIdles[0].FormKey, out var parent) ? parent : null;
                    if (at is null || !reached.ContainsKey(at.FormKey) || FileOf(at) is not { } file) continue;

                    misfiled[other.FormKey] = file;
                    reached.Add(other.FormKey, other);
                    originals.Add(other);
                    pending.Enqueue(other);
                }
            }
            foreach ((FormKey key, string file) in misfiled)
                notes.Add($"idle {reached[key].EditorID} names '{reached[key].Filename!.GivenPath}', which is not the template's; the copy plays under '{file}'");

            // What the template calls itself, so a copy can be named after the new creature
            // rather than prefixed: SabreCatDeath becomes HouseCatDeath. Its records rarely spell
            // it out -- the sabre cat's idles are SCatRecoil, ScatReset, CatIdleWarn -- so the
            // short forms are taken along, or a copy reads HouseCatSCatRecoil. se-cmd's retarget
            // keeps a table of those spellings by hand; here they are made from the name: the
            // whole of it, its last word, and the leading words' initials before that word.
            static IEnumerable<string> Forms(string name)
            {
                var words = System.Text.RegularExpressions.Regex.Matches(name, "[A-Z][a-z0-9]*|[a-z0-9]+")
                    .Select(m => m.Value).Where(w => w.Length > 0).ToList();
                yield return name;
                if (words.Count < 2) yield break;
                yield return string.Concat(words[..^1].Select(w => w[0])) + words[^1];
                yield return words[^1];
            }

            var spellings = new[] { Path.GetFileName(templateFolder.TrimEnd('/', '\\')), templateRace.EditorID?.Replace("Race", "") ?? "" }
                .Where(n => n.Length > 2).SelectMany(Forms).Where(n => n.Length > 2)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length).ToList();
            var taken = new HashSet<string>(
                _cache.PriorityOrder.WinningOverrides<IIdleAnimationGetter>().Select(i => i.EditorID ?? ""),
                StringComparer.OrdinalIgnoreCase);

            string Rename(string editorId)
            {
                foreach (string spelling in spellings)
                {
                    // A short form is a word of the name and matches where a name would stand,
                    // at the front: 'Cat' inside 'Duplicate' is not the creature.
                    int at = editorId.IndexOf(spelling, StringComparison.OrdinalIgnoreCase);
                    if (at < 0 || (at > 0 && spelling.Length < 5)) continue;
                    string renamed = editorId[..at] + id + editorId[(at + spelling.Length)..];
                    if (taken.Add(renamed)) return renamed;
                }

                // The masters misspell a creature's name in an editor id now and then, and the
                // sabre cat's swim idle is SabreCastStartSwimming. A name off by one letter is
                // still the name when what follows it starts a word of its own.
                foreach (string spelling in spellings)
                    for (int length = spelling.Length + 1; length >= spelling.Length - 1; length--)
                    {
                        if (length < 3 || length > editorId.Length) continue;
                        string rest = editorId[length..];
                        if (rest.Length > 0 && !char.IsUpper(rest[0])) continue;
                        if (!OneApart(editorId[..length], spelling)) continue;
                        string near = id + rest;
                        if (taken.Add(near)) return near;
                    }

                string prefixed = id + editorId;
                for (int n = 2; !taken.Add(prefixed); n++) prefixed = id + editorId + n;
                return prefixed;
            }

            var copies = new Dictionary<FormKey, IdleAnimation>();
            foreach (IIdleAnimationGetter idle in originals)
            {
                string editorId = Rename(idle.EditorID ?? idle.FormKey.ID.ToString("X6"));
                IdleAnimation made = Plugin.IdleAnimations.DuplicateInAsNewRecord<IdleAnimation, IIdleAnimationGetter>(idle, editorId);

                if (Names(idle) || misfiled.ContainsKey(idle.FormKey))
                {
                    string given = misfiled.TryGetValue(idle.FormKey, out string? corrected) ? corrected : idle.Filename!.GivenPath;
                    char separator = given.Contains('/') ? '/' : '\\';
                    // The file it names was copied under the new creature's name too.
                    string rest = renamed(Normal(given)[from.Length..]);
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

        /// <summary>Whether two names differ by at most one letter inserted, dropped or changed.</summary>
        private static bool OneApart(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            if (Math.Abs(a.Length - b.Length) > 1) return false;
            if (a.Length < b.Length) (a, b) = (b, a);

            for (int i = 0, j = 0, spent = 0; i < a.Length;)
            {
                if (j < b.Length && char.ToUpperInvariant(a[i]) == char.ToUpperInvariant(b[j])) { i++; j++; continue; }
                if (++spent > 1) return false;
                if (a.Length == b.Length) { i++; j++; } else i++;
            }

            return true;
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
