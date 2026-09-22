using LeanMeshIO;
using NIFBX.Conversion;
using NIFBX.Fbx;
using NIFSharp;
using SKAssets.Content.Nif;

namespace SKAssets.Authoring
{
    /// <summary>Turns an FBX into a NIF on disk.</summary>
    public interface IMeshImporter
    {
        /// <summary>Converts <paramref name="fbxPath"/> and writes it to <paramref name="nifPath"/>.</summary>
        /// <returns>The written mesh's census, for the mesh rules.</returns>
        NifProfile Import(string fbxPath, string nifPath);
    }

    /// <summary>The conversion NIFBX does, FBX to NIF.</summary>
    public sealed class FbxMeshImporter(NifXmlDatabase database, FbxToNifOptions? options = null) : IMeshImporter
    {
        /// <summary>An importer with the NIF format description built into NIFSharp.</summary>
        public FbxMeshImporter() : this(NifXmlDatabase.LoadEmbedded()) { }

        public NifProfile Import(string fbxPath, string nifPath)
        {
            NifModel model = new FbxToNif(new FbxScene(FbxDocument.Load(fbxPath)), options).Convert(database);

            Directory.CreateDirectory(Path.GetDirectoryName(nifPath)!);
            model.Save(nifPath);
            return NifProfileReader.Read(model);
        }
    }
}
