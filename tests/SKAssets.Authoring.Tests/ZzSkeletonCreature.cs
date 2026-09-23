using System.Text;
using HKSK.Assembly;
using Mutagen.Bethesda.Skyrim;
using NIFSharp;
using SKAssets.Authoring;
using SKAssets.Content.Assets;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    // The whole chain for a bought creature, into one mod folder: the rig corrected and written
    // both ways, the ragdoll fitted, the clips cut and measured, the project assembled, the
    // caches amended, the body and its textures converted, and the plugin written.
    //
    //   SKELETON_FBX   the bought rig and body
    //   SKELETON_CLIPS the merged take
    //   SKELETON_CUTS  the clip table, name/start/end, tab separated
    //   SKELETON_TEX   the folder holding its BaseColor and its packed surface map
    //   SKELETON_MOD   where to build, which is a Data folder
    public sealed class ZzSkeletonCreature
    {
        [MastersFact]
        public void Import()
        {
            string fbx = Environment.GetEnvironmentVariable("SKELETON_FBX") ?? "";
            string clips = Environment.GetEnvironmentVariable("SKELETON_CLIPS") ?? "";
            string cuts = Environment.GetEnvironmentVariable("SKELETON_CUTS") ?? "";
            string mod = Environment.GetEnvironmentVariable("SKELETON_MOD") ?? "";
            if (!File.Exists(fbx) || !File.Exists(clips) || !File.Exists(cuts) || mod.Length == 0) return;

            string textures = Environment.GetEnvironmentVariable("SKELETON_TEX") ?? "";
            string corpus = Game.Meshes ?? "";
            const string Name = "BoneSkeleton";

            var sb = new StringBuilder();
            var db = NifXmlDatabase.LoadEmbedded();

            string meshes = Path.Combine(mod, "Meshes");
            string folder = Path.Combine("actors", Name);
            string here = Path.Combine(meshes, folder);
            Directory.CreateDirectory(here);

            // ---- the rig, corrected once and used by everything after
            CorrectedSkeleton rig = BoughtSkeleton.Correct(fbx, db);
            sb.AppendLine($"rig: {rig}");

            string nif = Path.Combine(here, "character assets", "skeleton.nif");
            Directory.CreateDirectory(Path.GetDirectoryName(nif)!);
            rig.Mesh.Save(nif);

            // ---- the Havok skeleton, with a bone creature's ragdoll fitted to it
            string hkx = Path.Combine(here, "character assets", "skeleton.hkx");
            AssembledSkeleton made = CreatureSkeleton.Build(
                fbx,
                Path.Combine(corpus, "actors", "draugr", "character assets", "skeletons.hkx"),
                hkx,
                feet: [("Bip01_L_Foot", "Bip01_L_Toe0"), ("Bip01_R_Foot", "Bip01_R_Toe0")]);

            sb.AppendLine($"skeleton: {made}");

            foreach (MeshFinding f in CreatureChecks.Skeleton(NifModel.Load(nif, db), HKFBX.Hkx.HkxSkeletonFile.Read(hkx)))
                sb.AppendLine($"  {f.Severity} {f.Rule}: {f.Message}");

            // ---- the clips, cut the way their table means them and measured off their feet
            var table = File.ReadAllLines(cuts)
                .Select(l => l.Split('\t')).Where(f => f.Length >= 3)
                .Select(f => new HKFBX.Fbx.TakeCut(f[0].Trim(), int.Parse(f[1]), int.Parse(f[2])))
                .ToList();

            var document = LeanMeshIO.FbxDocument.Load(clips);
            HKFBX.Fbx.CutReport cut = HKFBX.Fbx.FbxTakeCutter.Cut(document, table, rig: HKFBX.Fbx.FbxAnimationReader.ReadSkeleton(document));
            string recut = Path.Combine(mod, "recut.fbx");
            document.Save(recut);
            sb.AppendLine($"clips: {cut.Cuts.Count} cut, end frame kept = {cut.EndIsInside}");

            string template = Directory.GetFiles(Path.Combine(corpus, "actors", "draugr", "animations"), "*.hkx").Order().First();

            WrittenClips written = CreatureClips.Write(
                recut, template, Path.Combine(here, "animations"), made.Renamed.Rig, made.Names,
                takes: [.. table.Select(c => c.Name)],
                turnDegrees: made.Grounded?.Turn ?? 0f);

            RoledClips read = ClipRoles.Of(written);
            sb.AppendLine($"roles: {read}");
            foreach (string why in read.Disagreements) sb.AppendLine($"  !! {why}");
            foreach (string why in read.Unmeasured) sb.AppendLine($"  ?? {why}");

            // ---- the project, assembled from the clips that read as something
            var spec = new CreatureSpec(
                Name, hkx,
                [.. read.Clips.Where(c => c.Animation.Roles.Count > 0).Select(c => c.Animation)],
                MovementTypeName: Name + "Default",
                ClipDurations: read.Durations);

            AssemblyResult project = CreatureAssembler.Assemble(spec, here);
            sb.AppendLine($"project: {project.Files.Count} files, {project.Plan.Locomotion}, movement {project.Movement}");
            foreach (string note in project.Notes) sb.AppendLine($"  {note}");

            // ---- the caches, into this mod's own copies of the game's three
            foreach (string cache in new[] { "animationdatasinglefile.txt", "animationsetdatasinglefile.txt", "speeddatasinglefile.txt" })
            {
                string from = Path.Combine(corpus, cache), to = Path.Combine(meshes, cache);
                if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
            }

            InstallReport installed = CreatureInstaller.Install(project, meshes);
            sb.AppendLine($"caches: added={installed.Added} speed={installed.SpeedTable} sets={installed.SetData}");
            foreach (string note in installed.Notes) sb.AppendLine($"  {note}");

            // ---- the body and its textures
            NifModel body = BoughtSkeleton.Body(fbx, db, made.Names);
            string bodyPath = Path.Combine(here, Name.ToLowerInvariant() + ".nif");
            body.Save(bodyPath);
            sb.AppendLine($"body: {bodyPath}");

            foreach (MeshFinding f in CreatureChecks.Skin(NifModel.Load(bodyPath, db), NifModel.Load(nif, db)))
                sb.AppendLine($"  {f.Severity} {f.Rule}: {f.Message}");

            if (Directory.Exists(textures))
            {
                string? colour = Directory.GetFiles(textures, "*BaseColor*").FirstOrDefault();
                string? surface = Directory.GetFiles(textures, "*Occlusion*").FirstOrDefault();
                if (colour is not null)
                {
                    BoughtTextureSet set = BoughtTextures.Convert(
                        colour, Path.Combine(mod, "Textures", "actors", Name), Name, surface);
                    sb.AppendLine($"textures: {set}");
                    foreach (string note in set.Notes) sb.AppendLine($"  {note}");
                }
            }

            // ---- the plugin
            SKAssets.Plugins.MutagenRuntime.Prepare();
            var order = SKAssets.Content.Havok.GameRecordReader.Masters
                .Select(f => Path.Combine(Game.Data!, f)).Where(File.Exists)
                .Select(f => (ISkyrimModGetter)SkyrimMod.CreateFromBinaryOverlay(f, SkyrimRelease.SkyrimSE))
                .ToList();

            using (var authoring = new PluginAuthoring(order, Name + ".esp", mod))
            {
                CreatureResult plugin = authoring.ImportAssembledCreature(new AssembledCreature(
                    Template: "SkeletonRace",
                    Name: Name,
                    ProjectFolder: folder.Replace('/', '\\'),
                    ProjectFile: Name + "Project.hkx",
                    BehaviourFile: Path.Combine("behaviors", Name + "Behavior.hkx").Replace('/', '\\'),
                    TemplateFolder: @"actors\draugr",
                    SkeletonModel: Path.Combine(folder, "character assets", "skeleton.nif").Replace('/', '\\'),
                    Movement: (Name + "Default", Speeds(project.Movement)),
                    Attacks: [.. project.Plan.Attacks],
                    DisplayName: "Bone Skeleton"));

                sb.AppendLine($"plugin: {plugin.Records.Count} records");
                foreach (var group in plugin.Records.GroupBy(r => r.Type).OrderByDescending(g => g.Count()))
                    sb.AppendLine($"  {group.Key} x{group.Count()}");
                foreach (string note in plugin.Notes) sb.AppendLine($"  {note}");

                authoring.Save();
            }

            // The cut-up take was a step, not a product: it is twenty megabytes of FBX and
            // nothing in the mod reads it.
            File.Delete(recut);

            sb.AppendLine();
            sb.AppendLine("--- what was written");
            foreach (string path in Directory.GetFiles(mod, "*", SearchOption.AllDirectories).Order())
                sb.AppendLine($"  {Path.GetRelativePath(mod, path),-70} {new FileInfo(path).Length,10:N0}");

            File.WriteAllText(Path.Combine(mod, "build.txt"), sb.ToString());
            Console.WriteLine(sb.ToString());
        }

        private static MovementSpeeds Speeds(HKSK.Speed.MovementType m) => new(
            m.ForwardWalk, m.ForwardRun, m.BackWalk, m.BackRun,
            m.LeftWalk, m.LeftRun, m.RightWalk, m.RightRun);
    }
}
