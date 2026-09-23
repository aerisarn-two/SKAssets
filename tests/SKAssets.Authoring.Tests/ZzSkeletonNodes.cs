using System.Text;
using HKFBX.Hkx;
using NIFSharp;
using Xunit;
namespace SKAssets.Authoring.Tests;

// What a skeleton NIF holds that its Havok rig does not: the nodes the game reads off
// an actor which are not bones the animation drives.
public sealed class ZzSkeletonNodes
{
    [HavokMastersFact]
    public void Dump()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string meshes = Game.Meshes ?? "";
        if (outDir.Length == 0 || meshes.Length == 0) return;
        var db = NifXmlDatabase.LoadEmbedded();
        var sb = new StringBuilder();
        var everywhere = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        int creatures = 0;

        foreach (string nifPath in Directory.EnumerateFiles(Path.Combine(meshes, "actors"), "skeleton*.nif", SearchOption.AllDirectories).Order())
        {
            string folder = Path.GetDirectoryName(nifPath)!;
            string? hkxPath = Directory.EnumerateFiles(folder, "*.hkx")
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                    .Equals(Path.GetFileNameWithoutExtension(nifPath), StringComparison.OrdinalIgnoreCase));
            if (hkxPath is null) continue;

            NifModel nif;
            HKFBX.Model.SkeletonFile rig;
            try { nif = NifModel.Load(nifPath, db); rig = HkxSkeletonFile.Read(hkxPath); } catch { continue; }

            var bones = new HashSet<string>(rig.Rig.Bones.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
            var extra = nif.Blocks
                .Where(b => b.Name is "NiNode" or "BSFadeNode" or "BSLeafAnimNode")
                .Select(b => nif.GetName(b) ?? "")
                .Where(n => n.Length > 0 && !bones.Contains(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

            creatures++;
            string who = Path.GetRelativePath(meshes, nifPath);
            sb.AppendLine($"{who}: {rig.Rig.Bones.Count} bones in the rig, {extra.Count} nodes beside them"
                + (extra.Count > 0 ? ": " + string.Join(", ", extra) : ""));

            foreach (string name in extra)
            {
                if (!everywhere.TryGetValue(name, out var who2)) everywhere[name] = who2 = [];
                who2.Add(who);
            }
        }

        sb.AppendLine();
        sb.AppendLine($"== {creatures} skeletons that have both a NIF and a rig");
        foreach ((string name, List<string> who) in everywhere.OrderByDescending(p => p.Value.Count).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"   {name,-34} on {who.Count,3} of {creatures}{(who.Count <= 3 ? ": " + string.Join(", ", who) : "")}");

        File.WriteAllText(Path.Combine(outDir, "skeleton_nodes.txt"), sb.ToString());
    }
}
