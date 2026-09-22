using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SKAssets.Authoring
{
    /// <summary>Where a placed reference goes, how it is turned, and how big it is.</summary>
    /// <param name="Position">In the cell's coordinates: game units.</param>
    /// <param name="Rotation">About X, Y and Z, in degrees, as the Creation Kit shows it.</param>
    /// <param name="Scale">1 for the mesh's own size.</param>
    public readonly record struct Placement(P3Float Position, P3Float Rotation = default, float Scale = 1f);

    public sealed partial class PluginAuthoring
    {
        /// <summary>The size of an exterior cell, in game units: 4096 on a side.</summary>
        public const float CellSize = 4096f;

        /// <summary>
        /// Places a reference to a record in an interior cell, named by editor id or form key.
        /// </summary>
        /// <remarks>
        /// The cell is overridden in the new plugin to hold the reference -- which is what the
        /// Creation Kit does too; the cell's own fields are carried over as the load order has
        /// them. An NPC is placed as an actor reference, anything else as an object.
        /// </remarks>
        /// <returns>The placed reference.</returns>
        public AuthoredRecord Place(FormKey baseRecord, string cell, Placement at)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cell);

            var context = FormKey.TryFactory(cell, out FormKey key)
                ? _cache.TryResolveContext<ICell, ICellGetter>(key, out var byKey) ? byKey : null
                : _cache.TryResolveContext<ICell, ICellGetter>(cell, out var byId) ? byId : null;

            return Place(baseRecord, context ?? throw new ArgumentException($"the load order has no cell '{cell}'", nameof(cell)), at);
        }

        /// <summary>
        /// Places a reference to a record in a worldspace, in whichever exterior cell holds the
        /// position.
        /// </summary>
        /// <remarks>
        /// Exterior cells are addressed by position because most of them have no editor id: a cell
        /// is the 4096-unit square at <c>(floor(x / 4096), floor(y / 4096))</c> of its worldspace.
        /// </remarks>
        public AuthoredRecord PlaceInWorldspace(FormKey baseRecord, string worldspace, Placement at)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(worldspace);

            IWorldspaceGetter world = (FormKey.TryFactory(worldspace, out FormKey key)
                    ? _cache.TryResolve<IWorldspaceGetter>(key, out var byKey) ? byKey : null
                    : _cache.TryResolve<IWorldspaceGetter>(worldspace, out var byId) ? byId : null)
                ?? throw new ArgumentException($"the load order has no worldspace '{worldspace}'", nameof(worldspace));

            var grid = new P2Int((int)MathF.Floor(at.Position.X / CellSize), (int)MathF.Floor(at.Position.Y / CellSize));

            // Highest priority first, so the first context found for a cell is its winner.
            var seen = new HashSet<FormKey>();
            foreach (ISkyrimModGetter mod in _cache.PriorityOrder)
                foreach (var context in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(_cache))
                {
                    if (!seen.Add(context.Record.FormKey)) continue;
                    if (context.Record.Grid?.Point != grid) continue;
                    if (WorldspaceOf(context) is not { } parent || parent.FormKey != world.FormKey) continue;
                    return Place(baseRecord, context, at);
                }

            throw new ArgumentException($"{world.EditorID} has no cell at {grid.X}, {grid.Y}", nameof(at));
        }

        /// <summary>The worldspace a cell is in: up past its sub-block and block, which are its context's parents.</summary>
        private static IWorldspaceGetter? WorldspaceOf(IModContext context)
        {
            for (IModContext? at = context.Parent; at is not null; at = at.Parent)
                if (at.Record is IWorldspaceGetter world) return world;
            return null;
        }

        private AuthoredRecord Place(FormKey baseRecord, IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> context, Placement at)
        {
            if (!Plugin.EnumerateMajorRecords().Any(r => r.FormKey == baseRecord) && !_cache.TryResolve<IMajorRecordGetter>(baseRecord, out _))
                throw new ArgumentException($"nothing in the load order or the plugin is {baseRecord}", nameof(baseRecord));

            ICell cell = context.GetOrAddAsOverride(Plugin);
            var placement = new Mutagen.Bethesda.Skyrim.Placement
            {
                Position = at.Position,
                Rotation = new P3Float(Radians(at.Rotation.X), Radians(at.Rotation.Y), Radians(at.Rotation.Z)),
            };

            IMajorRecord placed;
            if (IsNpc(baseRecord))
            {
                var actor = new PlacedNpc(Plugin) { Placement = placement, Scale = at.Scale == 1f ? null : at.Scale };
                actor.Base.SetTo(baseRecord);
                cell.Temporary.Add(actor);
                placed = actor;
            }
            else
            {
                var reference = new PlacedObject(Plugin) { Placement = placement, Scale = at.Scale == 1f ? null : at.Scale };
                reference.Base.SetTo(baseRecord);
                cell.Temporary.Add(reference);
                placed = reference;
            }

            return new AuthoredRecord(placed.FormKey, placed.GetType().Name, placed.EditorID ?? "", FormKey.Null);

            static float Radians(float degrees) => degrees * MathF.PI / 180f;
        }

        private bool IsNpc(FormKey key) =>
            Plugin.Npcs.ContainsKey(key) || _cache.TryResolve<INpcGetter>(key, out _);

        /// <summary>Adds a record to a leveled item list, so it turns up wherever the list is drawn.</summary>
        /// <remarks>
        /// The list is overridden in the new plugin with the entry added. Another plugin overriding
        /// the same list wins or loses the whole list by load order; a patcher that merges leveled
        /// lists is how the two are reconciled.
        /// </remarks>
        public void AddToLeveledList(FormKey item, string leveledList, short level = 1, short count = 1)
        {
            ILeveledItemGetter list = Resolve<ILeveledItemGetter>(leveledList, nameof(leveledList));
            var copy = Plugin.LeveledItems.GetOrAddAsOverride(list);
            copy.Entries ??= [];
            copy.Entries.Add(new LeveledItemEntry
            {
                Data = new LeveledItemEntryData { Level = level, Count = count, Reference = new FormLink<IItemGetter>(item) },
            });
        }

        /// <summary>Adds a record to a container's contents -- a merchant's chest, a barrel.</summary>
        /// <remarks>The container is overridden in the new plugin, with the same caveat as a leveled list.</remarks>
        public void AddToContainer(FormKey item, string container, int count = 1)
        {
            IContainerGetter chest = Resolve<IContainerGetter>(container, nameof(container));
            var copy = Plugin.Containers.GetOrAddAsOverride(chest);
            copy.Items ??= [];
            copy.Items.Add(new ContainerEntry { Item = new ContainerItem { Item = new FormLink<IItemGetter>(item), Count = count } });
        }

        private TGetter Resolve<TGetter>(string name, string parameter) where TGetter : class, IMajorRecordGetter =>
            (FormKey.TryFactory(name, out FormKey key)
                ? _cache.TryResolve<TGetter>(key, out var byKey) ? byKey : null
                : _cache.TryResolve<TGetter>(name, out var byId) ? byId : null)
            ?? throw new ArgumentException($"the load order has no {typeof(TGetter).Name.TrimStart('I').Replace("Getter", "")} '{name}'", parameter);
    }
}
