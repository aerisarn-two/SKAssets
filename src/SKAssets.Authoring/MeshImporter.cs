using LeanMeshIO;
using NIFBX.Conversion;
using NIFBX.Fbx;
using NIFSharp;
using SKAssets.Content.Assets;
using SKAssets.Content.Nif;

namespace SKAssets.Authoring
{
    /// <summary>One FBX to convert, and where its mesh and textures go.</summary>
    /// <param name="Fbx">The FBX.</param>
    /// <param name="Nif">The NIF to write.</param>
    /// <param name="DataFolder">The Data folder being written: <c>Meshes</c> and <c>Textures</c> are under it.</param>
    /// <param name="TextureFolder">Where the mesh's own textures go, under <c>Textures</c>: <c>MyMod\Weapons</c>.</param>
    /// <param name="Prefix">Put before each texture's file name.</param>
    /// <param name="BodyPart">
    /// The biped slot the mesh is worn in, 30 to 61, where it is worn at all.
    /// </param>
    /// <remarks>
    /// A skinned mesh is attached to an actor through the slot each of its partitions names, and
    /// a converter has no way of knowing which: NIFBX writes 0, which is not a slot. The engine
    /// and the Creation Kit read it as a biped object, find it out of range, fall back to asking
    /// the file which node to hang itself off -- a `Prn` string -- and give up when there is none:
    /// "Could not find parent node extra data for ...". The armour the mesh is worn as is what
    /// knows the slot, so it is given here. 362 of the game's own worn meshes say 32, the body.
    /// </remarks>
    public sealed record MeshTarget(string Fbx, string Nif, string DataFolder, string TextureFolder, string Prefix,
        int? BodyPart = null);

    /// <summary>A converted mesh.</summary>
    /// <param name="Profile">Its census, for the mesh rules.</param>
    /// <param name="Textures">The textures written for it, relative to the Data folder.</param>
    /// <param name="Findings">What could not be done: a texture the FBX names and nobody can find.</param>
    public sealed record ImportedMesh(NifProfile Profile, IReadOnlyList<string> Textures, IReadOnlyList<MeshFinding> Findings);

    /// <summary>Turns an FBX into a NIF on disk, with its textures.</summary>
    public interface IMeshImporter
    {
        ImportedMesh Import(MeshTarget target);
    }

    /// <summary>The conversion NIFBX does, FBX to NIF, with the textures brought along.</summary>
    /// <remarks>
    /// <para>
    /// A DCC tool names a texture where it found it -- <c>C:\work\sword_d.png</c> -- and NIFBX
    /// keeps what it can of that: a path with <c>textures\</c> in it from there on, anything else
    /// as it came, with its extension made <c>.dds</c>. So a texture the author made names a file
    /// the game will never find, and the file itself is not written anywhere.
    /// </para>
    /// <para>
    /// Here every texture the FBX references is looked for beside the FBX -- where its relative
    /// path points from the FBX's folder, where its absolute one does, and by name in the
    /// FBX's folder and a <c>textures</c> folder beside it, whatever the case of the name. One that is found is converted into
    /// <c>Textures\&lt;folder&gt;\&lt;prefix&gt;&lt;name&gt;.dds</c> and the NIF is pointed at it. One
    /// that is not keeps its path when that is a game path, and is reusing a texture the game
    /// already has; an absolute path nobody can find is reported.
    /// </para>
    /// </remarks>
    public sealed class FbxMeshImporter(NifXmlDatabase database, FbxToNifOptions? options = null, ITextureConverter? textures = null) : IMeshImporter
    {
        private readonly ITextureConverter _textures = textures ?? new DdsTextureConverter();

        /// <summary>An importer with the NIF format description built into NIFSharp.</summary>
        public FbxMeshImporter() : this(NifXmlDatabase.LoadEmbedded()) { }

        /// <summary>The fields of an effect shader that name a texture directly, rather than through a set.</summary>
        private static readonly string[] EffectTextures =
            ["Source Texture", "Greyscale Texture", "Env Map Texture", "Normal Texture", "Env Mask Texture"];

        public ImportedMesh Import(MeshTarget target)
        {
            var scene = new FbxScene(FbxDocument.Load(target.Fbx));
            Dictionary<string, List<string>> sources = Sources(scene, Path.GetDirectoryName(Path.GetFullPath(target.Fbx))!);

            NifModel model = new FbxToNif(scene, options).Convert(database);

            var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var findings = new List<MeshFinding>();

            foreach (NifItem block in model.Blocks)
            {
                if (block.Name == "BSShaderTextureSet" && Child(block, "Textures") is { } set)
                    foreach (NifItem slot in set.Children) Retarget(slot);
                else if (block.Name == "BSEffectShaderProperty")
                    foreach (string field in EffectTextures)
                        if (Child(block, field) is { } slot) Retarget(slot);
            }

            // A worn mesh's shapes hang off the root, as every worn mesh the game has does.
            if (target.BodyPart is not null) findings.AddRange(WornMesh.Flatten(model)
                .Select(n => new MeshFinding("skin-parent", FindingSeverity.Note, n)));

            // A skinned shape's bound is rebuilt each frame from one sphere per bone, and the
            // conversion leaves every sphere empty.
            findings.AddRange(WornMesh.Bounds(model)
                .Select(n => new MeshFinding("skin-bounds", FindingSeverity.Note, n)));

            // Every partition of every skinned shape names the slot the mesh is worn in.
            if (target.BodyPart is { } worn)
                foreach (NifItem block in model.Blocks)
                    if (block.Name == "BSDismemberSkinInstance")
                        foreach (NifItem partition in Child(block, "Partitions")?.Children ?? [])
                            if (Child(partition, "Body Part") is { } part)
                                part.Value.SetCount((ulong)worn);

            // The texture paths were rewritten after the conversion measured the blocks, and a
            // block that says 116 bytes and writes 132 is a file the game gives up on with
            // "stream size mismatch". Nothing recomputes the header but this.
            model.UpdateHeader();

            Directory.CreateDirectory(Path.GetDirectoryName(target.Nif)!);
            model.Save(target.Nif);
            return new ImportedMesh(NifProfileReader.Read(model), [.. written.Values.Distinct()], findings);

            void Retarget(NifItem slot)
            {
                if (slot.Value.Get<string>() is not { Length: > 0 } path) return;

                string? source = sources.TryGetValue(path, out var candidates)
                    ? candidates.Select(Existing).FirstOrDefault(found => found is not null)
                    : null;
                if (source is null)
                {
                    if (IsRooted(path))
                        findings.Add(new MeshFinding("texture-not-found", FindingSeverity.Warning,
                            $"the FBX names {path}, which is not beside it; the mesh names a file the game cannot find"));
                    return;
                }

                if (!written.TryGetValue(source, out string? relative))
                {
                    string name = target.Prefix + Path.GetFileNameWithoutExtension(source) + ".dds";
                    relative = Path.Combine("Textures", target.TextureFolder, name).Replace('/', '\\');
                    _textures.Convert(source, Path.Combine(target.DataFolder, relative.Replace('\\', Path.DirectorySeparatorChar)));
                    written[source] = relative;
                }

                // A mesh names a texture from the Data folder, 'textures\...', as vanilla's do.
                slot.Value.Set(relative);
            }
        }

        /// <summary>Where each texture the FBX references may be, by the path the NIF will carry for it.</summary>
        private static Dictionary<string, List<string>> Sources(FbxScene scene, string folder)
        {
            var sources = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (FbxObject texture in scene.OfClass("Texture"))
            {
                string? relative = texture.Child("RelativeFilename")?.Properties.FirstOrDefault() as string;
                string? absolute = texture.Child("FileName")?.Properties.FirstOrDefault() as string;
                string? given = relative is { Length: > 0 } ? relative : absolute;
                if (string.IsNullOrEmpty(given)) continue;

                // The same key the conversion writes: RelativeFilename first, then FileName.
                string key = MaterialData.NormalizeTexturePath(given);
                if (!sources.TryGetValue(key, out var list)) sources[key] = list = [];

                foreach (string? candidate in new[] { relative, absolute })
                {
                    if (string.IsNullOrEmpty(candidate)) continue;

                    string local = candidate.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    string name = Path.GetFileName(local);

                    // Beside the FBX, never beside wherever the process happens to run.
                    list.Add(IsRooted(candidate) ? local : Path.Combine(folder, local));
                    list.Add(Path.Combine(folder, name));
                    list.Add(Path.Combine(folder, "textures", name));
                }
            }

            return sources;
        }

        /// <summary>
        /// The file at a path, matched without regard to case: a DCC tool and the game spell the
        /// same texture <c>IronLongsword.dds</c> and <c>ironlongsword.dds</c>, and only one kind of
        /// file system does not care.
        /// </summary>
        private static string? Existing(string path)
        {
            if (File.Exists(path)) return path;

            string? folder = Path.GetDirectoryName(path);
            if (folder is null || !Directory.Exists(folder)) return null;

            string name = Path.GetFileName(path);
            return Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        }

        private static NifItem? Child(NifItem item, string name) =>
            item.Children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

        /// <summary>A path on a disc -- <c>C:\...</c>, <c>/home/...</c>, <c>\\server\...</c> -- rather than one in the game.</summary>
        private static bool IsRooted(string path) =>
            path.Length > 1 && (path[1] == ':' || path[0] is '\\' or '/');
    }
}
