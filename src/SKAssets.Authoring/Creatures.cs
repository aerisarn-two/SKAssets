using HKFBX.Hkx;
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

        /// <summary>FBX meshes for the body the creature's skin wears. Without them, the template's.</summary>
        public IReadOnlyDictionary<ModelSlot, string>? Body { get; init; }

        /// <summary>
        /// FBX files of clips, each stack named for the animation it replaces or adds, as
        /// <c>SKAssets.Export</c>'s clip exchange reads them. Converting needs Havok's codec.
        /// </summary>
        public IReadOnlyList<string> Animations { get; init; } = [];

        /// <summary>An NPC to copy as the creature's first, by editor id: <c>EncWolf</c>.</summary>
        public string? Npc { get; init; }

        /// <summary>Put before every name written; the plugin's prefix when null.</summary>
        public string? Prefix { get; init; }
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
                    HkxSkeletonFile.Write(rig, SkeletonExchange.ImportHavok(document), rig);
                }
            }

            // ------------------------------------------------ the records

            Race copy = Plugin.Races.DuplicateInAsNewRecord<Race, IRaceGetter>(race, id + "Race");
            records.Add(new AuthoredRecord(copy.FormKey, nameof(Race), copy.EditorID!, race.FormKey));
            if (request.DisplayName is not null) copy.Name = request.DisplayName;

            string behavior = Path.Combine(folder, project + ".hkx").Replace('/', '\\');
            copy.BehaviorGraph = new GenderedItem<Model?>(new Model { File = behavior }, new Model { File = behavior });
            if (newSkeletonModel is not null)
                copy.SkeletalModel = new GenderedItem<SimpleModel?>(new SimpleModel { File = newSkeletonModel }, new SimpleModel { File = newSkeletonModel });

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
            }

            if (request.Skeleton is not null && newSkeletonModel is not null
                && !race.BodyPartData.IsNull && _cache.TryResolve<IBodyPartDataGetter>(race.BodyPartData.FormKey, out var parts))
            {
                var bptd = Plugin.BodyParts.DuplicateInAsNewRecord<BodyPartData, IBodyPartDataGetter>(parts, id + "BodyPartData");
                bptd.Model = new Model { File = newSkeletonModel };
                copy.BodyPartData.SetTo(bptd);
                records.Add(new AuthoredRecord(bptd.FormKey, nameof(BodyPartData), bptd.EditorID!, parts.FormKey));
            }

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
            }

            var gameRecords = GameRecordReader.Read([.. _cache.ListedOrder.OfType<ISkyrimModGetter>(), Plugin]);
            CacheAmendment amended = CacheGeneration.Amend(caches, project, gameRecords);
            caches.Save();
            _caches = caches;

            return new CreatureResult(records[0], records, project, [.. files.Distinct()], amended, clips, findings, notes);
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
