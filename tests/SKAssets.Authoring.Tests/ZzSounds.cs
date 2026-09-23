using System.Text;
using HKSK.Havok;
using HKX2;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Every sound a project's graphs and animations ask for, and what record answers.
public sealed class ZzSounds
{
    [MastersFact]
    public void Dump()
    {
        string outDir = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        string folder = Environment.GetEnvironmentVariable("HKX_FOLDER") ?? "";
        if (outDir.Length == 0 || !Directory.Exists(folder)) return;
        SKAssets.Plugins.MutagenRuntime.Prepare();

        var asked = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories))
        {
            HavokFile file;
            try { file = HavokFile.Load(path); } catch { continue; }

            void Take(string text)
            {
                int at = text.IndexOf("SoundPlay.", StringComparison.OrdinalIgnoreCase);
                if (at >= 0) asked.Add(text[(at + "SoundPlay.".Length)..].Trim());
            }

            foreach (hkbBehaviorGraphStringData strings in file.All<hkbBehaviorGraphStringData>())
                foreach (string name in strings.m_eventNames) Take(name);
            foreach (hkaAnnotationTrack track in file.All<hkaAnnotationTrack>())
                foreach (var a in track.m_annotations) Take(a.m_text ?? "");
        }

        var order = SKAssets.Content.Havok.GameRecordReader.Masters.Select(f => Path.Combine(Game.Data!, f)).Where(File.Exists)
            .Select(f => (ISkyrimModGetter)SkyrimMod.CreateFromBinaryOverlay(f, SkyrimRelease.SkyrimSE)).ToList();
        var cache = order.ToImmutableLinkCache();
        var sb = new StringBuilder();

        foreach (string name in asked)
        {
            var found = cache.TryResolve<ISkyrimMajorRecordGetter>(name, out var record) ? record : null;
            sb.AppendLine($"{name}: {(found is null ? "nothing in the load order" : found.GetType().Name)}");

            if (found is ISoundDescriptorGetter descriptor)
            {
                sb.AppendLine($"   category={descriptor.Category.FormKey} output={descriptor.OutputModel.FormKey} "
                    + $"alternate={descriptor.AlternateSoundFor.FormKey} files={descriptor.SoundFiles?.Count}");
                foreach (var f in descriptor.SoundFiles ?? []) sb.AppendLine($"   file {f}");
            }
            else if (found is ISoundMarkerGetter marker)
                sb.AppendLine($"   descriptor={marker.SoundDescriptor.FormKey}");
        }

        File.WriteAllText(Path.Combine(outDir, "sounds.txt"), sb.ToString());
    }
}
