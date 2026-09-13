using HKFBX.Hkx;
using LeanMeshIO;
using HKSK.Model;
using NIFSharp;
using SKAssets.Content.Nif;
using SKAssets.Export;
using Xunit;

namespace SKAssets.Export.Tests
{
    /// <summary>
    /// What a path turns out to be.
    /// </summary>
    /// <remarks>
    /// A tool that converts assets decides what to do with a file by asking this,
    /// so a wrong answer is not a wrong label -- it is a NIF converted as if it were
    /// something else, or a creature's folder taken apart file by file.
    ///
    /// Everything here reads the extracted tree and writes nothing.
    /// </remarks>
    public sealed class AssetRecognitionTests
    {
        private static string Chicken =>
            Corpus.ExtractedCreatures().FirstOrDefault(
                folder => folder.Replace('\\', '/').Contains("/chicken/", StringComparison.OrdinalIgnoreCase))
            ?? Corpus.ExtractedCreatures()[0];

        private static NifXmlDatabase Database => NifXmlDatabase.LoadEmbedded();

        private static RecognisedAsset Of(string path) =>
            AssetRecognition.Of(path, Database, SkyrimCache.Load(Corpus.Havok!));

        [CreatureFact]
        public void AFolderHoldingASkeletonIsACreature()
        {
            RecognisedAsset what = Of(Chicken);

            Assert.Equal(AssetKind.Creature, what.Kind);
            Assert.NotNull(what.Creature);
            Assert.Equal("chicken", what.Creature!.Name);
            Assert.Contains("20 animations", what.Summary);
        }

        /// <summary>
        /// And a folder that merely contains creatures is not one itself.
        /// </summary>
        /// <remarks>
        /// The distinction decides whether a tool takes the folder whole or opens it
        /// and looks at what is inside, and getting it wrong either loses a creature's
        /// animations or converts its skeleton twice.
        /// </remarks>
        [CreatureFact]
        public void AFolderOfCreaturesIsAFolder()
        {
            string actors = Path.GetFullPath(Path.Combine(Corpus.Havok!, "actors"));

            Assert.Equal(AssetKind.Folder, Of(actors).Kind);
        }

        [CreatureFact]
        public void AnActorSkeletonSaysSoAndSaysWhatItCarries()
        {
            RecognisedAsset what = Of(Path.Combine(Chicken, "skeleton.nif"));

            Assert.Equal(AssetKind.Mesh, what.Kind);
            Assert.Equal(NifRole.ActorSkeleton, what.Role);
            Assert.True(what.Profile!.IsSkeleton);

            // The numbers a person would want in a log rather than a bare label.
            Assert.Contains("ragdoll", what.Summary);
            Assert.Contains("constraints", what.Summary);
        }

        [CreatureFact]
        public void ASkinnedBodyIsToldApartFromTheSkeletonItRides()
        {
            CreatureAssets creature = CreatureExchange.Find(Chicken, null)!;
            RecognisedAsset what = Of(creature.Meshes[0]);

            Assert.Equal(AssetKind.Mesh, what.Kind);
            Assert.True(what.Profile!.IsSkinned);
            Assert.NotEqual(NifRole.ActorSkeleton, what.Role);
        }

        /// <summary>
        /// A rig and a clip are both .hkx and have nothing else in common.
        /// </summary>
        [CreatureFact]
        public void AHavokFileIsEitherASkeletonOrAnAnimation()
        {
            RecognisedAsset rig = Of(Path.Combine(Chicken, "skeleton.hkx"));

            Assert.Equal(AssetKind.HavokSkeleton, rig.Kind);
            Assert.Contains("bones", rig.Summary);
            Assert.Contains("ragdoll", rig.Summary);

            string animations = Path.Combine(
                Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Chicken))!, "animations");

            string clip = Directory.EnumerateFiles(animations, "*.hkx").Order(StringComparer.Ordinal).First();
            RecognisedAsset animation = Of(clip);

            Assert.Equal(AssetKind.HavokAnimation, animation.Kind);
            Assert.Contains("frames", animation.Summary);
        }

        [CreatureFact]
        public void AnFbxIsWhateverIsInIt()
        {
            CreatureAssets creature = CreatureExchange.Find(Chicken, SkyrimCache.Load(Corpus.Havok!))!;
            var db = Database;

            FbxDocument scene = CreatureExchange.Export(creature, db, out _, slots: []);

            string path = Path.Combine(Path.GetTempPath(), $"recognise-{Guid.NewGuid():N}.fbx");
            scene.Save(path);

            try
            {
                RecognisedAsset what = Of(path);

                Assert.Equal(AssetKind.Scene, what.Kind);
                Assert.NotNull(what.Contents);
                Assert.True(what.Contents!.HasMesh);
                Assert.True(what.Contents.HasRig);
                Assert.False(what.Contents.HasClips);
            }
            finally { File.Delete(path); }
        }

        /// <summary>
        /// What is not recognised says so, rather than being guessed at.
        /// </summary>
        /// <remarks>
        /// A converter that guesses is worse than one that refuses: the refusal is a
        /// line in a log, and the guess is a file somebody finds broken later.
        /// </remarks>
        [CreatureFact]
        public void WhatIsNotAnAssetIsNotPretendedToBeOne()
        {
            string text = Path.Combine(Path.GetTempPath(), $"recognise-{Guid.NewGuid():N}.txt");
            File.WriteAllText(text, "notes");

            try
            {
                Assert.Equal(AssetKind.Unknown, Of(text).Kind);
            }
            finally { File.Delete(text); }

            RecognisedAsset missing = Of(Path.Combine(Path.GetTempPath(), "no-such-file.nif"));

            Assert.Equal(AssetKind.Unknown, missing.Kind);
            Assert.NotNull(missing.Problem);
        }

        /// <summary>
        /// A file whose name lies about it is read for what it is.
        /// </summary>
        /// <remarks>
        /// The whole reason this opens files rather than reading extensions. A NIF
        /// renamed .hkx would otherwise be handed to the Havok reader, which would
        /// fail in a way that reads as a corrupt animation rather than as a mistake.
        /// </remarks>
        [CreatureFact]
        public void AnExtensionIsNotTakenAtItsWord()
        {
            string lying = Path.Combine(Path.GetTempPath(), $"recognise-{Guid.NewGuid():N}.hkx");
            File.Copy(Path.Combine(Chicken, "skeleton.nif"), lying, overwrite: true);

            try
            {
                RecognisedAsset what = Of(lying);

                // Not a skeleton and not an animation: it is neither, and says so.
                Assert.Equal(AssetKind.Unknown, what.Kind);
                Assert.NotNull(what.Problem);
            }
            finally { File.Delete(lying); }
        }
    }
}
