using HKFBX.Hkx;
using HKSK.Model;
using Mutagen.Bethesda.Skyrim;
using NIFBX.Conversion;
using NIFSharp;
using SKAssets.Export;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    /// <summary>A new creature made from the wolf, against the game and its extracted meshes.</summary>
    public sealed class CreatureCorpusTests : IDisposable
    {
        private readonly string _out = Path.Combine(Path.GetTempPath(), "skassets-creature-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
        }

        /// <summary>
        /// The wolf, cloned as a direwolf with its own skeleton and body from FBX: the project, its
        /// files and its cache entries are the direwolf's, the race wears them, the skin dresses the
        /// new race, and the project opens as an actor from the caches written.
        /// </summary>
        [HavokMastersFact]
        public void AWolfBecomesADirewolf()
        {
            string meshes = Game.Meshes!;
            string wolf = Path.Combine(meshes, "actors", "canine", "character assets wolf");
            NifXmlDatabase schema = NifXmlDatabase.LoadEmbedded();

            // the wolf's own skeleton and body, taken to FBX as an author would hand them back
            string skeleton = Path.Combine(_out, "source", "skeleton.fbx");
            string body = Path.Combine(_out, "source", "body.fbx");
            Directory.CreateDirectory(Path.GetDirectoryName(skeleton)!);
            SkeletonExchange.Export(NifModel.Load(Path.Combine(wolf, "skeleton.nif"), schema), HkxSkeletonFile.Read(Path.Combine(wolf, "skeleton.hkx"))).Save(skeleton);
            new NifToFbx(NifModel.Load(Path.Combine(wolf, "wolf.nif"), schema)).Convert().Save(body);

            CreatureResult made;
            using (var authoring = PluginAuthoring.Open(Game.Data!, "MyMod.esp", _out))
            {
                authoring.Prefix = "MyMod_";
                made = authoring.ImportCreature(new NewCreature
                {
                    Template = "WolfRace", Name = "Direwolf", DisplayName = "Direwolf", SourceMeshes = meshes,
                    Skeleton = skeleton,
                    Body = new Dictionary<ModelSlot, string> { [ModelSlot.Main] = body },
                    Npc = "EncWolf",
                });
                authoring.Save();
            }

            Assert.Equal("MyMod_DirewolfProject", made.Project);
            Assert.Equal(new CacheAmendment("MyMod_DirewolfProject", Amendment.Unchanged, Amendment.Added, Amendment.Added), made.Caches);

            string folder = Path.Combine(_out, "Meshes", "actors", "MyMod_Direwolf");
            Assert.True(File.Exists(Path.Combine(folder, "MyMod_DirewolfProject.hkx")));
            // copied as the character spells them, which is not the disc's case
            string Under(string name) => Directory.GetDirectories(folder).Single(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
            // 66 of the wolf's 72: the other six are paired killmoves under ..\SharedKillMoves, which the
            // direwolf's folder, at the wolf's depth, reaches as the wolf's does
            Assert.Equal(66, Directory.GetFiles(Under("animations")).Length);
            Assert.Contains(made.Notes, n => n.StartsWith("6 animations live outside"));
            Assert.Contains(Directory.GetFiles(Under("character assets wolf")), f => Path.GetFileName(f).Equals("skeleton.hkx", StringComparison.OrdinalIgnoreCase));
            // Nothing wrong with the files written. The wolf's own skeleton.nif and skeleton.hkx
            // already disagree on six of its fifty bones, the worst Canine_RUpperLip by 2.7
            // units, and the copy inherits that as a warning; an error is a file that is wrong.
            Assert.DoesNotContain(made.Findings, f => f.Finding.Severity == Content.Assets.FindingSeverity.Error);
            Assert.Contains(made.Findings, f => f.Finding.Rule == "skin-weights" && f.Finding.Severity == Content.Assets.FindingSeverity.Note);
            Assert.Contains(made.Findings, f => f.Finding.Rule == "triangles" && f.Finding.Severity == Content.Assets.FindingSeverity.Note);

            // the caches written hold the direwolf beside the game's creatures, and open it whole
            SkyrimCache caches = SkyrimCache.Load(Path.Combine(_out, "Meshes"));
            ActorProject direwolf = caches.OpenActor("MyMod_DirewolfProject")!;
            ActorProject original = SkyrimCache.Load(meshes).OpenActor("WolfProject")!;
            Assert.True(direwolf.HasHavok);
            Assert.Equal(original.Behaviors.Count, direwolf.Behaviors.Count);
            Assert.Equal(original.Clips.Count, direwolf.Clips.Count);
            Assert.Equal(original.Data.Movements!.Movements.Count, direwolf.Data.Movements!.Movements.Count);
            Assert.NotNull(caches.SetData.Project("MyMod_DirewolfProject"));
            Assert.NotNull(caches.SpeedData!.Block("MyMod_DirewolfProject"));
            Assert.Equal(430, caches.AnimationData.Projects.Count);

            using var plugin = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(_out, "MyMod.esp"), SkyrimRelease.SkyrimSE);
            IRaceGetter race = Assert.Single(plugin.Races);
            Assert.Equal("MyMod_DirewolfRace", race.EditorID);
            Assert.Equal(@"actors\MyMod_Direwolf\MyMod_DirewolfProject.hkx", race.BehaviorGraph.Male!.File.GivenPath, ignoreCase: true);
            Assert.StartsWith(@"actors\MyMod_Direwolf\", race.SkeletalModel!.Male!.File.GivenPath, StringComparison.OrdinalIgnoreCase);

            IArmorGetter skin = plugin.Armors.Single(a => a.FormKey == race.Skin.FormKey);
            Assert.Equal(race.FormKey, skin.Race.FormKey);
            Assert.All(plugin.ArmorAddons, aa => Assert.Equal(race.FormKey, aa.Race.FormKey));
            Assert.Equal(race.FormKey, Assert.Single(plugin.Npcs).Race.FormKey);
            Assert.Equal(race.SkeletalModel.Male.File.GivenPath, plugin.BodyParts.Single().Model!.File.GivenPath);
        }
    }

    /// <summary>A creature with speeds of its own.</summary>
    public sealed class CreatureSpeedCorpusTests : IDisposable
    {
        private readonly string _out = Path.Combine(Path.GetTempPath(), "skassets-creature-speeds-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
        }

        /// <summary>
        /// The wolf's root graph names two movement types; the direwolf gets its own copies, renamed
        /// in its graph, with a faster run -- and the speed table written for it sweeps to twice that
        /// run, which is the speed read from the new record through the renamed constant.
        /// </summary>
        [HavokMastersFact]
        public void ADirewolfRunsFasterThanTheWolf()
        {
            CreatureResult made;
            using (var authoring = PluginAuthoring.Open(Game.Data!, "MyMod.esp", _out))
            {
                made = authoring.ImportCreature(new NewCreature
                {
                    Template = "WolfRace", Name = "Direwolf", SourceMeshes = Game.Meshes!,
                    Speeds = new Dictionary<string, MovementSpeeds> { ["WolfDefault"] = MovementSpeeds.Uniform(90f, 800f) },
                });
                authoring.Save();
            }

            using var plugin = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(_out, "MyMod.esp"), SkyrimRelease.SkyrimSE);
            Assert.Equal(["Direwolf_WolfDefault", "Direwolf_WolfRun"], plugin.MovementTypes.Select(m => m.Name).Order());
            Assert.Equal(800f, plugin.MovementTypes.Single(m => m.Name == "Direwolf_WolfDefault").ForwardRun);

            string project = Directory.EnumerateFiles(Path.Combine(_out, "Meshes"), "DirewolfProject.hkx", SearchOption.AllDirectories).Single();
            var walk = HKSK.Behavior.ProjectWalk.Of(project);
            Assert.True(HKSK.Behavior.BehaviorRoot.Of(project) is { } root);
            var constants = HKSK.Behavior.StateConstants.Of(walk, root);
            Assert.Contains("iState_Direwolf_WolfDefault", constants.Keys);
            Assert.DoesNotContain(constants.Keys, k => k is "iState_WolfDefault" or "iState_WolfRun");

            var table = SkyrimCache.Load(Path.Combine(_out, "Meshes")).SpeedData!;
            float reach = table.Block("DirewolfProject")!.Entries.SelectMany(e => e.Records).Max(r => r.Points[^1].X);
            float wolf = table.Block("WolfProject")!.Entries.SelectMany(e => e.Records).Max(r => r.Points[^1].X);
            Assert.True(reach >= 1600f, $"the direwolf's sweep stops at {reach}");
            Assert.True(reach > wolf, $"the direwolf's sweep ({reach}) reaches no further than the wolf's ({wolf})");
            Assert.Contains(made.Notes, n => n.StartsWith("2 movement types of its own"));
        }
    }

    /// <summary>A creature with a voice of its own.</summary>
    public sealed class CreatureSoundCorpusTests : IDisposable
    {
        private readonly string _out = Path.Combine(Path.GetTempPath(), "skassets-creature-sounds-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
        }

        /// <summary>
        /// The direwolf's bark: its body's footstep set is its own, the footstep tagged with the bark
        /// event leads through its own impact set and impact to a sound naming the new files, and
        /// every other event still sounds as the wolf's.
        /// </summary>
        [HavokMastersFact]
        public void ADirewolfBarksWithItsOwnVoice()
        {
            string[] audio = [Path.Combine(_out, "in", "bark_01.wav"), Path.Combine(_out, "in", "bark_02.wav")];
            Directory.CreateDirectory(Path.Combine(_out, "in"));
            foreach (string file in audio) File.WriteAllBytes(file, "RIFF"u8.ToArray());

            // an event the body does not sound is refused before anything is written
            using (var authoring = PluginAuthoring.Open(Game.Data!, "MyMod.esp", _out))
            {
                Assert.Throws<ArgumentException>(() => authoring.ImportCreature(new NewCreature
                {
                    Template = "WolfRace", Name = "Nobark", SourceMeshes = Game.Meshes!,
                    Sounds = new Dictionary<string, IReadOnlyList<string>> { ["NoSuchEvent"] = audio },
                }));
                Assert.Empty(authoring.Plugin.EnumerateMajorRecords());
                Assert.False(Directory.Exists(Path.Combine(_out, "Meshes")));
            }

            using (var authoring = PluginAuthoring.Open(Game.Data!, "MyMod.esp", _out))
            {
                authoring.ImportCreature(new NewCreature
                {
                    Template = "WolfRace", Name = "Direwolf", SourceMeshes = Game.Meshes!,
                    Sounds = new Dictionary<string, IReadOnlyList<string>> { ["NPCWolfBark"] = audio },
                });
                authoring.Save();
            }

            using var plugin = SkyrimMod.CreateFromBinaryOverlay(Path.Combine(_out, "MyMod.esp"), SkyrimRelease.SkyrimSE);
            IFootstepSetGetter set = Assert.Single(plugin.FootstepSets);
            Assert.All(plugin.ArmorAddons, aa => Assert.Equal(set.FormKey, aa.FootstepSound.FormKey));

            IFootstepGetter bark = Assert.Single(plugin.Footsteps);
            Assert.Equal("NPCWolfBark", bark.Tag);
            Assert.Contains(set.WalkForwardFootsteps, l => l.FormKey == bark.FormKey);
            Assert.Contains(set.WalkForwardFootsteps, l => l.FormKey.ModKey.FileName == "Skyrim.esm");

            IImpactDataSetGetter impacts = Assert.Single(plugin.ImpactDataSets);
            Assert.Equal(bark.ImpactDataSet.FormKey, impacts.FormKey);
            IImpactGetter impact = Assert.Single(plugin.Impacts);
            Assert.All(impacts.Impacts, i => Assert.Equal(impact.FormKey, i.Impact.FormKey));

            ISoundDescriptorGetter sound = Assert.Single(plugin.SoundDescriptors);
            Assert.Equal(sound.FormKey, impact.Sound1.FormKey);
            Assert.Equal([@"Data\Sound\FX\Direwolf\NPCWolfBark\bark_01.wav", @"Data\Sound\FX\Direwolf\NPCWolfBark\bark_02.wav"],
                sound.SoundFiles.Select(f => f.GivenPath));
            Assert.True(File.Exists(Path.Combine(_out, "Sound", "FX", "Direwolf", "NPCWolfBark", "bark_02.wav")));
        }
    }

    /// <summary>A creature's animations from FBX, through Havok's codec.</summary>
    public sealed class CreatureClipCorpusTests : IDisposable
    {
        private readonly string _out = Path.Combine(Path.GetTempPath(), "skassets-creature-clips-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
        }

        /// <summary>
        /// A travelling wolf clip, taken to FBX against the wolf's rig and given to the direwolf: it
        /// is written into the direwolf's own animation and root motion, not the wolf's.
        /// </summary>
        [HavokMastersFact]
        public void ADirewolfTakesAnAnimationFromFbx()
        {
            string meshes = Game.Meshes!;
            string wolf = Path.Combine(meshes, "actors", "canine", "character assets wolf");
            NifXmlDatabase schema = NifXmlDatabase.LoadEmbedded();

            ActorProject original = SkyrimCache.Load(meshes).OpenActor("WolfProject")!;
            AnimationSlot walk = original.Animations.First(a => a.Motion is { Travel: > 50f });

            var rig = HkxSkeletonFile.Read(Path.Combine(wolf, "skeleton.hkx"));
            var scene = SkeletonExchange.Export(NifModel.Load(Path.Combine(wolf, "skeleton.nif"), schema), rig);
            var report = ClipExchange.AddClips(scene, rig.Rig, original, [walk]);
            Assert.Single(report.Stacks);

            string clips = Path.Combine(_out, "source", "clips.fbx");
            Directory.CreateDirectory(Path.GetDirectoryName(clips)!);
            scene.Save(clips);

            CreatureResult made;
            using (var authoring = PluginAuthoring.Open(Game.Data!, "MyMod.esp", _out))
                made = authoring.ImportCreature(new NewCreature
                {
                    Template = "WolfRace", Name = "Direwolf", SourceMeshes = meshes, Animations = [clips],
                });

            ImportedClip clip = Assert.Single(made.Clips!.Clips);
            Assert.Empty(made.Clips.Failed);
            Assert.Equal(walk.Index, clip.CacheIndex);
            Assert.StartsWith(Path.Combine(_out, "Meshes"), clip.Path);

            ActorProject direwolf = SkyrimCache.Load(Path.Combine(_out, "Meshes")).OpenActor("DirewolfProject")!;
            Assert.Equal(walk.Motion!.Travel, direwolf.Animations[walk.Index].Motion!.Travel, 0);
        }
    }

    /// <summary>A fact that needs both the game's Data folder and the extracted meshes.</summary>
    public sealed class HavokMastersFactAttribute : FactAttribute
    {
        public HavokMastersFactAttribute()
        {
            if (Game.Data is null) Skip = $"set {Game.DataVar} to the game's Data folder to run this";
            else if (Game.Meshes is null) Skip = $"set {Game.MeshesVar} to an extracted meshes folder to run this";
        }
    }
}
