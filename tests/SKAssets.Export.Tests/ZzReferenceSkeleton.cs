using HKFBX.Hkx;
using NIFBX.Conversion;
using NIFSharp;
using SKAssets.Export;
using Xunit;
namespace SKAssets.Export.Tests;

// Writes a shipped quadruped's skeleton (rig + ragdoll + mesh) to FBX as the reference
// for authoring a new creature's ragdoll by hand.
public sealed class ZzReferenceSkeleton
{
    [HavokCorpusFact]
    public void Export()
    {
        string meshes = Environment.GetEnvironmentVariable("SKASSETS_HAVOK_MESHES")!;
        string outDir = Environment.GetEnvironmentVariable("HKSK_CENSUS_OUT") ?? Path.GetTempPath();
        var schema = NifXmlDatabase.LoadEmbedded();
        foreach (var (name, folder) in new[] { ("sabrecat", "actors/sabrecat/character assets"), ("skeever", "actors/skeever/character assets") })
        {
            string dir = Path.Combine(meshes, folder);
            var doc = SkeletonExchange.Export(NifModel.Load(Path.Combine(dir, "skeleton.nif"), schema), HkxSkeletonFile.Read(Path.Combine(dir, "skeleton.hkx")));
            doc.Save(Path.Combine(outDir, name + "_skeleton.fbx"));
        }
    }
}
