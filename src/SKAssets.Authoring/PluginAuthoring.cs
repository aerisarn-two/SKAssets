using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using SKAssets.Content.Assets;
using SKAssets.Content.Nif;
using SKAssets.Plugins;

namespace SKAssets.Authoring
{
    /// <summary>
    /// A new plugin against a load order, filled by importing FBX meshes as copies of vanilla
    /// records.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mesh is not usable until a record names it, and a record is not usable until the
    /// records it depends on are there. So an import is always a copy of a similar record the
    /// load order already has -- the template -- and of what the template <em>owns</em>: the
    /// records nothing but it refers to, which have to be copied to take the new mesh. What it
    /// shares -- keywords, sounds, equip slots, impact sets, materials, enchantments -- the copy
    /// refers to as the template does. Which is which was measured over the five masters and is
    /// <c>docs/authoring.md</c>:
    /// </para>
    /// <list type="bullet">
    /// <item>an armour owns its addons, which carry its body meshes;</item>
    /// <item>a weapon owns its first-person static;</item>
    /// <item>ammunition, when it is given a mesh to fly as, its projectile;</item>
    /// <item>a book or a scroll, when given one, its inventory static;</item>
    /// <item>and the recipes that create the template -- 253 weapons, 339 armours, 20 kinds of
    /// ammunition and 61 potions are made at a bench -- are copied to create the new record.</item>
    /// </list>
    /// <para>
    /// Nothing is placed in the world. The records are new, never overrides, so the masters are
    /// left as they are.
    /// </para>
    /// </remarks>
    public sealed partial class PluginAuthoring : IDisposable
    {
        private readonly List<ISkyrimModDisposableGetter> _opened = [];
        private readonly IReadOnlyList<ModKey> _loadOrder;
        private readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _cache;
        private readonly IMeshImporter _meshes;

        /// <summary>A new plugin against plugins already open, in load order.</summary>
        /// <param name="loadOrder">The masters and plugins the new one is written against.</param>
        /// <param name="plugin">The new plugin's name: <c>MyMod.esp</c>.</param>
        /// <param name="outputFolder">Where the plugin and its <c>Meshes</c> are written: a Data folder of their own, not the game's.</param>
        /// <param name="meshes">How an FBX becomes a NIF; NIFBX's conversion by default.</param>
        public PluginAuthoring(IEnumerable<ISkyrimModGetter> loadOrder, string plugin, string outputFolder, IMeshImporter? meshes = null)
        {
            ArgumentNullException.ThrowIfNull(loadOrder);
            ArgumentException.ThrowIfNullOrWhiteSpace(plugin);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);

            var order = loadOrder.ToList();
            _loadOrder = [.. order.Select(m => m.ModKey)];
            _cache = order.ToImmutableLinkCache();
            _meshes = meshes ?? new FbxMeshImporter();

            OutputFolder = Path.GetFullPath(outputFolder);
            Plugin = new SkyrimMod(ModKey.FromFileName(plugin), SkyrimRelease.SkyrimSE);
        }

        /// <summary>A new plugin against the game's masters in a Data folder.</summary>
        /// <param name="dataFolder">The game's Data folder, read and never written.</param>
        /// <param name="plugin">The new plugin's name.</param>
        /// <param name="outputFolder">Where the plugin and its meshes are written.</param>
        /// <param name="plugins">Plugins to read after the masters, by name in <paramref name="dataFolder"/> or by path.</param>
        public static PluginAuthoring Open(string dataFolder, string plugin, string outputFolder, IEnumerable<string>? plugins = null)
        {
            MutagenRuntime.Prepare();

            var opened = new List<ISkyrimModDisposableGetter>();
            foreach (string file in Content.Havok.GameRecordReader.Masters.Concat(plugins ?? []))
            {
                string path = File.Exists(file) ? file : Path.Combine(dataFolder, file);
                if (File.Exists(path)) opened.Add(SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE));
            }

            var authoring = new PluginAuthoring(opened, plugin, outputFolder);
            authoring._opened.AddRange(opened);
            return authoring;
        }

        /// <summary>The plugin being written.</summary>
        public SkyrimMod Plugin { get; }

        /// <summary>Where the plugin and its meshes go.</summary>
        public string OutputFolder { get; }

        /// <summary>
        /// The prefix every import's editor ids and mesh names take unless it gives its own
        /// (<see cref="AssetImport.Prefix"/>): <c>MyMod_</c>, so nothing written collides with the
        /// masters' names or another plugin's.
        /// </summary>
        public string Prefix { get; set; } = "";

        /// <summary>Imports one asset: its meshes, its record, what the record owns, and its recipes.</summary>
        /// <exception cref="ArgumentException">The template is not in the load order, or not of the kind asked for.</exception>
        public ImportResult Import(AssetImport request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!request.Fbx.ContainsKey(ModelSlot.Main))
                throw new ArgumentException("an import needs a Main mesh", nameof(request));

            var run = new Run(this, request);
            AuthoredRecord record = request.Kind switch
            {
                AuthoredKind.Static => run.Modeled<Static, IStaticGetter>(Plugin.Statics),
                AuthoredKind.MoveableStatic => run.Modeled<MoveableStatic, IMoveableStaticGetter>(Plugin.MoveableStatics),
                AuthoredKind.Furniture => run.Modeled<Furniture, IFurnitureGetter>(Plugin.Furniture),
                AuthoredKind.Door => run.Modeled<Door, IDoorGetter>(Plugin.Doors),
                AuthoredKind.Activator => run.Modeled<Mutagen.Bethesda.Skyrim.Activator, IActivatorGetter>(Plugin.Activators),
                AuthoredKind.Container => run.Modeled<Container, IContainerGetter>(Plugin.Containers),
                AuthoredKind.Flora => run.Modeled<Flora, IFloraGetter>(Plugin.Florae),
                AuthoredKind.Tree => run.Modeled<Tree, ITreeGetter>(Plugin.Trees),
                AuthoredKind.Light => run.Modeled<Light, ILightGetter>(Plugin.Lights),
                AuthoredKind.MiscItem => run.Modeled<MiscItem, IMiscItemGetter>(Plugin.MiscItems),
                AuthoredKind.Key => run.Modeled<Key, IKeyGetter>(Plugin.Keys),
                AuthoredKind.SoulGem => run.Modeled<SoulGem, ISoulGemGetter>(Plugin.SoulGems),
                AuthoredKind.Ingestible => run.Modeled<Ingestible, IIngestibleGetter>(Plugin.Ingestibles),
                AuthoredKind.Ingredient => run.Modeled<Ingredient, IIngredientGetter>(Plugin.Ingredients),
                AuthoredKind.ArtObject => run.Modeled<ArtObject, IArtObjectGetter>(Plugin.ArtObjects),
                AuthoredKind.Book => run.Book(),
                AuthoredKind.Scroll => run.Scroll(),
                AuthoredKind.Ammunition => run.Ammunition(),
                AuthoredKind.Weapon => run.Weapon(),
                AuthoredKind.Armor => run.Armor(),
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "not an authored kind"),
            };

            if (request.Recipes) run.Recipes();
            return run.Result(record);
        }

        /// <summary>The addons an armour wears -- traced to its base when it is a variant -- for <see cref="AssetImport.Addons"/>.</summary>
        public IReadOnlyList<ArmorAddonInfo> AddonsOf(string armor)
        {
            IArmorGetter found = (FormKey.TryFactory(armor, out FormKey key)
                    ? _cache.TryResolve<IArmorGetter>(key, out var byKey) ? byKey : null
                    : _cache.TryResolve<IArmorGetter>(armor, out var byId) ? byId : null)
                ?? throw new ArgumentException($"the load order has no Armor '{armor}'", nameof(armor));

            var seen = new HashSet<FormKey>();
            while (seen.Add(found.FormKey) && !found.TemplateArmor.IsNull && _cache.TryResolve<IArmorGetter>(found.TemplateArmor.FormKey, out var next))
                found = next;

            string Race(FormKey race) => _cache.TryResolve<IRaceGetter>(race, out var r) ? r.EditorID ?? race.ToString() : race.ToString();

            return [.. found.Armature
                .Select(l => _cache.TryResolve<IArmorAddonGetter>(l.FormKey, out var a) ? a : null)
                .OfType<IArmorAddonGetter>()
                .Select(a => new ArmorAddonInfo(
                    a.EditorID ?? a.FormKey.ToString(),
                    [.. new[] { a.Race.FormKey }.Where(r => !r.IsNull).Concat(a.AdditionalRaces.Select(r => r.FormKey)).Select(Race)],
                    a.WorldModel?.Male?.File.GivenPath,
                    a.WorldModel?.Female?.File.GivenPath))];
        }

        /// <summary>Writes the plugin to the output folder; the meshes were written as they were imported.</summary>
        /// <returns>The plugin's path.</returns>
        public string Save()
        {
            Directory.CreateDirectory(OutputFolder);
            string path = Path.Combine(OutputFolder, Plugin.ModKey.FileName);

            Plugin.WriteToBinary(path, new BinaryWriteParameters
            {
                MastersListOrdering = new MastersListOrderingByLoadOrder(_loadOrder),
            });
            return path;
        }

        public void Dispose()
        {
            foreach (var mod in _opened) mod.Dispose();
            _opened.Clear();
        }

        /// <summary>One import's state: the template, what it wrote, what it noted.</summary>
        private sealed class Run(PluginAuthoring owner, AssetImport request)
        {
            /// <summary>The asset's editor id with its prefix, which every name written starts from.</summary>
            private readonly string _id = (request.Prefix ?? owner.Prefix) + request.EditorId;

            /// <summary>The FBX slots being written, and the name their meshes take: the request's, or one addon's.</summary>
            private IReadOnlyDictionary<ModelSlot, string> _fbx = request.Fbx;
            private string _stem = (request.Prefix ?? owner.Prefix) + request.EditorId;

            /// <summary>The slot the mesh being converted is worn in, while an addon is dressed.</summary>
            private int? _bodyPart;
            private readonly List<AuthoredRecord> _records = [];
            private readonly List<string> _meshes = [];
            private readonly List<string> _textures = [];
            private readonly List<(string, MeshFinding)> _findings = [];
            private readonly List<string> _notes = [];
            private readonly Dictionary<string, string> _converted = new(StringComparer.OrdinalIgnoreCase);
            private IMajorRecordGetter? _template;
            private IMajorRecordGetter? _created;

            public ImportResult Result(AuthoredRecord record) =>
                new(record, _records, _meshes, _textures, _findings, _notes);

            // ------------------------------------------------------------ the kinds

            /// <summary>A record with one model: copied, and given the mesh.</summary>
            public AuthoredRecord Modeled<TMajor, TGetter>(IGroup<TMajor> group)
                where TMajor : SkyrimMajorRecord, TGetter, IModeled
                where TGetter : class, ISkyrimMajorRecordGetter
            {
                TMajor copy = Copy<TMajor, TGetter>(group, Template<TGetter>(), _id);
                copy.Model = Model(ModelSlot.Main, typeof(TMajor).Name, "");
                Rename(copy);
                return Created(copy);
            }

            public AuthoredRecord Weapon()
            {
                IWeaponGetter template = Base(Template<IWeaponGetter>(), w => w.Template, "weapon");
                Weapon copy = Copy<Weapon, IWeaponGetter>(owner.Plugin.Weapons, template, _id);
                copy.Model = Model(ModelSlot.Main, nameof(Weapon), "");
                Rename(copy);

                // The first-person model is a static of its own; 120 of the 368 base weapons
                // point it at the ground model's file, so one mesh is a whole weapon.
                ModelSlot first = request.Fbx.ContainsKey(ModelSlot.FirstPerson) ? ModelSlot.FirstPerson : ModelSlot.Main;
                if (first == ModelSlot.Main) _notes.Add("no first-person mesh: the first-person static uses the main mesh, as 120 of vanilla's 368 base weapons do");

                var stat = owner.Plugin.Statics.AddNew(_id + "1stPerson");
                if (!template.FirstPersonModel.IsNull && owner._cache.TryResolve<IStaticGetter>(template.FirstPersonModel.FormKey, out var shown))
                {
                    stat.DeepCopyIn(shown);
                    stat.EditorID = _id + "1stPerson";
                    Owned(stat, shown.FormKey);
                }
                else Owned(stat, FormKey.Null);

                stat.Model = Model(first, nameof(Static), first == ModelSlot.Main ? "" : "_1stperson");
                copy.FirstPersonModel.SetTo(stat);
                return Created(copy);
            }

            public AuthoredRecord Armor()
            {
                IArmorGetter template = Base(Template<IArmorGetter>(), a => a.TemplateArmor, "armour");
                Armor copy = Copy<Armor, IArmorGetter>(owner.Plugin.Armors, template, _id);
                Rename(copy);

                // On the ground: the template's ground model is another armour's, so it is
                // replaced when a mesh is given and kept, with a note, when not.
                if (request.Fbx.ContainsKey(ModelSlot.Ground) || request.Fbx.ContainsKey(ModelSlot.FemaleGround))
                {
                    Model? male = request.Fbx.ContainsKey(ModelSlot.Ground) ? Model(ModelSlot.Ground, nameof(Armor), "_go") : null;
                    Model? female = request.Fbx.ContainsKey(ModelSlot.FemaleGround) ? Model(ModelSlot.FemaleGround, nameof(Armor), "_f_go") : male;
                    copy.WorldModel = new GenderedItem<ArmorModel?>(
                        male is null ? null : new ArmorModel { Model = male },
                        female is null ? null : new ArmorModel { Model = female });
                }
                else if (template.WorldModel is not null) _notes.Add("no ground mesh: the armour lies on the ground as the template does");

                // Every addon the template wears is copied and given the new body meshes. A
                // template with several -- one per race family or a separate piece, as 244 of
                // vanilla's 1,055 base armours have -- can be given meshes addon by addon
                // (AssetImport.Addons); an addon not named wears the import's own, or is left out.
                var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var worn = new List<IFormLinkGetter<IArmorAddonGetter>>();

                // An addon's own meshes are numbered so they cannot overwrite the import's or
                // each other's. One addon wearing everything there is to wear has nothing to be
                // told apart from, and numbering it would leave a creature in 'CatSkin_0.nif',
                // which reads like half a weight slider.
                int dressed = request.Addons is null ? 0 : template.Armature
                    .Select(link => owner._cache.TryResolve<IArmorAddonGetter>(link.FormKey, out var a) ? a.EditorID : null)
                    .Count(id => id is not null && request.Addons.ContainsKey(id));
                bool alone = dressed == 1 && (request.DropUnlistedAddons || template.Armature.Count == 1);

                for (int i = 0; i < template.Armature.Count; i++)
                {
                    if (!owner._cache.TryResolve<IArmorAddonGetter>(template.Armature[i].FormKey, out var addon)) continue;

                    IReadOnlyDictionary<ModelSlot, string>? own =
                        addon.EditorID is { } key && request.Addons?.GetValueOrDefault(key) is { } slots ? slots : null;
                    if (own is not null) named.Add(addon.EditorID!);
                    else if (request.DropUnlistedAddons) { _notes.Add($"'{addon.EditorID}' is not in the import's addons and is left out"); continue; }

                    string id = template.Armature.Count == 1 ? _id + "AA" : $"{_id}AA{i}";
                    var aa = owner.Plugin.ArmorAddons.DuplicateInAsNewRecord<ArmorAddon, IArmorAddonGetter>(addon, id);
                    Owned(aa, addon.FormKey);

                    // An addon with meshes of its own names them after itself, so they do not
                    // overwrite the import's.
                    (_fbx, _stem) = own is null ? (request.Fbx, _id)
                        : alone ? (Merged(own), _id)
                        : (Merged(own), $"{_id}_{i}");
                    Dress(aa, addon);
                    (_fbx, _stem) = (request.Fbx, _id);

                    worn.Add(aa.ToLink<IArmorAddonGetter>());
                }

                if (request.Addons?.Keys.FirstOrDefault(k => !named.Contains(k)) is { } stray)
                    throw new ArgumentException($"'{template.EditorID}' wears no addon '{stray}'", nameof(request));
                if (template.Armature.Count > 1 && named.Count == 0)
                    _notes.Add($"the template has {template.Armature.Count} addons; each copy wears the same meshes");

                copy.Armature.Clear();
                copy.Armature.AddRange(worn);

                return Created(copy);
            }

            public AuthoredRecord Ammunition()
            {
                IAmmunitionGetter template = Template<IAmmunitionGetter>();
                Ammunition copy = Copy<Ammunition, IAmmunitionGetter>(owner.Plugin.Ammunitions, template, _id);
                copy.Model = Model(ModelSlot.Main, nameof(Ammunition), "");
                Rename(copy);

                // The projectile is shared by 30 of the 54 kinds of ammunition, and is copied only
                // to fly as a mesh of its own.
                if (request.Fbx.ContainsKey(ModelSlot.Projectile) && owner._cache.TryResolve<IProjectileGetter>(template.Projectile.FormKey, out var flies))
                {
                    var proj = owner.Plugin.Projectiles.DuplicateInAsNewRecord<Projectile, IProjectileGetter>(flies, _id + "Projectile");
                    Owned(proj, flies.FormKey);
                    proj.Model = Model(ModelSlot.Projectile, nameof(Projectile), "_projectile");
                    copy.Projectile.SetTo(proj);
                }
                else _notes.Add("no projectile mesh: the ammunition flies as the template's does");

                return Created(copy);
            }

            public AuthoredRecord Book()
            {
                Book copy = Copy<Book, IBookGetter>(owner.Plugin.Books, Template<IBookGetter>(), _id);
                copy.Model = Model(ModelSlot.Main, nameof(Book), "");
                Rename(copy);

                if (Shown(copy.InventoryArt.FormKey, "Inventory") is { } art) copy.InventoryArt.SetTo(art);
                return Created(copy);
            }

            public AuthoredRecord Scroll()
            {
                Scroll copy = Copy<Scroll, IScrollGetter>(owner.Plugin.Scrolls, Template<IScrollGetter>(), _id);
                copy.Model = Model(ModelSlot.Main, nameof(Scroll), "");
                Rename(copy);

                if (Shown(copy.MenuDisplayObject.FormKey, "Inventory") is { } art) copy.MenuDisplayObject.SetTo(art);
                return Created(copy);
            }

            /// <summary>
            /// Copies what makes the template -- a crafting recipe, a tempering one -- to make the
            /// new record instead, with the same bench, components and conditions.
            /// </summary>
            public void Recipes()
            {
                if (_template is null || _created is null) return;

                foreach (var recipe in owner._cache.PriorityOrder.WinningOverrides<IConstructibleObjectGetter>())
                {
                    if (recipe.CreatedObject.FormKey != _template.FormKey) continue;

                    string id = (recipe.EditorID ?? "Recipe").Replace(_template.EditorID ?? "\u0000", _id, StringComparison.OrdinalIgnoreCase);
                    if (id == recipe.EditorID) id = "Recipe" + _id;

                    var copy = owner.Plugin.ConstructibleObjects.DuplicateInAsNewRecord<ConstructibleObject, IConstructibleObjectGetter>(recipe, id);
                    copy.CreatedObject.SetTo(_created.FormKey);
                    Owned(copy, recipe.FormKey);
                }
            }

            // ------------------------------------------------------------ helpers

            private TGetter Template<TGetter>() where TGetter : class, IMajorRecordGetter
            {
                TGetter? found = FormKey.TryFactory(request.Template, out FormKey key)
                    ? owner._cache.TryResolve<TGetter>(key, out var byKey) ? byKey : null
                    : owner._cache.TryResolve<TGetter>(request.Template, out var byId) ? byId : null;

                return found ?? throw new ArgumentException(
                    $"the load order has no {typeof(TGetter).Name.TrimStart('I').Replace("Getter", "")} '{request.Template}'", nameof(request));
            }

            /// <summary>A variant's base: the enchanted and templated copies inherit their mesh from it.</summary>
            private TGetter Base<TGetter>(TGetter record, Func<TGetter, IFormLinkNullableGetter<TGetter>> templateOf, string what)
                where TGetter : class, IMajorRecordGetter
            {
                var seen = new HashSet<FormKey>();
                TGetter at = record;
                while (seen.Add(at.FormKey) && !templateOf(at).IsNull && owner._cache.TryResolve<TGetter>(templateOf(at).FormKey, out var next))
                    at = next;

                if (!ReferenceEquals(at, record))
                    _notes.Add($"'{record.EditorID}' is a variant; the {what} is copied from its base, '{at.EditorID}'");
                return at;
            }

            private TMajor Copy<TMajor, TGetter>(IGroup<TMajor> group, TGetter template, string editorId)
                where TMajor : SkyrimMajorRecord, TGetter
                where TGetter : class, IMajorRecordGetter
            {
                _template = template;
                TMajor copy = group.DuplicateInAsNewRecord<TMajor, TGetter>(template, editorId);
                _created = copy;
                return copy;
            }

            private void Rename(IMajorRecord copy)
            {
                if (request.Name is null) return;
                if (copy is Mutagen.Bethesda.Plugins.Aspects.ITranslatedNamed named) named.Name = request.Name;
                else _notes.Add($"a {copy.GetType().Name} has no name to give it");
            }

            private AuthoredRecord Created(IMajorRecordGetter record)
            {
                var made = new AuthoredRecord(record.FormKey, record.GetType().Name, record.EditorID ?? "", _template?.FormKey ?? FormKey.Null);
                _records.Insert(0, made);
                return made;
            }

            private void Owned(IMajorRecordGetter record, FormKey copiedFrom) =>
                _records.Add(new AuthoredRecord(record.FormKey, record.GetType().Name, record.EditorID ?? "", copiedFrom));

            /// <summary>A copied inventory static, given its own mesh -- or the shared one kept, when none is given.</summary>
            private Static? Shown(FormKey shared, string what)
            {
                if (!request.Fbx.ContainsKey(ModelSlot.Inventory))
                {
                    if (!shared.IsNull) _notes.Add($"no {what.ToLowerInvariant()} mesh: the template's shared static is kept");
                    return null;
                }

                var stat = owner.Plugin.Statics.AddNew(_id + what);
                if (!shared.IsNull && owner._cache.TryResolve<IStaticGetter>(shared, out var from))
                {
                    stat.DeepCopyIn(from);
                    stat.EditorID = _id + what;
                    Owned(stat, from.FormKey);
                }
                else Owned(stat, FormKey.Null);

                stat.Model = Model(ModelSlot.Inventory, nameof(Static), "_inventory");
                return stat;
            }

            /// <summary>An armour addon's body meshes, in the weights and sexes the template's come in.</summary>
            private void Dress(ArmorAddon aa, IArmorAddonGetter template)
            {
                // The slot the addon wears, which the mesh's partitions have to name. Skyrim's
                // slots are 30 to 61 and the flags are a bit each from 30, so the lowest bit set
                // is the slot; an addon wearing several is worn as the first of them.
                _bodyPart = aa.BodyTemplate?.FirstPersonFlags is { } worn && (uint)worn != 0
                    ? 30 + System.Numerics.BitOperations.TrailingZeroCount((uint)worn)
                    : null;

                bool weighted = template.WorldModel?.Male?.File.GivenPath.EndsWith("_1.nif", StringComparison.OrdinalIgnoreCase) == true;
                if (weighted && !_fbx.ContainsKey(ModelSlot.LightWeight))
                    _notes.Add("no light-weight mesh: both body weights are the main mesh");

                Model male = Weighted(ModelSlot.Main, "", weighted);
                Model? female = template.WorldModel?.Female is null ? null
                    : _fbx.ContainsKey(ModelSlot.Female) ? Weighted(ModelSlot.Female, "_f", weighted)
                    : male;
                if (female == male && template.WorldModel?.Female is not null) _notes.Add("no female mesh: the female body wears the male mesh");
                aa.WorldModel = new GenderedItem<Model?>(male, female);

                if (_fbx.ContainsKey(ModelSlot.FirstPerson) || _fbx.ContainsKey(ModelSlot.FemaleFirstPerson))
                {
                    Model? first = _fbx.ContainsKey(ModelSlot.FirstPerson) ? Weighted(ModelSlot.FirstPerson, "_1stperson", weighted) : null;
                    Model? firstFemale = _fbx.ContainsKey(ModelSlot.FemaleFirstPerson) ? Weighted(ModelSlot.FemaleFirstPerson, "_f_1stperson", weighted) : first;
                    aa.FirstPersonModel = new GenderedItem<Model?>(first, firstFemale);
                }
                else if (template.FirstPersonModel?.Male is not null) _notes.Add("no first-person mesh: the first-person view wears the template's");

                _bodyPart = null;
            }

            /// <summary>
            /// A body mesh written in both weights where the template has them: <c>_1</c> from the
            /// slot, and <c>_0</c> from <see cref="ModelSlot.LightWeight"/>, or the same mesh.
            /// </summary>
            private Model Weighted(ModelSlot slot, string suffix, bool weighted)
            {
                if (!weighted) return Model(slot, nameof(ArmorAddon), suffix);

                Model heavy = Model(slot, nameof(ArmorAddon), suffix + "_1");
                ModelSlot light = slot == ModelSlot.Main && _fbx.ContainsKey(ModelSlot.LightWeight) ? ModelSlot.LightWeight : slot;
                Model(light, nameof(ArmorAddon), suffix + "_0");
                return heavy;
            }

            /// <summary>An addon's own slots, with the import's filling any it leaves out.</summary>
            private IReadOnlyDictionary<ModelSlot, string> Merged(IReadOnlyDictionary<ModelSlot, string> own)
            {
                var merged = new Dictionary<ModelSlot, string>(request.Fbx);
                foreach ((ModelSlot slot, string fbx) in own) merged[slot] = fbx;
                return merged;
            }

            /// <summary>Converts a slot's FBX to a mesh named from the editor id, and a model naming it.</summary>
            private Model Model(ModelSlot slot, string recordType, string suffix)
            {
                string relative = Path.Combine(request.MeshFolder, _stem + suffix + ".nif").Replace('/', '\\');
                string target = Path.Combine(owner.OutputFolder, "Meshes", relative.Replace('\\', Path.DirectorySeparatorChar));

                if (!_converted.ContainsKey(relative))
                {
                    ImportedMesh mesh = owner._meshes.Import(new MeshTarget(
                        _fbx[slot], target, owner.OutputFolder,
                        request.TextureFolder ?? request.MeshFolder, request.Prefix ?? owner.Prefix, _bodyPart));
                    _converted[relative] = target;
                    _meshes.Add(Path.Combine("Meshes", relative).Replace('\\', '/'));

                    foreach (string texture in mesh.Textures)
                        if (!_textures.Contains(texture.Replace('\\', '/'))) _textures.Add(texture.Replace('\\', '/'));
                    foreach (MeshFinding finding in mesh.Findings.Concat(MeshRules.Check(recordType, mesh.Profile)))
                        _findings.Add((relative, finding));
                }

                return new Model { File = relative };
            }
        }
    }
}
