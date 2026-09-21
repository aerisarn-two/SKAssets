using System.Diagnostics;
using System.Globalization;
using HKSK.Cache;
using HKSK.Model;
using HKSK.SetData;
using HKSK.Records;
using SKAssets.Content.Havok;

// setgen: writes animationsetdatasinglefile.txt from the game's other assets.
//
//   setgen <meshes> <data> [--plugin <file>]... [-o <output>] [--slack <factor>] [--force]
//
// <meshes> is the extracted meshes folder -- animationdatasinglefile.txt and the actors'
// behaviour and character files. <data> is the game's Data folder, for the five masters;
// each --plugin is read after them, in the order given, its records overriding theirs.
// A shipped animationsetdatasinglefile.txt in <meshes> is emptied before anything is built.
//
// Nothing is read from a shipped animationsetdatasinglefile.txt: the sets, and the
// moving-attack flag on their attacks, are built from the other assets alone (§4.6, §6).

string? meshes = null, data = null, output = null;
var plugins = new List<string>();
bool force = false;
double slack = SetDataGenerator.DefaultSlack;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-o" or "--output" when i + 1 < args.Length:
            output = args[++i];
            break;
        case "--slack" when i + 1 < args.Length:
            if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out slack) || !(slack >= 1))
                return Fail($"--slack wants a number of at least 1, not '{args[i]}'");
            break;
        case "--plugin" when i + 1 < args.Length:
            plugins.Add(args[++i]);
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

output = Path.GetFullPath(output ?? SkyrimCache.AnimationSetDataFileName);
if (Directory.Exists(output)) output = Path.Combine(output, SkyrimCache.AnimationSetDataFileName);

string vanilla = Path.Combine(meshes, SkyrimCache.AnimationSetDataFileName);
if (!force && string.Equals(output, vanilla, StringComparison.OrdinalIgnoreCase) && File.Exists(vanilla))
    return Fail($"{output} is the shipped file in the input folder; choose another output or pass --force");

var clock = Stopwatch.StartNew();

SkyrimCache cache = SkyrimCache.Load(meshes);
// Built from the other assets alone: whatever a shipped set data file says is dropped
// before anything can ask it.
cache.SetData.Projects.Clear();
Console.WriteLine($"cache      {meshes}");

GameEvents events = GameRecordRules.Events(GameRecordReader.Read(loadOrder));
Console.WriteLine($"plugins    {loadOrder.Count} from {data}  ({events.Idle.Count} idle events, {events.Equip.Count} equip, {events.Attacks.Count} graphs with races)");

AnimationSetDataFile file = SetDataGenerator.Generate(cache, events, slack);

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
file.Save(output);

int sets = file.Projects.Sum(p => p.Sets.Sets.Count);
int animations = file.Projects.Sum(p => p.Sets.Sets.Sum(s => s.Checksums.Entries.Count / 3));
int attacks = file.Projects.Sum(p => p.Sets.Sets.Sum(s => s.Attacks.Attacks.Count));

Console.WriteLine($"projects   {file.Projects.Count}");
Console.WriteLine($"sets       {sets}  ({animations} animations listed, {attacks} attacks)");
Console.WriteLine($"wrote      {output}  ({new FileInfo(output).Length} bytes, {clock.Elapsed.TotalSeconds:0.0}s)");
return 0;

static int Usage()
{
    Console.Error.WriteLine("usage: setgen <meshes> <data> [--plugin <file>]... [-o <output>] [--slack <factor>] [--force]");
    Console.Error.WriteLine("  <meshes>      extracted meshes folder (animationdatasinglefile.txt, behaviours)");
    Console.Error.WriteLine("  <data>        the game's Data folder (Skyrim.esm and the DLC masters)");
    Console.Error.WriteLine("  --plugin      a plugin to read after the masters, by path or by name in <data>; repeatable");
    Console.Error.WriteLine("  -o <output>   file or folder to write; default ./animationsetdatasinglefile.txt");
    Console.Error.WriteLine($"  --slack       how far a set covering several weapons may outgrow one; default {SetDataGenerator.DefaultSlack.ToString(CultureInfo.InvariantCulture)}");
    Console.Error.WriteLine("  --force       allow overwriting the shipped file in <meshes>");
    return 2;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"setgen: {message}");
    return 1;
}
