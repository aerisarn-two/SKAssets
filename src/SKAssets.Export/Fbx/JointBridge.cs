using System.Globalization;
using System.Numerics;
using HKFBX.Model;
using LeanMeshIO;
using NIFBX.Conversion;
using NIFBX.Fbx;
using NIFSharp;

namespace SKAssets.Export.Fbx
{
    /// <summary>
    /// The ragdoll's joints, said in the ragdoll's own vocabulary.
    /// </summary>
    /// <remarks>
    /// A constraint comes out of the mesh naming the two nodes it joins, because
    /// that is what the mesh has: <c>Pelvis_rb</c> and <c>LFemur_rb</c>. A Havok
    /// reader wants the two ragdoll bodies, which are <c>Ragdoll_Pelvis</c> and
    /// <c>Ragdoll_LFemur</c>, and the mapping between the two vocabularies is the
    /// thing this library carries. So the names are translated here, after the
    /// bodies have been named and before anything reads them back — without it the
    /// ragdoll hierarchy HKFBX rebuilds from the joints joins nothing to anything.
    ///
    /// The limits and the two frames are then overwritten from the Havok file where
    /// there is one, for the same reason the poses are: the mesh's own numbers are
    /// close but not the same bits, and a frame that arrives through a node's
    /// Euler channels is not the frame that left. Measured over the 45 vanilla
    /// creatures, 909 joints came back with neither frame intact and the cone limit
    /// right only where it was zero to begin with.
    /// </remarks>
    public static class JointBridge
    {
        /// <summary>Where the joint sat in the Havok file's own list.</summary>
        /// <remarks>
        /// A scene holds its constraint nodes in whatever order the mesh listed
        /// them, and a ragdoll addresses its joints by index.
        /// </remarks>
        public const string OrderProperty = "sk_joint_index";

        /// <summary>The near frame, exactly.</summary>
        public const string FrameAProperty = "sk_frame_a";

        /// <summary>The far frame, exactly.</summary>
        public const string FrameBProperty = "sk_frame_b";

        private const string TypeProperty = "constraint_type";
        private const string FrameMarker = "constraint_frame";
        private const string BodyAProperty = "constraint_body_a";
        private const string BodyBProperty = "constraint_body_b";
        private const string Separator = "_con_";
        private const string AttachSuffix = "_attach_point";

        /// <summary>
        /// Translates each constraint's body names, and carries the Havok file's own
        /// joint where there is one.
        /// </summary>
        /// <returns>How many constraints were matched to a Havok joint.</returns>
        public static int Apply(FbxDocument document, SkeletonFile? havok = null)
        {
            ArgumentNullException.ThrowIfNull(document);

            var scene = new FbxScene(document);
            var models = scene.OfClass("Model").ToList();

            // Ragdoll body name by the node that carries it, which is what a
            // constraint names.
            var ragdollOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (FbxObject model in models)
            {
                string body = model.Properties.GetString(HavokBridge.BodyPrefix + "ragdoll_bone");

                // Keyed by the plain name, because that is the spelling a
                // constraint states its bodies in and the spelling its own name
                // unescapes to. Keying by the escaped form matches only the
                // creatures whose bones need no escaping -- 281 joints of 909, the
                // cow among them and nothing with a bracket in its bone names.
                if (body.Length > 0)
                    ragdollOf[Plain(model.Name)] = body;
            }

            // Keyed by the body the joint moves, because in a ragdoll that is what
            // identifies a joint: a body hangs from exactly one parent, so there is
            // exactly one joint per child body. Keying by the pair instead looks
            // safer and is not -- the hare's spine is numbered in opposite
            // directions in its two files, so the mesh hangs the arms off
            // Ragdoll_Spine03 where the ragdoll hangs them off Ragdoll_Spine01, and
            // 50 of the game's 909 joints match on the child and on nothing else.
            // Where the two disagree the Havok file is the one being reproduced, so
            // the far name is overwritten from it rather than trusted.
            var joints = new Dictionary<string, (RagdollJoint Joint, int Index)>(
                StringComparer.OrdinalIgnoreCase);

            if (havok is not null)
                for (int i = 0; i < havok.Joints.Count; i++)
                    joints.TryAdd(havok.Joints[i].BodyA, (havok.Joints[i], i));

            int matched = 0;
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bodyNodes = new Dictionary<string, FbxObject>(StringComparer.OrdinalIgnoreCase);

            foreach (FbxObject model in models)
            {
                string body = model.Properties.GetString(HavokBridge.BodyPrefix + "ragdoll_bone");

                if (body.Length > 0)
                    bodyNodes.TryAdd(body, model);
            }

            foreach (FbxObject node in models)
            {
                if (node.Properties.GetString(TypeProperty).Length == 0) continue;
                if (node.Properties.GetString(FrameMarker) == "A") continue;

                // The far body is the node this one hangs off; the near body is the
                // second half of the name. Both are stated outright as well, and the
                // stated form is preferred because a name long enough to hold two
                // bone names is a name Blender will have truncated.
                string near = Plain(node.Properties.GetString(BodyAProperty));
                string far = Plain(node.Properties.GetString(BodyBProperty));

                if (near.Length == 0 || far.Length == 0)
                {
                    (string parsedFar, string parsedNear) = Split(Plain(node.Name));
                    if (near.Length == 0) near = parsedNear;
                    if (far.Length == 0) far = parsedFar;
                }

                if (far.Length == 0 && scene.ParentsOf(node.Id).FirstOrDefault() is { } parent)
                    far = Plain(parent.Name);

                string bodyA = ragdollOf.GetValueOrDefault(near, near);
                string bodyB = ragdollOf.GetValueOrDefault(far, far);

                if (joints.TryGetValue(bodyA, out var stated))
                {
                    bodyB = stated.Joint.BodyB;
                    Carry(node, stated.Joint, stated.Index);
                    placed.Add(bodyA);
                    matched++;
                }
                else if (havok is not null && !ragdollOf.ContainsKey(near))
                {
                    // A constraint on a body the ragdoll never named. It stays in the
                    // scene as the mesh's own -- its hkc_ fields are what rebuilds the
                    // NIF -- but it is not a ragdoll joint and must not read back as
                    // one, so the two Havok names are left empty and the import drops
                    // it. The frostbite spider is the case: its left and right Leg_01
                    // bodies are crossed between its two files, so the mesh has a
                    // constraint for a body the ragdoll gives to the other leg.
                    node.Properties.SetUserString(BodyAProperty, string.Empty);
                    node.Properties.SetUserString(BodyBProperty, string.Empty);
                    continue;
                }

                node.Properties.SetUserString(BodyAProperty, bodyA);
                node.Properties.SetUserString(BodyBProperty, bodyB);
            }

            // And a node for every joint the mesh could not account for, the way the
            // bone union adds one for every body the mesh does not have. Without it
            // the ragdoll comes back a joint short and everything after it in the
            // list has moved up one.
            if (havok is not null)
                foreach (RagdollJoint joint in havok.Joints)
                {
                    if (placed.Contains(joint.BodyA))
                        continue;

                    Carry(Invent(scene, joint, bodyNodes), joint, joints[joint.BodyA].Index);
                    matched++;
                }

            scene.Flush();
            return matched;
        }

        /// <summary>
        /// A constraint node for a joint the mesh has no constraint for.
        /// </summary>
        /// <remarks>
        /// Placed under the body it hangs from, which is where the mesh would have
        /// put it, and named the same way: far body, separator, near body, suffix.
        /// </remarks>
        private static FbxObject Invent(
            FbxScene scene, RagdollJoint joint, IReadOnlyDictionary<string, FbxObject> bodyNodes)
        {
            string name = NameEncoding.Sanitize(
                $"{joint.BodyB}{Separator}{joint.BodyA}{AttachSuffix}");

            FbxObject node = FbxMeshWriter.AddModel(scene, name, "Null", Frame(joint.FrameB));

            node.Properties.SetUserString(BoneOrigin.Property, BoneOrigin.Havok);
            node.Properties.SetUserString(FrameMarker, "B");
            node.Properties.SetUserString(BodyAProperty, joint.BodyA);
            node.Properties.SetUserString(BodyBProperty, joint.BodyB);

            if (bodyNodes.GetValueOrDefault(joint.BodyB) is { } parent) scene.Connect(node, parent);
            else scene.ConnectToRoot(node);

            return node;
        }

        /// <summary>
        /// A joint frame as a node's placement, for a viewer's benefit. The exact
        /// matrix travels as a property beside it.
        /// </summary>
        private static NifTransform Frame(Matrix4x4 frame)
        {
            if (!Matrix4x4.Decompose(frame, out _, out Quaternion rotation, out Vector3 translation))
            {
                rotation = Quaternion.Identity;
                translation = frame.Translation;
            }

            Matrix4x4 m = Matrix4x4.CreateFromQuaternion(rotation);

            // Into the column form a NIF stores and NifTransform expects.
            var basis = new NifMatrix33
            {
                M11 = m.M11, M12 = m.M21, M13 = m.M31,
                M21 = m.M12, M22 = m.M22, M23 = m.M32,
                M31 = m.M13, M32 = m.M23, M33 = m.M33,
            };

            return new NifTransform(
                new NifVector3 { X = translation.X, Y = translation.Y, Z = translation.Z }, basis, 1f);
        }

        /// <summary>
        /// Puts the carried frames and joint order back.
        /// </summary>
        public static SkeletonFile ApplyTo(SkeletonFile file, FbxDocument document)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(document);

            var scene = new FbxScene(document);
            var frames = new Dictionary<string, (Matrix4x4 A, Matrix4x4 B, int Index)>(
                StringComparer.OrdinalIgnoreCase);

            foreach (FbxObject node in scene.OfClass("Model"))
            {
                if (node.Properties.GetString(TypeProperty).Length == 0) continue;
                if (node.Properties.GetString(FrameMarker) == "A") continue;

                string a = node.Properties.GetString(FrameAProperty);
                string b = node.Properties.GetString(FrameBProperty);

                if (a.Length == 0 || b.Length == 0) continue;
                if (Matrix(a) is not { } frameA || Matrix(b) is not { } frameB) continue;

                string key = node.Properties.GetString(BodyAProperty);

                if (key.Length == 0) continue;

                int index = (int)node.Properties.GetDouble(OrderProperty);
                frames.TryAdd(key, (frameA, frameB, index));
            }

            if (frames.Count == 0)
                return file;

            var exact = new List<RagdollJoint>(file.Joints.Count);

            foreach (RagdollJoint joint in file.Joints)
            {
                // A constraint the scene left unnamed is the mesh's own and not the
                // ragdoll's. See Apply.
                if (joint.BodyA.Length == 0 || joint.BodyB.Length == 0)
                    continue;

                exact.Add(frames.TryGetValue(joint.BodyA, out var found)
                    ? joint with { FrameA = found.A, FrameB = found.B }
                    : joint);
            }

            // Back into the order the Havok file listed them, and anything the scene
            // did not place keeps its relative position at the end.
            exact = exact
                .Select((joint, position) => (joint, key: frames.TryGetValue(joint.BodyA, out var f)
                    ? f.Index
                    : int.MaxValue - exact.Count + position))
                .OrderBy(entry => entry.key)
                .Select(entry => entry.joint)
                .ToList();

            return new SkeletonFile
            {
                Rig = file.Rig,
                FloatSlots = file.FloatSlots,
                Ragdoll = file.Ragdoll,
                Bodies = file.Bodies,
                Joints = exact,
                RigToRagdoll = file.RigToRagdoll,
                RagdollToRig = file.RagdollToRig,
            };
        }

        /// <summary>
        /// Writes the Havok joint's own numbers over the mesh's.
        /// </summary>
        /// <remarks>
        /// The limits go under the names HKFBX reads them by, so they need no
        /// import-side handling; the frames cannot, because HKFBX takes those from
        /// the node's transform, so they travel as their own properties and
        /// <see cref="ApplyTo"/> prefers them.
        /// </remarks>
        private static void Carry(FbxObject node, RagdollJoint joint, int index)
        {
            node.Properties.SetUserFloat(OrderProperty, index);
            node.Properties.SetUserString(FrameAProperty, Text(joint.FrameA));
            node.Properties.SetUserString(FrameBProperty, Text(joint.FrameB));
            node.Properties.SetUserFloat("maxFriction", joint.MaxFrictionTorque);

            switch (joint.Kind)
            {
                case JointKind.Ragdoll:
                    node.Properties.SetUserString(TypeProperty, "Ragdoll");
                    node.Properties.SetUserFloat("coneMaxAngle", joint.ConeMaxAngle);
                    node.Properties.SetUserFloat("planeMinAngle", joint.PlaneMinAngle);
                    node.Properties.SetUserFloat("planeMaxAngle", joint.PlaneMaxAngle);
                    node.Properties.SetUserFloat("twistMinAngle", joint.TwistMinAngle);
                    node.Properties.SetUserFloat("twistMaxAngle", joint.TwistMaxAngle);
                    break;

                case JointKind.LimitedHinge:
                    node.Properties.SetUserString(TypeProperty, "LimitedHinge");
                    node.Properties.SetUserFloat("minAngle", joint.HingeMinAngle);
                    node.Properties.SetUserFloat("maxAngle", joint.HingeMaxAngle);
                    break;
            }
        }

        /// <summary>The far body and the near one, out of a constraint's name.</summary>
        private static (string Far, string Near) Split(string name)
        {
            if (name.EndsWith(AttachSuffix, StringComparison.Ordinal))
                name = name[..^AttachSuffix.Length];

            int at = name.IndexOf(Separator, StringComparison.Ordinal);

            return at < 0
                ? (string.Empty, name)
                : (name[..at], name[(at + Separator.Length)..]);
        }

        private static string Plain(string name) =>
            name.Length == 0 ? name : NameEncoding.Unsanitize(name);

        private static string Text(Matrix4x4 m) =>
            string.Join(' ', new[]
            {
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44,
            }.Select(v => v.ToString(CultureInfo.InvariantCulture)));

        private static Matrix4x4? Matrix(string text)
        {
            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 16)
                return null;

            var v = new float[16];

            for (int i = 0; i < 16; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                    return null;

            return new Matrix4x4(
                v[0], v[1], v[2], v[3],
                v[4], v[5], v[6], v[7],
                v[8], v[9], v[10], v[11],
                v[12], v[13], v[14], v[15]);
        }
    }
}
