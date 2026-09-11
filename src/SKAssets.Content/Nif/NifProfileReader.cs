using NIFBX.Nif;
using NIFSharp;

namespace SKAssets.Content.Nif
{
    /// <summary>
    /// Reads a mesh into the census the classification works from.
    /// </summary>
    /// <remarks>
    /// One pass over the block list, and one more over the root's children to settle
    /// whether a skin's bones are all outside the file. Nothing is kept but the
    /// counts and the names, so a sweep of the whole game holds nothing open.
    ///
    /// The schema is expensive to build and safe to share, so a reader is made once
    /// and used for everything.
    /// </remarks>
    public sealed class NifProfileReader
    {
        private readonly NifXmlDatabase _schema;

        /// <summary>A reader over the schema NIFSharp embeds.</summary>
        public NifProfileReader()
            : this(NifXmlDatabase.LoadEmbedded())
        {
        }

        /// <summary>A reader over a schema already loaded.</summary>
        public NifProfileReader(NifXmlDatabase schema)
        {
            ArgumentNullException.ThrowIfNull(schema);
            _schema = schema;
        }

        /// <summary>The census of a mesh.</summary>
        public NifProfile Read(Stream mesh)
        {
            ArgumentNullException.ThrowIfNull(mesh);

            return Read(NifModel.Load(mesh, _schema));
        }

        /// <summary>The census of a mesh already loaded.</summary>
        public static NifProfile Read(NifModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            NifItem? root = RootOf(model);

            int collisions = 0, phantoms = 0, constraints = 0, shapes = 0;
            int timeControllers = 0, managers = 0, sequences = 0, particles = 0, addons = 0;
            bool skeleton = false, skinned = false, dismembered = false;
            bool furniture = false, editorMarker = false;
            string? behavior = null, attach = null;
            uint? stored = null;

            var nodes = new List<string>();
            var skinBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var boneBlocks = new HashSet<NifItem>();

            foreach (NifItem block in model.Blocks)
            {
                string type = block.Name;

                if (model.BlockInherits(block, "bhkCollisionObject")) collisions++;
                if (model.BlockInherits(block, "bhkSPCollisionObject")) phantoms++;
                if (model.BlockInherits(block, "bhkConstraint")) constraints++;

                // A blend collision object is what makes a file a skeleton, with or
                // without a ragdoll constraint anywhere in it.
                if (model.BlockInherits(block, "bhkBlendCollisionObject")) skeleton = true;

                if (model.BlockInherits(block, "NiSkinInstance"))
                {
                    skinned = true;

                    if (type == "BSDismemberSkinInstance") dismembered = true;

                    foreach (NifItem bone in model.GetRefArray(block, "Bones"))
                    {
                        boneBlocks.Add(bone);
                        skinBones.Add(model.GetName(bone));
                    }
                }

                if (model.BlockInherits(block, "NiTimeController")) timeControllers++;
                if (model.BlockInherits(block, "NiControllerManager")) managers++;
                if (model.BlockInherits(block, "NiControllerSequence")) sequences++;
                if (model.BlockInherits(block, "NiParticleSystem")) particles++;

                if (model.BlockInherits(block, "NiNode"))
                {
                    string name = model.GetName(block);
                    nodes.Add(name);

                    // A BSValueNode is a node, so this counts an attachment point
                    // once however it was marked: by type, or by being named for one.
                    if (model.BlockInherits(block, "BSValueNode")
                        || name.Contains("AddonNode", StringComparison.Ordinal))
                    {
                        addons++;
                    }
                }
                if (model.BlockInherits(block, "NiTriBasedGeom") || type.Contains("TriShape", StringComparison.Ordinal)) shapes++;

                if (type == "BSFurnitureMarkerNode") furniture = true;
                if (type == "BSXFlags") stored = model.GetUInt(block, "Integer Data");
                if (type == "BSBehaviorGraphExtraData") behavior = model.GetString(block, "Behaviour Graph File");

                if (type == "NiStringExtraData" && model.GetName(block) == "Prn")
                    attach = model.GetString(block, "String Data");

                if (model.GetName(block).Contains("EditorMarker", StringComparison.OrdinalIgnoreCase))
                    editorMarker = true;
            }

            return new NifProfile
            {
                RootType = root?.Name ?? "<none>",
                StoredBsxFlags = stored,
                CalculatedBsxFlags = model.Calculate(),
                Collisions = collisions,
                Phantoms = phantoms,
                Constraints = constraints,
                IsSkeleton = skeleton,
                IsSkinned = skinned,
                IsDismembered = dismembered,
                HasExternalSkeleton = IsExternallySkinned(model, root, skinned, boneBlocks),
                Shapes = shapes,
                TimeControllers = timeControllers,
                ControllerManagers = managers,
                ControllerSequences = sequences,
                ParticleSystems = particles,
                AddonNodes = addons,
                HasFurnitureMarkers = furniture,
                HasEditorMarker = editorMarker,
                BehaviorGraph = string.IsNullOrWhiteSpace(behavior) ? null : behavior,
                AttachParent = string.IsNullOrWhiteSpace(attach) ? null : attach,
                Nodes = nodes,
                SkinBones = [.. skinBones],
            };
        }

        /// <summary>
        /// Whether the skin names no bone this file contains.
        /// </summary>
        /// <remarks>
        /// The test is NIFBX's, which is ck-cmd's: strike the root's own children off
        /// the bone list, and if nothing is left the file is mounted on a skeleton it
        /// does not carry. The root has to be exactly <c>NiNode</c> -- a
        /// <c>BSFadeNode</c> is a scene, not an attachment.
        /// </remarks>
        private static bool IsExternallySkinned(NifModel model, NifItem? root, bool skinned, HashSet<NifItem> bones)
        {
            if (!skinned || root is null || !model.BlockInherits(root, "NiNode"))
                return false;

            var remaining = new HashSet<NifItem>(bones);

            foreach (NifItem child in model.GetChildren(root))
                remaining.Remove(child);

            return remaining.Count == 0 && root.Name == "NiNode";
        }

        private static NifItem? RootOf(NifModel model)
        {
            if (model.FindItem(model.Footer, "Roots") is { Children.Count: > 0 } roots
                && model.GetBlock(roots.Children[0]) is { } named)
            {
                return named;
            }

            return model.Blocks.Count > 0 ? model.Blocks[0] : null;
        }
    }
}
