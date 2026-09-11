namespace SKAssets.Assets
{
    /// <summary>
    /// What an asset is, coarsely enough to sort a sweep by.
    /// </summary>
    /// <remarks>
    /// The media type says what the file is; this says what it is for. They do not
    /// always agree, because the game uses one container for several jobs: a
    /// <c>.hkx</c> is a Havok file whether it holds a skeleton, a behaviour graph or
    /// a single animation, and a <c>.wav</c> under <c>Sound/Voice</c> is a line of
    /// dialogue rather than a sound effect. Where the extension alone cannot tell
    /// them apart, the folder does -- see <see cref="AssetMediaTypes.ForPath"/>.
    /// </remarks>
    public enum AssetCategory
    {
        /// <summary>An extension the catalogue does not know.</summary>
        Unknown = 0,

        /// <summary>Geometry: NIF models, their LOD forms and morph data.</summary>
        Model,

        /// <summary>Images the renderer samples.</summary>
        Texture,

        /// <summary>Havok: skeletons, behaviour graphs, animations, ragdolls.</summary>
        Animation,

        /// <summary>Sound effects.</summary>
        Sound,

        /// <summary>Music tracks.</summary>
        Music,

        /// <summary>Spoken dialogue, and the lip sync that goes with it.</summary>
        Voice,

        /// <summary>Papyrus, compiled or source.</summary>
        Script,

        /// <summary>Menus and HUD: Scaleform movies and what they load.</summary>
        Interface,

        /// <summary>Localised strings for a plugin.</summary>
        Translation,

        /// <summary>Full-motion video.</summary>
        Video,

        /// <summary>Compiled shader packages.</summary>
        Shader,

        /// <summary>Human-readable data files.</summary>
        Text,

        /// <summary>Game data that is none of the above: quest start-up lists, LOD tables.</summary>
        Data,

        /// <summary>An archive of other assets.</summary>
        Archive,

        /// <summary>A plugin: master, plugin or light plugin.</summary>
        Plugin,
    }
}
