using System.Diagnostics;
using System.Globalization;
using HKSK.Model;
using HKSK.Records;
using HKSK.SetData;
using HKSK.Speed;
using SKAssets.Content.Havok;

// animgen: brings the three animation caches up to date with the game's other assets.
//
//   animgen <meshes> <data> [--plugin <file>]... [--project <name>]... [-o <folder>]
//           [--slack <factor>] [--tolerance <units>] [--force]
//
// <meshes> is the extracted meshes folder: the three merged caches and the Havok files
// beside them. <data> is the game's Data folder, for the five masters; each --plugin is
// read after them, in the order given, its records overriding theirs.
//
// With --project, only the projects named change: each is brought up to date in all
// three files -- added where they do not list it yet, as an actor when a race wears it --
// and every other project's entries are written back as they were read. Without it,
// every project is: the animation data amended entry by entry, since its root motion and
// event lists are in no Havok file, and the set data and speed table generated whole.

string? meshes = null, data = null, output = null;
var plugins = new List<string>();
var projects = new List<string>();
var options = new CacheGenerationOptions();
bool force = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-o" or "--output" when i + 1 < args.Length:
            output = args[++i];
            break;
        case "--plugin" when i + 1 < args.Length:
            plugins.Add(args[++i]);
            break;
        case "--project" when i + 1 < args.Length:
            projects.Add(args[++i]);
            break;
        case "--slack" when i + 1 < args.Length:
            if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double slack) || !(slack >= 1))
                return Fail($"--slack wants a number of at least 1, not '{args[i]}'");
            options = options with { Slack = slack };
            break;
        case "--tolerance" when i + 1 < args.Length:
            if (!float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out float tolerance) || !(tolerance > 0f))
                return Fail($"--tolerance wants a positive number, not '{args[i]}'");
            options = options with { Tolerance = tolerance };
            break;
        case "--force":
            force = true;
            break;
        case "-h" or "--help":
            return Usage();
        default:
            if (args[i].StartsWith('-')) return Fail($"unknown option '{args[i]}'");
            if (meshes is null) meshes = args[i];
            else if (data is null) data = args[i];
            else return Fail($"unexpected argument '{args[i]}'");
            break;
    }
}

if (meshes is null || data is null) return Usage();

meshes = Path.GetFullPath(meshes);
data = Path.GetFullPath(data);

if (!File.Exists(Path.Combine(meshes, SkyrimCache.AnimationDataFileName)))
    return Fail($"{meshes} has no {SkyrimCache.AnimationDataFileName}: pass the extracted meshes folder");
if (!File.Exists(Path.Combine(data, GameRecordReader.Masters[0])))
    return Fail($"{data} has no {GameRecordReader.Masters[0]}: pass the game's Data folder");
foreach (string plugin in plugins)
    if (!File.Exists(plugin) && !File.Exists(Path.Combine(data, plugin)))
        return Fail($"no plugin '{plugin}', neither as a path nor in {data}");

// The masters present, then the plugins asked for: a path, or a name in the Data folder.
List<string> loadOrder =
[
    .. GameRecordReader.Masters.Select(m => Path.Combine(data, m)).Where(File.Exists),
    .. plugins.Select(p => File.Exists(p) ? Path.GetFullPath(p) : Path.Combine(data, p)),
];

// Three files, so the output is a folder; writing into the input one replaces the game's.
output = Path.GetFullPath(output ?? ".");
if (!force && string.Equals(output.TrimEnd(Path.DirectorySeparatorChar), meshes.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
    return Fail($"{output} is the input folder, whose caches this would replace; choose another output or pass --force");

var clock = Stopwatch.StartNew();

SkyrimCache cache = SkyrimCache.Load(meshes);
Console.WriteLine($"cache      {meshes}  ({cache.AnimationData.Projects.Count} projects)");

GameRecords records = GameRecordReader.Read(loadOrder);
Console.WriteLine($"plugins    {loadOrder.Count} from {data}  ({records.Races.Count} races, {records.MovementTypes.Count} movement types, {records.Idles.Count} idles)");

if (projects.Count > 0)
{
    foreach (string project in projects)
    {
        CacheAmendment done;
        try { done = CacheGeneration.Amend(cache, project, records, options); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { return Fail(e.Message); }

        Console.WriteLine($"amended    {done.Project}  animation data {Say(done.AnimationData)}, set data {Say(done.SetData)}, speed data {Say(done.SpeedData)}");
    }
}
else
{
    IReadOnlyList<string> changed = CacheGeneration.Regenerate(cache, records, options);
    Console.WriteLine($"amended    animation data: {(changed.Count == 0 ? "no entry changed" : $"{changed.Count} changed: {string.Join(", ", changed)}")}");
    Console.WriteLine($"generated  set data ({cache.SetData.Projects.Count} projects), speed data ({cache.SpeedData?.Projects.Count ?? 0} projects)");
}

cache.Save(output);

foreach (string file in new[] { SkyrimCache.AnimationDataFileName, SkyrimCache.AnimationSetDataFileName, SkyrimCache.SpeedDataFileName })
    if (File.Exists(Path.Combine(output, file)))
        Console.WriteLine($"wrote      {Path.Combine(output, file)}  ({new FileInfo(Path.Combine(output, file)).Length} bytes)");

Console.WriteLine($"done       {clock.Elapsed.TotalSeconds:0.0}s");
return 0;

static string Say(Amendment amendment) => amendment switch
{
    Amendment.None => "no entry, none needed",
    _ => amendment.ToString().ToLowerInvariant(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: animgen <meshes> <data> [--plugin <file>]... [--project <name>]... [-o <folder>]");
    Console.Error.WriteLine("               [--slack <factor>] [--tolerance <units>] [--force]");
    Console.Error.WriteLine("  <meshes>      extracted meshes folder (the three caches, the Havok files)");
    Console.Error.WriteLine("  <data>        the game's Data folder (Skyrim.esm and the DLC masters)");
    Console.Error.WriteLine("  --plugin      a plugin to read after the masters, by path or by name in <data>; repeatable");
    Console.Error.WriteLine("  --project     amend only this project, in all three files; repeatable. Without it, every project");
    Console.Error.WriteLine("  -o <folder>   the folder to write the three files to; default the current one");
    Console.Error.WriteLine($"  --slack       how far a set covering several weapons may outgrow one; default {SetDataGenerator.DefaultSlack.ToString(CultureInfo.InvariantCulture)}");
    Console.Error.WriteLine($"  --tolerance   how far a dropped speed point may sit from its line; default {SpeedDataGenerator.DefaultTolerance.ToString(CultureInfo.InvariantCulture)}");
    Console.Error.WriteLine("  --force       allow writing over the caches in <meshes>");
    return 2;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"animgen: {message}");
    return 1;
}
