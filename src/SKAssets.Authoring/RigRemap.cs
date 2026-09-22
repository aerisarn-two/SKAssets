using System.Numerics;
using System.Text.RegularExpressions;
using HKFBX.Model;
using HKSK.Havok;
using HKX2;

namespace SKAssets.Authoring
{
    /// <summary>
    /// Rebinds a creature's character file and behaviour graphs from the rig they
    /// were written for to another, by bone name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A behaviour names a bone by its index in the rig: the foot IK's hips, knees
    /// and ankles, the look-at's neck and head, the bones a get-up matches, the
    /// mirror map, the keyframed bones a ragdoll is driven around. A creature made
    /// from another with a rig of its own keeps those numbers, and every one of
    /// them then names some other bone or none. So each is looked up as a name in
    /// the old rig and found again in the new, through a map the caller gives for
    /// the bones that are called differently -- the sabre cat's <c>Sabrecat_Head
    /// [Head]</c> is the cat's <c>head</c> -- and by the same name otherwise. A bone
    /// with no place in the new rig drops out of a list, and a single index falls
    /// back to the root and is reported.
    /// </para>
    /// <para>
    /// Two of the numbers are geometry rather than names, and are derived from the
    /// new rig's rest pose the way the shipped values were from the old one: a
    /// look-at bone's forward axis is model-forward expressed in the bone, exactly
    /// the sabre cat's stored values; a knee's bend axis is the normal of the plane
    /// its leg bends in, snapped to the bone axis it lies along, with the sign the
    /// shipped files carry. The foot IK's heights scale with the ankle's rest
    /// height, since the template's were tuned for its own legs.
    /// </para>
    /// <para>
    /// Ragdoll bones follow their rig bones: <c>Ragdoll_X</c> maps to
    /// <c>Ragdoll_</c> plus whatever <c>X</c> maps to, which is the convention every
    /// shipped ragdoll keeps.
    /// </para>
    /// </remarks>
    public static class RigRemap
    {
        /// <summary>What was rebound, and what could not be.</summary>
        public sealed record Report(IReadOnlyList<string> Files, IReadOnlyList<string> Notes);

        /// <summary>
        /// Rebinds every graph and character file given, in place.
        /// </summary>
        /// <param name="files">The .hkx files to edit: the character file and the behaviours.</param>
        /// <param name="oldRig">The rig the files were written for.</param>
        /// <param name="newRig">The rig they are to serve, in the order the new skeleton.hkx lists it.</param>
        /// <param name="oldRagdoll">The old ragdoll skeleton, for the modifiers that index ragdoll bones.</param>
        /// <param name="newRagdoll">The new one.</param>
        /// <param name="boneMap">Old bone name to new, for the bones not called the same.</param>
        /// <param name="mirror">A bone's mirror partner by name, or null for none; the default reads L/R conventions.</param>
        public static Report Apply(
            IEnumerable<string> files, Skeleton oldRig, Skeleton newRig, Skeleton? oldRagdoll, Skeleton? newRagdoll,
            IReadOnlyDictionary<string, string>? boneMap = null, Func<string, string?>? mirror = null)
        {
            ArgumentNullException.ThrowIfNull(files);
            ArgumentNullException.ThrowIfNull(oldRig);
            ArgumentNullException.ThrowIfNull(newRig);

            var map = new Mapping(oldRig, newRig, oldRagdoll, newRagdoll, boneMap ?? new Dictionary<string, string>());
            var notes = new List<string>();
            var edited = new List<string>();
            Matrix4x4[] rest = ModelSpace(newRig);
            mirror ??= name => MirrorOf(name, newRig);

            foreach (string path in files)
            {
                HavokFile file;
                try { file = HavokFile.Load(path); }
                catch (Exception e) when (e is not OutOfMemoryException) { notes.Add($"{Path.GetFileName(path)}: not read ({e.Message})"); continue; }

                bool changed = false;
                string name = Path.GetFileName(path);

                foreach (hkbCharacterData character in file.All<hkbCharacterData>())
                    changed |= Character(character, map, rest, newRig, mirror, name, notes);

                foreach (BSLookAtModifier lookAt in file.All<BSLookAtModifier>())
                {
                    changed |= LookAt(lookAt.m_bones, map, rest, out IList<BSLookAtModifierBoneData> bones, name, lookAt.m_name, notes);
                    lookAt.m_bones = bones;
                    changed |= LookAt(lookAt.m_eyeBones, map, rest, out IList<BSLookAtModifierBoneData> eyes, name, lookAt.m_name, notes);
                    lookAt.m_eyeBones = eyes;
                }

                // Keyframing drives rigid bodies, so the indices are the ragdoll's: the sabre
                // cat's get-up list names 21 of its 28 ragdoll bones, and read as rig bones it
                // names the eyes and the ears.
                foreach (hkbKeyframeBonesModifier keyframed in file.All<hkbKeyframeBonesModifier>())
                {
                    changed |= Filter(keyframed.m_keyframeInfo, k => k.m_boneIndex, (k, i) => k.m_boneIndex = (short)i, map.Ragdoll, out var infos, name, keyframed.m_name, notes);
                    keyframed.m_keyframeInfo = infos;
                    if (keyframed.m_keyframedBonesList is { } list)
                        changed |= Indices(list, map.Ragdoll, name, keyframed.m_name, notes);
                }

                foreach (hkbPoweredRagdollControlsModifier powered in file.All<hkbPoweredRagdollControlsModifier>())
                {
                    if (powered.m_bones is { } bones) changed |= Indices(bones, map.Ragdoll, name, powered.m_name, notes);
                    if (powered.m_boneWeights is { } weights) changed |= Weights(weights, map, name, powered.m_name, notes);
                }

                foreach (hkbRigidBodyRagdollControlsModifier rigid in file.All<hkbRigidBodyRagdollControlsModifier>())
                    if (rigid.m_bones is { } bones) changed |= Indices(bones, map.Ragdoll, name, rigid.m_name, notes);

                foreach (BSRagdollContactListenerModifier contact in file.All<BSRagdollContactListenerModifier>())
                    if (contact.m_bones is { } bones) changed |= Indices(bones, map.Ragdoll, name, contact.m_name, notes);

                foreach (hkbBlenderGeneratorChild child in file.All<hkbBlenderGeneratorChild>())
                    if (child.m_boneWeights is { } weights) changed |= Weights(weights, map, name, "blend child", notes);

                foreach (BSBoneSwitchGeneratorBoneData bone in file.All<BSBoneSwitchGeneratorBoneData>())
                    if (bone.m_spBoneWeight is { } weights) changed |= Weights(weights, map, name, "bone switch", notes);

                changed |= Scalars(file, map, name, notes);

                if (changed)
                {
                    file.Save(path);
                    edited.Add(path);
                }
            }

            return new Report(edited, notes);
        }

        // ------------------------------------------------------------ the character file

        private static bool Character(
            hkbCharacterData character, Mapping map, Matrix4x4[] rest, Skeleton newRig, Func<string, string?> mirror,
            string file, List<string> notes)
        {
            bool changed = false;

            if (character.m_numBonesPerLod.Count > 0)
            {
                character.m_numBonesPerLod = [newRig.Count];
                changed = true;
            }

            if (character.m_footIkDriverInfo is { } ik)
            {
                var legs = new List<hkbFootIkDriverInfoLeg>();

                foreach (hkbFootIkDriverInfoLeg leg in ik.m_legs)
                {
                    int hip = map.Rig(leg.m_hipIndex), knee = map.Rig(leg.m_kneeIndex), ankle = map.Rig(leg.m_ankleIndex);

                    if (hip < 0 || knee < 0 || ankle < 0)
                    {
                        notes.Add($"{file}: foot IK leg {map.OldRigName(leg.m_hipIndex)}/{map.OldRigName(leg.m_kneeIndex)}/{map.OldRigName(leg.m_ankleIndex)} has no bones in the new rig and was dropped");
                        continue;
                    }

                    float oldAnkleHeight = map.OldRest[leg.m_ankleIndex].Translation.Z;
                    float newAnkleHeight = rest[ankle].Translation.Z;
                    float scale = oldAnkleHeight > 1e-3f ? newAnkleHeight / oldAnkleHeight : 1f;

                    leg.m_hipIndex = (short)hip;
                    leg.m_kneeIndex = (short)knee;
                    leg.m_ankleIndex = (short)ankle;
                    leg.m_kneeAxisLS = KneeAxis(rest, hip, knee, ankle);
                    leg.m_footPlantedAnkleHeightMS *= scale;
                    leg.m_footRaisedAnkleHeightMS *= scale;
                    leg.m_maxAnkleHeightMS *= scale;
                    leg.m_minAnkleHeightMS *= scale;
                    legs.Add(leg);
                }

                ik.m_legs = legs;
                changed = true;
            }

            if (character.m_mirroredSkeletonInfo is { } mirrored)
            {
                var pairs = new List<short>(newRig.Count);

                for (int i = 0; i < newRig.Count; i++)
                {
                    string? partner = mirror(newRig.Bones[i].Name);
                    int j = partner is null ? -1 : IndexOf(newRig, partner);
                    pairs.Add((short)(j >= 0 ? j : i));
                }

                mirrored.m_bonePairMap = pairs;
                changed = true;
            }

            if (character.m_characterPropertyValues is { } values)
                foreach (hkbBoneWeightArray weights in values.m_variantVariableValues.OfType<hkbBoneWeightArray>())
                    changed |= Weights(weights, map, file, "character property", notes);

            return changed;
        }

        // ------------------------------------------------------------ nodes

        private static bool LookAt(
            IList<BSLookAtModifierBoneData> bones, Mapping map, Matrix4x4[] rest, out IList<BSLookAtModifierBoneData> kept,
            string file, string node, List<string> notes)
        {
            kept = new List<BSLookAtModifierBoneData>();
            bool changed = false;

            foreach (BSLookAtModifierBoneData bone in bones)
            {
                int index = map.Rig(bone.m_index);

                if (index < 0)
                {
                    notes.Add($"{file}: look-at '{node}' bone {map.OldRigName(bone.m_index)} has no place in the new rig and was dropped");
                    changed = true;
                    continue;
                }

                changed |= index != bone.m_index;
                bone.m_index = (short)index;
                bone.m_fwdAxisLS = ForwardAxis(rest[index]);
                kept.Add(bone);
                changed = true;
            }

            return changed;
        }

        private static bool Indices(hkbBoneIndexArray array, Func<int, int> remap, string file, string node, List<string> notes)
        {
            var kept = new List<short>();
            bool changed = false;

            foreach (short index in array.m_boneIndices)
            {
                int mapped = remap(index);

                if (mapped < 0)
                {
                    changed = true;
                    continue;
                }

                changed |= mapped != index;
                kept.Add((short)mapped);
            }

            if (changed) array.m_boneIndices = kept;
            if (kept.Count < array.m_boneIndices.Count || changed && kept.Count == 0)
                notes.Add($"{file}: '{node}' keeps {kept.Count} of its bones");

            return changed;
        }

        /// <summary>
        /// Bone weights indexed by rig bone, or by ragdoll bone when the count says
        /// so; a bone new to the rig weighs one, as an unlisted bone does.
        /// </summary>
        private static bool Weights(hkbBoneWeightArray weights, Mapping map, string file, string node, List<string> notes)
        {
            int count = weights.m_boneWeights.Count;
            if (count == 0) return false;

            (Func<int, int> remap, int newCount)? by =
                count == map.OldRig.Count ? (map.Rig, map.NewRig.Count)
                : map.OldRagdoll is { } oldRagdoll && count == oldRagdoll.Count && map.NewRagdoll is { } newRagdoll ? (map.Ragdoll, newRagdoll.Count)
                : null;

            if (by is null)
            {
                notes.Add($"{file}: '{node}' weighs {count} bones, which is neither rig; left alone");
                return false;
            }

            var kept = Enumerable.Repeat(1f, by.Value.newCount).ToArray();

            for (int i = 0; i < count; i++)
            {
                int mapped = by.Value.remap(i);
                if (mapped >= 0) kept[mapped] = weights.m_boneWeights[i];
            }

            weights.m_boneWeights = kept;
            return true;
        }

        private static bool Filter<T>(
            IList<T> items, Func<T, int> index, Action<T, int> set, Func<int, int> remap, out IList<T> kept,
            string file, string node, List<string> notes)
        {
            kept = new List<T>();
            bool changed = false;

            foreach (T item in items)
            {
                int mapped = remap(index(item));
                if (mapped < 0) { changed = true; continue; }

                changed |= mapped != index(item);
                set(item, mapped);
                kept.Add(item);
            }

            if (kept.Count < items.Count) notes.Add($"{file}: '{node}' keeps {kept.Count} of {items.Count} bones");
            return changed;
        }

        /// <summary>The single bone indices the modifiers carry, each to its bone or the root.</summary>
        private static bool Scalars(HavokFile file, Mapping map, string name, List<string> notes)
        {
            bool changed = false;

            foreach (hkbGetUpModifier m in file.All<hkbGetUpModifier>())
            {
                changed |= Scalar(m.m_rootBoneIndex, v => m.m_rootBoneIndex = v, map, name, m.m_name, "root", notes);
                changed |= Scalar(m.m_otherBoneIndex, v => m.m_otherBoneIndex = v, map, name, m.m_name, "other", notes);
                changed |= Scalar(m.m_anotherBoneIndex, v => m.m_anotherBoneIndex = v, map, name, m.m_name, "another", notes);
            }

            foreach (hkbPoseMatchingGenerator g in file.All<hkbPoseMatchingGenerator>())
            {
                changed |= Scalar(g.m_rootBoneIndex, v => g.m_rootBoneIndex = v, map, name, g.m_name, "root", notes);
                changed |= Scalar(g.m_otherBoneIndex, v => g.m_otherBoneIndex = v, map, name, g.m_name, "other", notes);
                changed |= Scalar(g.m_anotherBoneIndex, v => g.m_anotherBoneIndex = v, map, name, g.m_name, "another", notes);
                changed |= Scalar(g.m_pelvisIndex, v => g.m_pelvisIndex = v, map, name, g.m_name, "pelvis", notes);
            }

            foreach (BSDirectAtModifier m in file.All<BSDirectAtModifier>())
            {
                changed |= Scalar(m.m_sourceBoneIndex, v => m.m_sourceBoneIndex = v, map, name, m.m_name, "source", notes);
                changed |= Scalar(m.m_startBoneIndex, v => m.m_startBoneIndex = v, map, name, m.m_name, "start", notes);
                changed |= Scalar(m.m_endBoneIndex, v => m.m_endBoneIndex = v, map, name, m.m_name, "end", notes);
            }

            foreach (BSLimbIKModifier m in file.All<BSLimbIKModifier>())
            {
                changed |= Scalar(m.m_startBoneIndex, v => m.m_startBoneIndex = v, map, name, m.m_name, "start", notes);
                changed |= Scalar(m.m_endBoneIndex, v => m.m_endBoneIndex = v, map, name, m.m_name, "end", notes);
            }

            foreach (BSComputeAddBoneAnimModifier m in file.All<BSComputeAddBoneAnimModifier>())
                changed |= Scalar(m.m_boneIndex, v => m.m_boneIndex = v, map, name, m.m_name, "bone", notes);

            foreach (hkbLookAtModifier m in file.All<hkbLookAtModifier>())
            {
                changed |= Scalar(m.m_headIndex, v => m.m_headIndex = v, map, name, m.m_name, "head", notes);
                changed |= Scalar(m.m_neckIndex, v => m.m_neckIndex = v, map, name, m.m_name, "neck", notes);
            }

            foreach (hkbFootIkModifier m in file.All<hkbFootIkModifier>())
            {
                var legs = new List<hkbFootIkModifierLeg>();
                foreach (hkbFootIkModifierLeg leg in m.m_legs)
                {
                    int hip = map.Rig(leg.m_hipIndex), knee = map.Rig(leg.m_kneeIndex), ankle = map.Rig(leg.m_ankleIndex);
                    if (hip < 0 || knee < 0 || ankle < 0) { notes.Add($"{name}: '{m.m_name}' drops a leg the new rig lacks"); continue; }
                    leg.m_hipIndex = (short)hip; leg.m_kneeIndex = (short)knee; leg.m_ankleIndex = (short)ankle;
                    legs.Add(leg);
                }
                m.m_legs = legs;
                changed = true;
            }

            return changed;
        }

        private static bool Scalar(short index, Action<short> set, Mapping map, string file, string node, string what, List<string> notes)
        {
            if (index < 0) return false;

            int mapped = map.Rig(index);

            if (mapped < 0)
            {
                mapped = 0;
                notes.Add($"{file}: '{node}' {what} bone {map.OldRigName(index)} has no place in the new rig; the root stands in");
            }

            if (mapped == index) return false;
            set((short)mapped);
            return true;
        }

        // ------------------------------------------------------------ geometry

        /// <summary>Each bone's rest transform in model space, composed from the locals.</summary>
        public static Matrix4x4[] ModelSpace(Skeleton skeleton)
        {
            ArgumentNullException.ThrowIfNull(skeleton);

            var world = new Matrix4x4[skeleton.Count];

            for (int i = 0; i < world.Length; i++)
            {
                BoneTransform pose = skeleton.Bones[i].ReferencePose;
                Matrix4x4 local = Matrix4x4.CreateScale(pose.Scale)
                    * Matrix4x4.CreateFromQuaternion(pose.Rotation)
                    * Matrix4x4.CreateTranslation(pose.Translation);
                int parent = skeleton.Bones[i].ParentIndex;
                world[i] = parent >= 0 && parent < i ? local * world[parent] : local;
            }

            return world;
        }

        /// <summary>
        /// Model-forward (+Y) in a bone's own frame: what a look-at turns toward
        /// its target. The sabre cat's stored axes are exactly this, to four places.
        /// </summary>
        public static Vector4 ForwardAxis(Matrix4x4 boneModelSpace)
        {
            Matrix4x4.Invert(boneModelSpace, out Matrix4x4 inverse);
            Vector3 forward = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, inverse));
            return new Vector4(forward, 0f);
        }

        /// <summary>
        /// The axis a knee bends about, in the knee bone's frame: the normal of the
        /// hip-knee-ankle plane with the sign the shipped files carry, snapped to
        /// the bone axis it lies nearest, as theirs are.
        /// </summary>
        public static Vector4 KneeAxis(Matrix4x4[] rest, int hip, int knee, int ankle)
        {
            ArgumentNullException.ThrowIfNull(rest);

            Vector3 thigh = rest[knee].Translation - rest[hip].Translation;
            Vector3 shin = rest[ankle].Translation - rest[knee].Translation;
            Vector3 normal = Vector3.Cross(shin, thigh);

            // A straight leg has no bend plane; the model's sideways axis is the
            // one a leg bends about.
            if (normal.LengthSquared() < 1e-6f) normal = Vector3.Cross(Vector3.UnitY, thigh);
            if (normal.LengthSquared() < 1e-6f) normal = Vector3.UnitX;

            Matrix4x4.Invert(rest[knee], out Matrix4x4 inverse);
            Vector3 local = Vector3.Normalize(Vector3.TransformNormal(Vector3.Normalize(normal), inverse));

            float ax = MathF.Abs(local.X), ay = MathF.Abs(local.Y), az = MathF.Abs(local.Z);
            Vector3 snapped = ax >= ay && ax >= az ? new Vector3(MathF.Sign(local.X), 0, 0)
                : ay >= az ? new Vector3(0, MathF.Sign(local.Y), 0)
                : new Vector3(0, 0, MathF.Sign(local.Z));

            return new Vector4(snapped, 0f);
        }

        /// <summary>
        /// A bone's mirror partner by the conventions the rigs use: <c>.L</c>/<c>.R</c>,
        /// <c>_L</c>/<c>_R</c>, <c>Left</c>/<c>Right</c>, <c> L </c>/<c> R </c>, <c>[L</c>/<c>[R</c>.
        /// </summary>
        public static string? MirrorOf(string name, Skeleton rig)
        {
            ArgumentNullException.ThrowIfNull(rig);

            foreach ((string pattern, string replacement) in Sides)
            {
                string candidate = Regex.Replace(name, pattern, replacement);
                if (candidate != name && IndexOf(rig, candidate) >= 0) return candidate;
            }

            return null;
        }

        /// <summary>Each side's spelling as a pattern, and the other side's that replaces it.</summary>
        private static readonly (string Pattern, string Replacement)[] Sides =
        [
            (@"\.L$", ".R"), (@"\.R$", ".L"),
            (@"\.L\.", ".R."), (@"\.R\.", ".L."),
            (@"_L$", "_R"), (@"_R$", "_L"),
            (@"_L_", "_R_"), (@"_R_", "_L_"),
            ("Left", "Right"), ("Right", "Left"),
            (@"\bL\b", "R"), (@"\bR\b", "L"),
            (@"\[L", "[R"), (@"\[R", "[L"),
        ];

        private static int IndexOf(Skeleton rig, string name)
        {
            for (int i = 0; i < rig.Count; i++)
                if (string.Equals(rig.Bones[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;

            return -1;
        }

        // ------------------------------------------------------------ the mapping

        private sealed class Mapping
        {
            private readonly int[] _rig;
            private readonly int[] _ragdoll;

            public Mapping(Skeleton oldRig, Skeleton newRig, Skeleton? oldRagdoll, Skeleton? newRagdoll, IReadOnlyDictionary<string, string> boneMap)
            {
                OldRig = oldRig;
                NewRig = newRig;
                OldRagdoll = oldRagdoll;
                NewRagdoll = newRagdoll;
                OldRest = ModelSpace(oldRig);

                var renamed = new Dictionary<string, string>(boneMap, StringComparer.OrdinalIgnoreCase);

                _rig = new int[oldRig.Count];
                for (int i = 0; i < oldRig.Count; i++)
                    _rig[i] = IndexOf(newRig, renamed.GetValueOrDefault(oldRig.Bones[i].Name, oldRig.Bones[i].Name));

                _ragdoll = new int[oldRagdoll?.Count ?? 0];
                for (int i = 0; i < _ragdoll.Length; i++)
                {
                    string old = oldRagdoll!.Bones[i].Name;
                    string target = renamed.TryGetValue(old, out string? given) ? given
                        : old.StartsWith("Ragdoll_", StringComparison.OrdinalIgnoreCase)
                            ? "Ragdoll_" + renamed.GetValueOrDefault(old["Ragdoll_".Length..], old["Ragdoll_".Length..])
                            : old;
                    _ragdoll[i] = newRagdoll is null ? -1 : IndexOf(newRagdoll, target);
                }
            }

            public Skeleton OldRig { get; }
            public Skeleton NewRig { get; }
            public Skeleton? OldRagdoll { get; }
            public Skeleton? NewRagdoll { get; }
            public Matrix4x4[] OldRest { get; }

            public int Rig(int old) => old >= 0 && old < _rig.Length ? _rig[old] : -1;
            public int Ragdoll(int old) => old >= 0 && old < _ragdoll.Length ? _ragdoll[old] : -1;
            public string OldRigName(int old) => old >= 0 && old < OldRig.Count ? OldRig.Bones[old].Name : $"#{old}";
        }
    }
}
