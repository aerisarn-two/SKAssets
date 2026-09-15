using HKFBX.Codec;
using HKFBX.Fbx;
using HKFBX.Hkx;
using HKFBX.Model;
using HKSK.Fbx;
using HKSK.Model;
using LeanMeshIO;
using NIFBX.Conversion;

namespace SKAssets.Export
{
    /// <summary>One clip, as it ended up in the scene.</summary>
    /// <param name="Name">The stack's name, which is the animation's file stem.</param>
    /// <param name="StoredName">The animation as the project stores it.</param>
    /// <param name="CacheIndex">Its position in the character's animation list.</param>
    /// <param name="BoundBones">
    /// How many of the rig's bones the stack drives. **Zero is an inert stack** — the
    /// scene does not hold the bones this clip animates — and is the one number worth
    /// checking after an export.
    /// </param>
    /// <param name="Frames">Frames the clip was sampled to.</param>
    /// <param name="Duration">Seconds it runs for.</param>
    /// <param name="Travels">Whether the cache records it as going anywhere.</param>
    public sealed record ClipStack(
        string Name, string StoredName, int CacheIndex,
        int BoundBones, int Frames, float Duration, bool Travels)
    {
        public override string ToString() =>
            $"{Name}: {BoundBones} bones, {Frames} frames, {Duration:F2}s{(Travels ? ", travels" : "")}";
    }

    /// <summary>What went into the scene, and what would not.</summary>
    /// <param name="Stacks">Animations added, one stack each.</param>
    /// <param name="Missing">
    /// Slots whose animation file the project names and the folder does not have.
    /// </param>
    /// <param name="Unreadable">
    /// Slots whose file is there and could not be decoded, with the reason.
    /// </param>
    public sealed record ClipReport(
        IReadOnlyList<ClipStack> Stacks,
        IReadOnlyList<string> Missing,
        IReadOnlyDictionary<string, string> Unreadable)
    {
        /// <summary>Stacks that drive nothing, which is an export that looks fine and is not.</summary>
        public IReadOnlyList<ClipStack> Inert => [.. Stacks.Where(s => s.BoundBones == 0)];

        public override string ToString() =>
            $"{Stacks.Count} stacks, {Missing.Count} missing, {Unreadable.Count} unreadable"
            + (Inert.Count > 0 ? $", {Inert.Count} inert" : "");
    }

    /// <summary>One clip, as it came back out of the scene.</summary>
    /// <param name="Stack">The stack it was read from.</param>
    /// <param name="StoredName">The animation as the project stores it.</param>
    /// <param name="CacheIndex">Its position in the character's animation list.</param>
    /// <param name="Path">The packfile written, as the project names it.</param>
    public sealed record ImportedClip(string Stack, string StoredName, int CacheIndex, string? Path)
    {
        public override string ToString() => $"{Stack} -> {StoredName}";
    }

    /// <summary>What came back out of the scene, and what did not.</summary>
    /// <param name="Clips">Stacks written back, one animation each.</param>
    /// <param name="Ignored">
    /// Stacks the export did not put there. A creature's skeleton.nif can carry
    /// animation of its own, which NIFBX writes as a stack like any other, and
    /// writing that into the project as a clip would invent an animation the game
    /// never had.
    /// </param>
    /// <param name="Failed">Stacks that could not be written back, with the reason.</param>
    public sealed record ImportReport(
        IReadOnlyList<ImportedClip> Clips,
        IReadOnlyList<string> Ignored,
        IReadOnlyDictionary<string, string> Failed)
    {
        public override string ToString() =>
            $"{Clips.Count} clips, {Ignored.Count} ignored"
            + (Failed.Count > 0 ? $", {Failed.Count} failed" : "");
    }

    /// <summary>
    /// A creature's clips, into the scene its skeleton is already in.
    /// </summary>
    /// <remarks>
    /// The game keeps an actor's animations as one packfile per clip over a rig held
    /// somewhere else — 5,120 of them across the 46 actors that have a skeleton, from
    /// the witchlight's 7 to the player's 1,859. Exported one file at a time that is
    /// 5,120 FBXs each carrying its own copy of a skeleton, and an animator who wants
    /// to see a walk next to a run has to load two scenes.
    ///
    /// So the skeleton goes in once, from <see cref="SkeletonExchange"/>, and each
    /// clip becomes an animation stack over it. Which clips is the caller's decision
    /// and deliberately so: for a hare, all 18 is obviously right, and for the player
    /// 1,859 stacks in one file is a decision somebody should make on purpose.
    ///
    /// Two things come from the cache rather than from the animation file, and both
    /// would be silently lost by reading the packfile alone:
    ///
    /// <list type="bullet">
    /// <item><b>Root motion.</b> Havok has a place for it in the animation —
    /// <c>hkaAnimation.m_extractedMotion</c> — and Skyrim does not use it. It is null
    /// on every clip in the game, measured over a sample of the player's; the travel
    /// is recorded in the animation cache instead. A clip exported without it walks
    /// on the spot.</item>
    /// <item><b>Which clip generators play the animation.</b> A slot is what the
    /// cache indexes and a clip is what the behaviour graph asks for, and the two are
    /// not one to one: several clips can play one animation.</item>
    /// </list>
    /// </remarks>
    public static class ClipExchange
    {
        /// <summary>The animation as the project stores it, on the stack.</summary>
        public const string StoredNameProperty = "sk_clip_animation";

        /// <summary>Its position in the character's animation list.</summary>
        public const string CacheIndexProperty = "sk_clip_index";

        /// <summary>
        /// The clip generators that play it, tab separated, or absent where none do.
        /// </summary>
        public const string GeneratorsProperty = "sk_clip_generators";

        /// <summary>The same three facts about every clip, written on a node.</summary>
        /// <remarks>
        /// A second copy, and the only one that survives a DCC tool. Blender reads an
        /// animation stack as an action and keeps its curves and its name; the stack's
        /// own properties are not part of an action and are dropped, and the stacks it
        /// writes on the way out are new ones it named itself. So a creature that has
        /// been through Blender comes back with 216 stacks and not one of them says
        /// which animation it is -- the clips are all there and all anonymous.
        ///
        /// Node properties do survive: <see cref="SkeletonExchange.RigBonesProperty"/>
        /// is 1,670 bytes of bone names on the rig's root node and comes back byte for
        /// byte. So the same three facts ride there as well, keyed by the name of the
        /// stack they were written for, and a stack that has lost its properties is
        /// looked up by name instead.
        ///
        /// One row per clip, fields tab separated: the stack's name, the stored name,
        /// the cache index, and then a field per generator.
        /// </remarks>
        public const string ManifestProperty = "sk_clips";

        private const char Separator = '\t';

        private const char RowSeparator = '\n';

        /// <summary>
        /// Adds one animation stack per slot to a scene that already holds the rig.
        /// </summary>
        /// <param name="document">
        /// A scene from <see cref="SkeletonExchange.Export"/>, modified in place. Its
        /// bone nodes are what the curves are bound to, so a scene without them gets
        /// stacks that drive nothing — which the report says, by binding nothing.
        /// </param>
        /// <param name="rig">
        /// The skeleton the clips are authored against, which is the animation rig
        /// out of the creature's <c>skeleton.hkx</c> and not its ragdoll. Bone order
        /// is what a clip's track bindings index, so the wrong skeleton with the
        /// right names puts every curve on the wrong bone.
        /// </param>
        /// <param name="project">
        /// The creature's Havok project, for the cache's root motion and for which
        /// clip generators play what.
        /// </param>
        /// <param name="slots">
        /// The animations to add. Null takes every slot the project has, which is
        /// right for a creature and a decision worth making deliberately for the
        /// player.
        /// </param>
        /// <param name="codec">
        /// What decompresses the spline curves. Null builds the default, which runs
        /// Havok's own codec through mopper.
        /// </param>
        public static ClipReport AddClips(
            FbxDocument document,
            Skeleton rig,
            ActorProject project,
            IEnumerable<AnimationSlot>? slots = null,
            IAnimationCodec? codec = null)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(rig);
            ArgumentNullException.ThrowIfNull(project);

            codec ??= new MopperAnimationCodec();

            IReadOnlyDictionary<string, string> nodeNames = NodeNames(document, rig);

            var added = new List<ClipStack>();
            var missing = new List<string>();
            var unreadable = new Dictionary<string, string>(StringComparer.Ordinal);
            // Seeded with the stacks the scene already has, because it may already
            // have some: an actor's skeleton.nif can carry its own bone animation
            // with no sequence and no controller manager around it, and NIFBX writes
            // that as a stack of its own. Seven of the game's 49 actors are like
            // that -- the deer's mesh holds 39 NiTransformControllers, 113 curve
            // nodes' worth. A clip that collided with it would be a second stack of
            // one name, which is the thing this set exists to prevent.
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (FbxObject stack in new FbxScene(document).OfClass("AnimationStack"))
                taken.Add(stack.Name);

            foreach (AnimationSlot slot in slots ?? project.Animations)
            {
                if (project.AnimationPath(slot) is not { } path || !File.Exists(path))
                {
                    missing.Add(slot.StoredName);
                    continue;
                }

                SampledAnimation clip;

                try
                {
                    (SplineAnimationData spline, IReadOnlyList<short> trackToBone, _,
                     IReadOnlyList<AnnotationTrack> annotations) = HkxAnimationFile.ReadAnimationWithEvents(path);

                    clip = codec.Decompress(spline) with
                    {
                        TrackToBone = trackToBone,
                        Annotations = annotations,
                        RootMotion = slot.Motion is { } movement
                            ? Conversions.ToFbx(movement)
                            : RootMotion.None,
                    };
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // One unreadable clip is not a reason to lose the other 1,858.
                    unreadable[slot.StoredName] = e.Message;
                    continue;
                }

                string name = Unique(slot.FileStem, taken);

                int bound = FbxAnimationWriter.AddStack(document, rig, clip, name, nodeNames);
                Describe(document, name, slot, project);

                added.Add(new ClipStack(
                    name, slot.StoredName, slot.Index, bound, clip.FrameCount, clip.Duration, slot.Travels));
            }

            WriteManifest(document, added, project);

            return new ClipReport(added, missing, unreadable);
        }

        /// <summary>
        /// The other direction: every stack the export put in the scene, back into
        /// the project it came from.
        /// </summary>
        /// <remarks>
        /// A creature leaves as one file and has to come back as many. The game
        /// keeps an animation as its own packfile and records what it does in the
        /// cache, so writing a creature back means one packfile per stack plus the
        /// cache entries that address them -- the slot, its root motion, and the
        /// clip generators that play it.
        ///
        /// Which stack is which is not guessed. <see cref="AddClips"/> writes the
        /// stored name, the cache index and the generators onto the stack itself,
        /// and this reads them back: a stack without a stored name is not something
        /// this library put there and is left alone. That matters -- seven of the
        /// game's 49 actors carry animation in their skeleton.nif, which NIFBX
        /// writes as a stack like any other, and importing it as a clip would
        /// invent an animation the creature never had.
        ///
        /// <b>Nothing is saved here but the packfiles.</b> The cache and the
        /// character's animation list are edited in memory, the way HKSK does it,
        /// so a half-finished import cannot leave half a cache on disk. The caller
        /// saves: <c>cache.Save()</c>, and <c>project.SaveCharacter()</c> when a
        /// slot was added.
        /// </remarks>
        /// <param name="document">
        /// A scene from <see cref="AddClips"/>, or one a DCC tool has handed back.
        /// </param>
        /// <param name="project">
        /// The creature's Havok project, opened through a cache that can find its
        /// files -- there is nowhere to write an animation otherwise.
        /// </param>
        /// <param name="codec">
        /// What compresses the curves. Null builds the default, which runs Havok's
        /// own codec through mopper.
        /// </param>
        /// <param name="overwrite">
        /// Whether an animation packfile already on disk may be replaced. False is
        /// for importing into a project you do not want to touch.
        /// </param>
        public static ImportReport ImportClips(
            FbxDocument document,
            ActorProject project,
            IAnimationCodec? codec = null,
            bool overwrite = true)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(project);

            var exchange = codec is null ? new AnimationExchange() : new AnimationExchange(codec);

            var clips = new List<ImportedClip>();
            var ignored = new List<string>();
            var failed = new Dictionary<string, string>(StringComparer.Ordinal);

            IReadOnlyDictionary<string, ClipRecord> manifest = Manifest(document);

            foreach (FbxObject stack in new FbxScene(document).OfClass("AnimationStack").ToList())
            {
                // The stack's own properties first, because they are the ones written
                // for this stack. The manifest is the fallback, and it is what answers
                // for a scene that has been through a DCC tool: the properties are gone
                // and the stack has been renamed, but the list on the node survived and
                // the name it was given is still inside the new one.
                string stored = stack.Properties.GetString(StoredNameProperty);
                ClipRecord? recorded = stored.Length > 0 ? null : Recorded(stack, manifest);

                if (recorded is { } row)
                    stored = row.StoredName;

                if (stored.Length == 0)
                {
                    ignored.Add(stack.Name);
                    continue;
                }

                ExchangeResult result = exchange.Import(
                    project, document, stack.Name,
                    new ImportOptions { StoredName = stored, Overwrite = overwrite },
                    stack.Name);

                if (!result.Succeeded)
                {
                    failed[stack.Name] = result.Problem ?? "the import gave no reason";
                    continue;
                }

                // The generators come back too, and only the ones that are missing.
                // A round trip finds them all already there; an animation new to the
                // project arrives with nothing able to play it, which is the case
                // this is for.
                if (project.Animation(stored) is { } slot)
                    foreach (string generator in Generators(stack, recorded))
                        if (project.Clip(generator) is null)
                            project.AddClip(generator, slot);

                clips.Add(new ImportedClip(stack.Name, stored, result.CacheIndex ?? -1, result.Path));
            }

            return new ImportReport(clips, ignored, failed);
        }

        /// <summary>The clips a stack says play it, or the ones the manifest remembers.</summary>
        private static IEnumerable<string> Generators(FbxObject stack, ClipRecord? recorded) =>
            recorded is { } row
                ? row.Generators.Distinct(StringComparer.Ordinal)
                : stack.Properties.GetString(GeneratorsProperty)
                    .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.Ordinal);

        /// <summary>
        /// The node each bone of the rig is called in the scene.
        /// </summary>
        /// <remarks>
        /// A scene converted from a NIF has its names escaped — <c>NPC L Forearm
        /// [LLar]</c> is <c>NPC_s_L_s_Forearm_s__ob_LLar_cb_</c> on the node — so the
        /// map is built by unescaping what the scene holds rather than by escaping
        /// what the rig holds. Those are not the same thing: only the scene knows
        /// what its own nodes ended up called.
        /// </remarks>
        private static Dictionary<string, string> NodeNames(FbxDocument document, Skeleton rig)
        {
            var plain = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (FbxObject model in new FbxScene(document).OfClass("Model"))
                plain.TryAdd(NameEncoding.Unsanitize(model.Name), model.Name);

            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Bone bone in rig.Bones)
                if (plain.TryGetValue(bone.Name, out string? node))
                    names[bone.Name] = node;

            return names;
        }

        /// <summary>
        /// Records what the stack came from, so it can go back where it came from.
        /// </summary>
        /// <remarks>
        /// A stack's name is the animation's file stem, which is not enough to put it
        /// back: the project stores a path and addresses the animation by a cache
        /// index, and the clips that play it are named separately again. All three are
        /// written here rather than inferred later.
        /// </remarks>
        private static void Describe(
            FbxDocument document, string name, AnimationSlot slot, ActorProject project)
        {
            var scene = new FbxScene(document);

            FbxObject? stack = scene.OfClass("AnimationStack")
                .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));

            if (stack is null)
                return;

            stack.Properties.SetUserString(StoredNameProperty, slot.StoredName);
            stack.Properties.SetUserFloat(CacheIndexProperty, slot.Index);

            List<string> generators = project.ClipsOf(slot)
                .Select(clip => clip.Entry.Name)
                .Where(clipName => clipName.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (generators.Count > 0)
                stack.Properties.SetUserString(GeneratorsProperty, string.Join(Separator, generators));

            scene.Flush();
        }

        /// <summary>What a manifest row says about one clip.</summary>
        /// <param name="Stack">The stack it was written for.</param>
        /// <param name="StoredName">The animation as the project stores it.</param>
        /// <param name="Index">Its position in the character's animation list.</param>
        /// <param name="Generators">The clip generators that play it.</param>
        public readonly record struct ClipRecord(
            string Stack, string StoredName, int Index, IReadOnlyList<string> Generators);

        /// <summary>Puts the whole clip list on a node, where a DCC tool will keep it.</summary>
        private static void WriteManifest(
            FbxDocument document, IReadOnlyList<ClipStack> added, ActorProject project)
        {
            if (added.Count == 0)
                return;

            var rows = new List<string>(added.Count);

            foreach (ClipStack stack in added)
            {
                var fields = new List<string>
                {
                    stack.Name,
                    stack.StoredName,
                    stack.CacheIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                };

                if (project.Animation(stack.StoredName) is { } slot)
                    fields.AddRange(project.ClipsOf(slot)
                        .Select(clip => clip.Entry.Name)
                        .Where(name => name.Length > 0)
                        .Distinct(StringComparer.Ordinal));

                rows.Add(string.Join(Separator, fields));
            }

            var scene = new FbxScene(document);
            FbxObject? root = scene.RootModels().FirstOrDefault()
                ?? scene.OfClass("Model").FirstOrDefault();

            if (root is null)
                return;

            root.Properties.SetUserString(ManifestProperty, string.Join(RowSeparator, rows));
            scene.Flush();
        }

        /// <summary>The clip list a scene carries, by the stack name each was written for.</summary>
        /// <remarks>
        /// Public because it is the answer to "what animations does this file hold",
        /// which a tool may want without importing any of them.
        /// </remarks>
        public static IReadOnlyDictionary<string, ClipRecord> Manifest(FbxDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            var found = new Dictionary<string, ClipRecord>(StringComparer.OrdinalIgnoreCase);
            var scene = new FbxScene(document);

            foreach (FbxObject model in scene.OfClass("Model"))
            {
                string stored = model.Properties.GetString(ManifestProperty);

                if (stored.Length == 0)
                    continue;

                foreach (string row in stored.Split(RowSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] fields = row.Split(Separator);

                    if (fields.Length < 3 || fields[0].Length == 0)
                        continue;

                    int.TryParse(fields[2], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int index);

                    found[fields[0]] = new ClipRecord(fields[0], fields[1], index, fields[3..]);
                }

                break;
            }

            return found;
        }

        /// <summary>The manifest row a stack belongs to, however it has been renamed.</summary>
        /// <remarks>
        /// Blender writes a stack out as <c>Skeleton.nif|Skeleton.nif|1HMAttackA|Default</c>
        /// -- the object, the action and the track, joined by bars -- so the name it was
        /// given is in there rather than lost. Each part is tried rather than a fixed
        /// position taken, because which part is the action is Blender's business and
        /// another tool will decorate it differently or not at all.
        /// </remarks>
        private static ClipRecord? Recorded(
            FbxObject stack, IReadOnlyDictionary<string, ClipRecord> manifest)
        {
            if (manifest.Count == 0)
                return null;

            foreach (string name in Names(stack.Name))
                if (manifest.TryGetValue(name, out ClipRecord found))
                    return found;

            return null;
        }

        /// <summary>Every name a stack might be listed under, the whole one first.</summary>
        public static IEnumerable<string> Names(string stackName)
        {
            if (string.IsNullOrEmpty(stackName))
                yield break;

            yield return stackName;

            if (!stackName.Contains('|', StringComparison.Ordinal))
                yield break;

            foreach (string part in stackName.Split('|', StringSplitOptions.RemoveEmptyEntries))
                yield return part;
        }

        /// <summary>
        /// A stack name nothing else in the document has.
        /// </summary>
        /// <remarks>
        /// Two stacks of one name is a file whose clips a reader cannot tell apart,
        /// and a project can hold two animations with the same file stem in different
        /// folders. Suffixed with a number rather than the folder, because a folder
        /// name is long and the stack name is what an animator reads off a menu.
        /// </remarks>
        private static string Unique(string stem, HashSet<string> taken)
        {
            string name = stem.Length > 0 ? stem : "clip";

            if (taken.Add(name))
                return name;

            for (int i = 2; ; i++)
            {
                string candidate = $"{name}_{i}";

                if (taken.Add(candidate))
                    return candidate;
            }
        }
    }
}
