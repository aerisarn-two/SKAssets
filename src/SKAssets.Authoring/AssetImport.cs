using Mutagen.Bethesda.Plugins;
using SKAssets.Content.Assets;

namespace SKAssets.Authoring
{
    /// <summary>The record type an imported mesh is given, which decides everything written with it.</summary>
    /// <remarks>
    /// Each is a base record that names a mesh and can be copied from a vanilla one; see
    /// <c>docs/authoring.md</c> for what each owns, shares and is made by. Actors are not
    /// here: a race is a skeleton, a Havok project and three caches (<c>docs/new-race.md</c>).
    /// </remarks>
    public enum AuthoredKind
    {
        Static,
        MoveableStatic,
        Furniture,
        Door,
        Activator,
        Container,
        Flora,
        Tree,
        Light,
        MiscItem,
        Key,
        SoulGem,
        Ingestible,
        Ingredient,
        Book,
        Scroll,
        Ammunition,
        Weapon,
        Armor,
        ArtObject,
    }

    /// <summary>Where a mesh goes on the records an import writes.</summary>
    public enum ModelSlot
    {
        /// <summary>
        /// The mesh: the record's model; a weapon's, ammunition's, book's or scroll's model in
        /// the world; an armour's third-person body mesh on its addons.
        /// </summary>
        Main,

        /// <summary>
        /// A weapon's first-person model, or an armour's first-person body mesh. A weapon without
        /// one uses <see cref="Main"/>, as 120 of vanilla's 368 base weapons do.
        /// </summary>
        FirstPerson,

        /// <summary>An armour's female body mesh. Without one, the male mesh is used.</summary>
        Female,

        /// <summary>An armour's female first-person mesh.</summary>
        FemaleFirstPerson,

        /// <summary>An armour lying on the ground (<c>_go</c>).</summary>
        Ground,

        /// <summary>An armour lying on the ground, female.</summary>
        FemaleGround,

        /// <summary>
        /// The lightest body weight of an armour mesh, <c>_0</c>, beside <see cref="Main"/> as
        /// <c>_1</c>; without one, both weights are the same mesh.
        /// </summary>
        LightWeight,

        /// <summary>A book's inventory art, or a scroll's menu display object.</summary>
        Inventory,

        /// <summary>The mesh an ammunition's projectile flies as.</summary>
        Projectile,
    }

    /// <summary>One FBX to bring in, and the record to model it on.</summary>
    public sealed record AssetImport
    {
        /// <summary>The record type to write.</summary>
        public required AuthoredKind Kind { get; init; }

        /// <summary>
        /// The vanilla record to copy, by editor id (<c>IronSword</c>) or form key
        /// (<c>012EB7:Skyrim.esm</c>). A variant -- an enchanted weapon, a templated armour -- is
        /// traced to the base it is made from.
        /// </summary>
        public required string Template { get; init; }

        /// <summary>The new record's editor id; the records written with it are named from it.</summary>
        public required string EditorId { get; init; }

        /// <summary>
        /// Put before every editor id and mesh file name this import writes -- the asset's, the
        /// records it owns, its recipes: <c>MyMod_</c>. Null takes the plugin's
        /// <see cref="PluginAuthoring.Prefix"/>; an empty string asks for none.
        /// </summary>
        public string? Prefix { get; init; }

        /// <summary>The name shown in game, where the record has one; the template's otherwise.</summary>
        public string? Name { get; init; }

        /// <summary>An FBX for each slot the asset fills; <see cref="ModelSlot.Main"/> is required.</summary>
        public required IReadOnlyDictionary<ModelSlot, string> Fbx { get; init; }

        /// <summary>
        /// Where the meshes go, under <c>Meshes</c>: <c>MyMod\Weapons</c>. Each is named from the
        /// editor id.
        /// </summary>
        public required string MeshFolder { get; init; }

        /// <summary>
        /// Where the meshes' own textures go, under <c>Textures</c>; the mesh folder by default. A
        /// texture the FBX takes from the game is left where it is.
        /// </summary>
        public string? TextureFolder { get; init; }

        /// <summary>Whether to copy the recipes that make the template -- crafting and tempering.</summary>
        public bool Recipes { get; init; } = true;
    }

    /// <summary>A record an import wrote.</summary>
    /// <param name="FormKey">Its form key in the new plugin.</param>
    /// <param name="Type">Its type, as Mutagen names it.</param>
    /// <param name="EditorId">Its editor id.</param>
    /// <param name="CopiedFrom">The vanilla record it is a copy of.</param>
    public sealed record AuthoredRecord(FormKey FormKey, string Type, string EditorId, FormKey CopiedFrom);

    /// <summary>What an import wrote, and what it found wrong with it.</summary>
    /// <param name="Record">The record the asset is.</param>
    /// <param name="Records">Every record written: the asset's, the ones it owns, its recipes.</param>
    /// <param name="Meshes">Every mesh written, relative to the output folder.</param>
    /// <param name="Textures">Every texture written for those meshes, relative to the output folder.</param>
    /// <param name="Findings">What the mesh rules say about each mesh against the record that names it.</param>
    /// <param name="Notes">Choices made for the caller: a template traced to its base, a slot filled from another.</param>
    public sealed record ImportResult(
        AuthoredRecord Record,
        IReadOnlyList<AuthoredRecord> Records,
        IReadOnlyList<string> Meshes,
        IReadOnlyList<string> Textures,
        IReadOnlyList<(string Mesh, MeshFinding Finding)> Findings,
        IReadOnlyList<string> Notes);
}
