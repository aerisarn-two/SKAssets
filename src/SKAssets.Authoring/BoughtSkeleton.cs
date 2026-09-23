using HKFBX.Fbx;
using HKFBX.Model;
using NIFSharp;
using LeanMeshIO;
using SKAssets.Export;

namespace SKAssets.Authoring
{
    /// <summary>A bought rig, corrected, and the two files a creature needs it as.</summary>
    /// <param name="Rig">The rig as it now stands: grounded, centred, facing forward, renamed.</param>
    /// <param name="Grounded">What putting it where the game expects took.</param>
    /// <param name="Renamed">What putting it under the game's names changed, and what it left.</param>
    /// <param name="Mesh">The render skeleton, as a NIF.</param>
    public sealed record CorrectedSkeleton(
        Skeleton Rig,
        GroundedSkeleton Grounded,
        RenamedSkeleton Renamed,
        NifModel Mesh)
    {
        public override string ToString() => $"{Rig.Count} bones; {Grounded}; {Renamed}";
    }

    /// <summary>
    /// Corrects a rig bought for another engine, and writes it as the two skeletons Skyrim
    /// wants: the Havok one that animations are bound to, and the NIF one the game builds the
    /// actor's node tree from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both have to say the same thing. The Havok rig is what a clip's tracks are bound to and
    /// what a ragdoll is pinned to; the NIF is what the engine makes nodes from, what armour
    /// is fitted against, and what a magic effect or a body part looks up by name. A creature
    /// whose two disagree is one whose animations play on bones the renderer has not got.
    /// </para>
    /// <para>
    /// Which is why the corrections cannot be applied to one and not the other, and why this
    /// exists at all. A bought rig needs four things done to it -- standing on the floor,
    /// brought over the origin, turned to face the way the game faces, and put under the names
    /// everything in Skyrim looks a bone up by -- and doing them to the rig read out of an FBX
    /// leaves the FBX itself untouched. Anything that then converts that FBX again, as the
    /// mesh half does, produces a skeleton below the floor, facing backwards, under an
    /// exporter's names. So the corrected rig is written back out as a scene of its own, and
    /// the mesh is built from that.
    /// </para>
    /// <para>
    /// Nothing checks the two agree by construction, so <see cref="CreatureChecks.Skeleton"/>
    /// is the thing to run over the pair afterwards: it compares them bone by bone and in the
    /// world, and reports what the game would find.
    /// </para>
    /// </remarks>
    public static class BoughtSkeleton
    {
        /// <summary>An ankle and a toe of the same foot, under the names a 3ds Max biped uses.</summary>
        public static readonly (string Ankle, string Toe)[] BipedFeet =
        [
            ("Bip01_L_Foot", "Bip01_L_Toe0"),
            ("Bip01_R_Foot", "Bip01_R_Toe0"),
        ];

        /// <summary>Corrects the rig in <paramref name="fbxPath"/> and builds both skeletons.</summary>
        /// <param name="fbxPath">An FBX holding the rig.</param>
        /// <param name="database">The NIF format description the mesh is built against.</param>
        /// <param name="names">
        /// What to call the bones, or null to read the biped naming off the rig. Pass the same
        /// map everything else uses: a rig named one way and clips named another line up by
        /// accident at best.
        /// </param>
        /// <param name="feet">
        /// The ankle and toe of each foot, for reading which way the creature currently faces.
        /// Both feet, or the splay of the one is read as the facing of the creature. Empty
        /// leaves the facing alone.
        /// </param>
        /// <param name="meshName">
        /// What the NIF's root block is called. The game names it after the file: 42 of its 64
        /// skeleton meshes call their root <c>skeleton.nif</c> and ten more <c>Skeleton.nif</c>.
        /// </param>
        public static CorrectedSkeleton Correct(
            string fbxPath,
            NifXmlDatabase database,
            IReadOnlyDictionary<string, string>? names = null,
            (string Ankle, string Toe)[]? feet = null,
            string meshName = "skeleton.nif")
        {
            ArgumentException.ThrowIfNullOrEmpty(fbxPath);
            ArgumentNullException.ThrowIfNull(database);

            FbxDocument source = FbxDocument.Load(fbxPath);
            Skeleton bought = FbxAnimationReader.ReadSkeleton(source);

            (string Ankle, string Toe)[] pairs = feet ?? BipedFeet;
            float turn = pairs.Length > 0 ? SkeletonGrounding.TurnToFaceForward(bought, pairs) : 0f;

            GroundedSkeleton grounded = SkeletonGrounding.Ground(bought, centre: true, turnDegrees: turn);
            RenamedSkeleton renamed = SkeletonNaming.Rename(
                grounded.Rig, names ?? SkeletonNaming.BipedMap(grounded.Rig));

            // The mesh is built from the corrected rig written back out, not from the file it
            // came in on: the file still has the bones where and as its exporter left them.
            //
            // With one node put on top of it first, because a NIF's topmost node becomes the
            // file's root block and stops being a bone anybody can look up. Without it the rig
            // root is consumed and no node in the mesh is called NPC Root [Root], which is the
            // name the engine, the armour and the Havok binding all look a creature's root up
            // by. The game's own meshes have two nodes above it -- the file-named root and an
            // exporter's grouping node, '_' on the draugr and 'NPC' on the human -- so one is
            // the fewest that leaves the root a bone.
            FbxDocument corrected = FbxSkeletonWriter.Build(
                new SkeletonFile { Rig = Under(meshName, renamed.Rig) }, BoneNaming.Havok);

            NifModel mesh = SkeletonExchange.ImportMesh(corrected, database);

            // The converter names the root block after the scene rather than after the file,
            // and the game names it after the file: 42 of its 64 skeleton meshes call their
            // root 'skeleton.nif' and ten more 'Skeleton.nif'. Not one calls it 'Scene'.
            if (mesh.Blocks.Count > 0) mesh.SetString(mesh.Blocks[0], "Name", meshName);

            return new CorrectedSkeleton(renamed.Rig, grounded, renamed, mesh);
        }

        /// <summary>The same rig with one node above it, which the NIF's root block becomes.</summary>
        private static Skeleton Under(string name, Skeleton rig)
        {
            var bones = new List<Bone>(rig.Count + 1)
            {
                new(name, -1, BoneTransform.Identity),
            };

            foreach (Bone bone in rig.Bones)
                bones.Add(bone with { ParentIndex = bone.ParentIndex < 0 ? 0 : bone.ParentIndex + 1 });

            return new Skeleton { Name = rig.Name, Bones = bones };
        }
    }
}
