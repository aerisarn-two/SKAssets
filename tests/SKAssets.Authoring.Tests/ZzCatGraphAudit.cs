using System.Text;
using HKSK.Behavior;
using HKSK.Havok;
using HKX2;
using Xunit;
namespace SKAssets.Authoring.Tests;

// Everything a loader walks from the race's behaviour graph: the project, its
// characters, the behaviours they name, and whether each file and index resolves.
public sealed class ZzCatGraphAudit
{
    [Fact]
    public void Audit()
    {
        string mod = Environment.GetEnvironmentVariable("CAT_MOD") ?? "";
        if (mod.Length == 0) return;
        string folder = Path.Combine(mod, "Data", "Meshes", "actors", "HouseCat");
        if (!Directory.Exists(folder)) return;
        var sb = new StringBuilder();

        string? Resolve(string relative)
        {
            string direct = Path.Combine(folder, relative.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(direct)) return direct;
            return Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetRelativePath(folder, f).Replace('/', '\\').Equals(relative.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase));
        }

        string projectPath = Directory.GetFiles(folder, "*.hkx").First(f => f.Contains("Project", StringComparison.OrdinalIgnoreCase));
        var project = ProjectFile.Load(projectPath);
        sb.AppendLine($"project {Path.GetFileName(projectPath)}: characters=[{string.Join(", ", project.CharacterFiles)}] worldUp={project.Data.m_worldUpWS}");

        foreach (string character in project.CharacterFiles)
        {
            string? path = Resolve(character);
            sb.AppendLine($"  character '{character}' -> {(path is null ? "MISSING" : Path.GetRelativePath(folder, path))}");
            if (path is null) continue;

            CharacterFile file = CharacterFile.Load(path);
            sb.AppendLine($"    name={file.Name} rig={file.RigName} ragdoll={file.RagdollName} behavior={file.BehaviorFilename} animations={file.AnimationNames.Count}");
            foreach (string named in new[] { file.RigName, file.RagdollName, file.BehaviorFilename })
                sb.AppendLine($"    names '{named}' -> {(Resolve(named) is { } p ? Path.GetRelativePath(folder, p) : "MISSING")}");

            int missingAnimations = file.AnimationNames.Count(a => !a.StartsWith(@"..\", StringComparison.Ordinal) && Resolve(a) is null);
            sb.AppendLine($"    animations missing on disk: {missingAnimations}");
        }

        foreach (string path in Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories).Where(f => f.Contains("ehaviors", StringComparison.Ordinal)).Order())
        {
            HavokFile file;
            try { file = HavokFile.Load(path); }
            catch (Exception e) { sb.AppendLine($"  BEHAVIOUR {Path.GetFileName(path)}: CANNOT LOAD: {e.Message}"); continue; }

            var editor = new GraphEditor(file);
            var reached = editor.Reachable().ToList();
            sb.AppendLine($"  behaviour {Path.GetFileName(path)}: graph '{editor.Graph.m_name}' root={editor.Graph.m_rootGenerator?.GetType().Name} data={(editor.Graph.m_data is null ? "NONE" : "yes")} names={editor.Strings.m_eventNames.Count}/{editor.Data.m_eventInfos.Count} events, {editor.Strings.m_variableNames.Count}/{editor.Data.m_variableInfos.Count}/{editor.Data.m_variableInitialValues?.m_wordVariableValues.Count} variables, {reached.Count} objects");

            foreach (var reference in reached.OfType<hkbBehaviorReferenceGenerator>())
                sb.AppendLine($"    references '{reference.m_behaviorName}' -> {(Resolve(reference.m_behaviorName) is { } p ? Path.GetRelativePath(folder, p) : "MISSING")}");

            foreach (var machine in reached.OfType<hkbStateMachine>())
            {
                var ids = machine.m_states.Select(s => s.m_stateId).ToHashSet();
                var problems = new List<string>();
                if (machine.m_states.Count == 0) problems.Add("no states");
                else if (!ids.Contains(machine.m_startStateId)) problems.Add($"start state {machine.m_startStateId} is not one of [{string.Join(",", ids)}]");
                if (machine.m_states.Any(s => s.m_generator is null)) problems.Add("a state with no generator");
                if (machine.m_states.Count != ids.Count) problems.Add("two states share an id");

                foreach (var (transition, where) in machine.m_states.SelectMany(s => (s.m_transitions?.m_transitions ?? []).Select(t => (t, s.m_name)))
                         .Concat((machine.m_wildcardTransitions?.m_transitions ?? []).Select(t => (t, "wildcard"))))
                {
                    if (!ids.Contains(transition.m_toStateId)) problems.Add($"{where}: a transition goes to state {transition.m_toStateId}, which does not exist");
                    if (transition.m_eventId < 0 || transition.m_eventId >= editor.Strings.m_eventNames.Count) problems.Add($"{where}: a transition fires event {transition.m_eventId}, past the {editor.Strings.m_eventNames.Count} declared");
                    if ((transition.m_flags & 0x2000) != 0 && machine.m_states.FirstOrDefault(s => s.m_stateId == transition.m_toStateId)?.m_generator is hkbStateMachine nested
                        && !nested.m_states.Any(s => s.m_stateId == transition.m_toNestedStateId))
                        problems.Add($"{where}: a transition enters nested state {transition.m_toNestedStateId} of '{nested.m_name}', which has none");
                }

                if (problems.Count > 0) sb.AppendLine($"    PROBLEM machine '{machine.m_name}': {string.Join("; ", problems)}");
            }

            foreach (var clip in reached.OfType<hkbClipGenerator>())
            {
                foreach (var trigger in clip.m_triggers?.m_triggers ?? [])
                    if (trigger.m_event.m_id < 0 || trigger.m_event.m_id >= editor.Strings.m_eventNames.Count)
                        sb.AppendLine($"    PROBLEM clip '{clip.m_name}' fires event {trigger.m_event.m_id}, past the {editor.Strings.m_eventNames.Count} declared");
            }

            foreach (var binding in reached.OfType<hkbVariableBindingSet>().SelectMany(b => b.m_bindings))
                if (binding.m_bindingType == 0 && (binding.m_variableIndex < 0 || binding.m_variableIndex >= editor.Strings.m_variableNames.Count))
                    sb.AppendLine($"    PROBLEM a binding on '{binding.m_memberPath}' names variable {binding.m_variableIndex}, past the {editor.Strings.m_variableNames.Count} declared");
        }

        var cache = HKSK.Model.SkyrimCache.Load(Path.Combine(mod, "Data", "Meshes"));
        sb.AppendLine($"caches: {cache.AnimationData.Projects.Count} projects, set data {(cache.SetData is null ? "none" : cache.SetData.Projects.Count + " projects")}, speed {(cache.SpeedData is null ? "none" : cache.SpeedData.Blocks.Count + " blocks")}");
        var entry = cache.AnimationData.Project("HouseCatProject");
        sb.AppendLine($"entry: {(entry is null ? "MISSING" : $"name={entry.Name} hasFiles={entry.Block.HasFiles} hasCache={entry.Block.HasAnimationCache} files={entry.Block.Files.Count} clips={entry.Block.Clips.Count} motions={entry.Movements?.Movements.Count}")}");
        foreach (string file in entry?.Block.Files ?? []) sb.AppendLine($"  file '{file}'");
        sb.AppendLine($"set data for it: {(cache.SetData?.Project("HouseCatProject") is { } set ? set.Sets.Sets.Count + " sets" : "MISSING")}");
        sb.AppendLine($"speed block: {(cache.SpeedData?.Block("HouseCatProject") is { } speed ? speed.ToString() : "MISSING")}");
        File.WriteAllText(Path.Combine(mod, "graph_audit.txt"), sb.ToString());
    }
}
