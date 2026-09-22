using SKAssets.Content.Assets;
using NIFSharp;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    /// <summary>
    /// A skeleton given back what a rebuild from an FBX leaves out.
    /// </summary>
    public class SkeletonExtrasTests
    {
        private static readonly NifXmlDatabase Db = NifXmlDatabase.LoadEmbedded();

        private static string Wolf => Path.Combine(Game.Meshes!, "actors", "canine", "character assets wolf");

        private static NifModel Skeleton() => NifModel.Load(Path.Combine(Wolf, "skeleton.nif"), Db);

        /// <summary>The wolf with its extra data cut away, given the wolf's own back.</summary>
        [HavokMastersFact]
        public void TheExtraDataAShippedSkeletonHasIsPutBack()
        {
            NifModel template = Skeleton();
            NifModel rebuilt = Skeleton();

            // What a converter writes: the bones, and a BSXFlags and nothing else hanging off
            // the root. The blocks stay where they are, unreferenced, as an FBX would never
            // have produced them at all.
            NifItem root = rebuilt.Blocks[0];
            NifItem flags = rebuilt.GetRefArray(root, "Extra Data List").First(b => b.Name == "BSXFlags");
            rebuilt.FindItem(root, "Num Extra Data List")!.Value.SetCount(1);
            NifItem list = rebuilt.FindItem(root, "Extra Data List")!;
            rebuilt.UpdateArraySize(list);
            list.Children[0].Value.SetLink(rebuilt.IndexOf(flags));

            var notes = SkeletonExtras.Carry(rebuilt, template, boneMap: null, bodies: null);

            // The four the game reads off an actor's root. The wolf also carries an inventory
            // marker, which is a marker for a model shown in an inventory and not an actor's,
            // and is not carried.
            var back = rebuilt.GetRefArray(root, "Extra Data List").ToList();
            Assert.Equal(["BBX", "BSBoneLOD", "BSX", "SkeletonID"], back.Select(b => rebuilt.GetName(b)).Order());

            // The identifier is the template's, since it identifies an export and not a rig.
            NifItem id = back.Single(b => rebuilt.GetName(b) == "SkeletonID");
            Assert.Equal(template.GetUInt(template.Blocks.First(b => template.GetName(b) == "SkeletonID"), "Integer Data"),
                rebuilt.GetUInt(id, "Integer Data"));

            // The bones are the same, so every LOD entry survives.
            NifItem lod = back.Single(b => b.Name == "BSBoneLODExtraData");
            Assert.Equal(template.GetUInt(template.Blocks.First(b => b.Name == "BSBoneLODExtraData"), "BoneLOD Count"),
                rebuilt.GetUInt(lod, "BoneLOD Count"));
            Assert.Contains(notes, n => n.Contains("BSBoneLOD", StringComparison.Ordinal));

            // The box stands on the ground: its centre is its own height up.
            NifItem box = back.Single(b => b.Name == "BSBound");
            NifVector3 centre = rebuilt.FindItem(box, "Center")!.Value.Get<NifVector3>();
            NifVector3 half = rebuilt.FindItem(box, "Dimensions")!.Value.Get<NifVector3>();
            Assert.Equal(centre.Z, half.Z, 3);
            Assert.True(half.Z > 0, "the bones have some height");

            // And the file it all goes into still measures up.
            Assert.DoesNotContain(CreatureChecks.BlockSizes(rebuilt), f => f.Severity == FindingSeverity.Error);
        }

        /// <summary>A rig that has none of the bones the list names does not get an empty list.</summary>
        [HavokMastersFact]
        public void ALodListNamingNoBoneOfThisRigIsLeftOut()
        {
            NifModel template = Skeleton();
            NifModel rebuilt = Skeleton();

            foreach (NifItem node in rebuilt.Blocks.Where(b => b.Name == "NiNode"))
                rebuilt.SetString(node, "Name", "cat_" + rebuilt.GetName(node));

            NifItem root = rebuilt.Blocks[0];
            rebuilt.FindItem(root, "Num Extra Data List")!.Value.SetCount(0);
            rebuilt.UpdateArraySize(rebuilt.FindItem(root, "Extra Data List")!);

            var notes = SkeletonExtras.Carry(rebuilt, template);

            Assert.DoesNotContain(rebuilt.GetRefArray(root, "Extra Data List"), b => b.Name == "BSBoneLODExtraData");
            Assert.Contains(notes, n => n.Contains("left out", StringComparison.Ordinal));
        }
    }
}
