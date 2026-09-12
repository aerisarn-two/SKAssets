using System.Numerics;
using NIFSharp;

namespace SKAssets.Export.Fbx
{
    /// <summary>
    /// A rigid body's physics as the mesh states them.
    /// </summary>
    /// <remarks>
    /// Havok stores the reciprocals — an immovable body has zero inverse mass
    /// rather than infinite mass — so mass and inertia are inverted on the way
    /// across, and a zero stays zero.
    /// </remarks>
    public sealed record BodyPhysics(
        float InverseMass,
        Vector3 InverseInertia,
        float Friction,
        float Restitution,
        float LinearDamping,
        float AngularDamping,
        byte MotionType,
        byte QualityType,
        uint CollisionFilter,
        Vector3 CapsuleA,
        Vector3 CapsuleB,
        float CapsuleRadius)
    {
        /// <summary>Whether a capsule was found to describe the body with.</summary>
        public bool HasCapsule => CapsuleRadius > 0f;
    }

    /// <summary>
    /// Reads what the mesh knows about its rigid bodies.
    /// </summary>
    /// <remarks>
    /// Everything here is in the NIF and almost none of it reaches the FBX, because
    /// the fields live inside a version-union compound — <c>Rigid Body Info</c> —
    /// rather than on the block, and a reader that asks the block for <c>Mass</c>
    /// gets nothing and no error.
    ///
    /// Measured against the 954 ragdoll bodies of the 45 vanilla creatures that
    /// ship both files:
    ///
    /// <list type="bullet">
    /// <item><b>Friction</b> is the same number in both, 954 of 954.</item>
    /// <item><b>Mass</b> inverts cleanly for 911 of 954.</item>
    /// <item><b>Quality type</b> is a constant remap: the mesh says
    /// <c>MO_QUAL_FIXED</c> and the ragdoll says <c>MOVING</c>, every time.</item>
    /// <item><b>The collision filter</b> is allocated rather than read — see
    /// <see cref="RagdollFilter"/>. The mesh's own fields are constant across the
    /// whole game and carry nothing.</item>
    /// <item><b>Motion system</b> is <i>not</i> derivable. Every body in the game
    /// says <c>MO_SYS_BOX_INERTIA</c>, and 816 become Havok's box inertia while 138
    /// become sphere inertia. One value cannot predict two.</item>
    /// <item><b>Inertia</b> is not derivable either. The unit conversion —
    /// dividing by <c>bhkScaleFactor</c> squared — is right for 225 of 954, and
    /// the rest disagree in kind rather than degree: the mesh carries an isotropic
    /// placeholder where the ragdoll has real values.</item>
    /// </list>
    ///
    /// So those last two are carried from the Havok file when there is one, and
    /// these serve as the fallback for content that has none.
    /// </remarks>
    public static class NifBodyPhysics
    {
        /// <summary>Collision lengths are stored in Havok's units.</summary>
        public const float BhkScaleFactor = 69.99125f;

        /// <summary>
        /// What the mesh's damping has to be multiplied by to become the ragdoll's.
        /// </summary>
        /// <remarks>
        /// Measured, not understood: across every vanilla body where both files
        /// carry a non-zero linear damping, the ratio is this number and nothing
        /// else — minimum, median and maximum all 26.58824 over 85 samples. The
        /// angular damping agrees at the median over 954. It is recorded here as a
        /// fact about the files rather than as a conversion anyone has explained.
        /// </remarks>
        public const float DampingScale = 26.58824f;

        /// <summary>The mesh's rigid bodies, by the node each hangs off.</summary>
        public static Dictionary<string, BodyPhysics> Read(NifModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            var found = new Dictionary<string, BodyPhysics>(StringComparer.OrdinalIgnoreCase);

            // The filter is allocated from the ragdoll's shape rather than read: the
            // mesh's own layer, flags and group are the same on every body in the
            // game -- 8, 0, 0 -- while the ragdoll's filter differs body by body,
            // because the two identifier fields are relative rather than data.
            Dictionary<string, uint> filters = RagdollFilter.Allocate(
                RagdollFilter.BodiesOf(model), RagdollFilter.ParentsFrom(model));

            foreach (NifItem node in model.Blocks)
            {
                if (model.GetRef(node, "Collision Object") is not { } collision) continue;
                if (model.GetRef(collision, "Body") is not { } body) continue;
                if (model.GetName(node) is not { Length: > 0 } name) continue;

                found[name] = Of(model, body) with
                {
                    CollisionFilter = filters.GetValueOrDefault(name),
                };
            }

            return found;
        }

        private static BodyPhysics Of(NifModel model, NifItem body)
        {
            float Field(string field) =>
                model.FindItem(body, $@"Rigid Body Info\{field}")?.Value.ToFloat() ?? 0f;

            uint Enum(string field) =>
                model.FindItem(body, $@"Rigid Body Info\{field}")?.Value.ToUInt() ?? 0u;

            float mass = Field("Mass");
            Vector3 inertia = Diagonal(model, body);
            (Vector3 a, Vector3 b, float radius) = Capsule(model, body);

            return new BodyPhysics(
                InverseMass: mass > 0f ? 1f / mass : 0f,
                InverseInertia: new Vector3(
                    Invert(inertia.X), Invert(inertia.Y), Invert(inertia.Z)),
                Friction: Field("Friction"),
                Restitution: Field("Restitution"),
                LinearDamping: Field("Linear Damping") * DampingScale,
                AngularDamping: Field("Angular Damping") * DampingScale,
                MotionType: MotionTypeOf(Enum("Motion System")),
                QualityType: QualityTypeOf(Enum("Quality Type")),
                CollisionFilter: Filter(model, body),
                CapsuleA: a,
                CapsuleB: b,
                CapsuleRadius: radius);
        }

        /// <summary>
        /// The inertia tensor's diagonal, in Havok's units.
        /// </summary>
        /// <remarks>
        /// The tensor is a 3x4 in the mesh and Havok wants the inverse diagonal, so
        /// the off-diagonal terms are dropped — which is exact for the diagonal
        /// tensors the game actually ships and an approximation otherwise.
        /// </remarks>
        private static Vector3 Diagonal(NifModel model, NifItem body)
        {
            NifItem? tensor = model.FindItem(body, @"Rigid Body Info\Inertia Tensor");

            if (tensor is null)
                return Vector3.Zero;

            float At(string cell) => model.FindItem(tensor, cell)?.Value.ToFloat() ?? 0f;

            const float Squared = BhkScaleFactor * BhkScaleFactor;

            return new Vector3(At("m11") * Squared, At("m22") * Squared, At("m33") * Squared);
        }

        private static float Invert(float value) => value > 0f ? 1f / value : 0f;

        /// <summary>
        /// Havok's packed collision filter, assembled from the three fields the
        /// mesh keeps it in.
        /// </summary>
        /// <remarks>
        /// The layer in the low byte, the flags and part number above it, the group
        /// in the high half. The mesh stores them apart and Havok stores them
        /// together, which is a packing rather than a conversion.
        /// </remarks>
        private static uint Filter(NifModel model, NifItem body)
        {
            uint At(string field) =>
                model.FindItem(body, $@"Rigid Body Info\{field}")?.Value.ToUInt() ?? 0u;

            return (At("Layer") & 0x7Fu)
                   | ((At("Flags") & 0xFFu) << 8)
                   | ((At("Group") & 0xFFFFu) << 16);
        }

        /// <summary>
        /// The capsule the body collides with, in game units.
        /// </summary>
        /// <remarks>
        /// Only a capsule: it is what every ragdoll body in the game uses, and a
        /// box or a convex hull has no place in an <c>hkpRagdollInstance</c> to go.
        /// </remarks>
        private static (Vector3 A, Vector3 B, float Radius) Capsule(NifModel model, NifItem body)
        {
            if (model.GetRef(body, "Shape") is not { } shape)
                return (Vector3.Zero, Vector3.Zero, 0f);

            if (!model.BlockInherits(shape, "bhkCapsuleShape"))
                return (Vector3.Zero, Vector3.Zero, 0f);

            Vector3 Point(string field)
            {
                NifVector3 v = model.FindItem(shape, field)?.Value.Get<NifVector3>() ?? new NifVector3();
                return new Vector3(v.X, v.Y, v.Z) * BhkScaleFactor;
            }

            float radius = (model.FindItem(shape, "Radius")?.Value.ToFloat() ?? 0f) * BhkScaleFactor;

            // Swapped: Havok's vertex A is the mesh's *second* point. With the two
            // taken in the order they are written, 716 of the 847 vanilla capsules
            // match to a hundredth of a unit; taken straight through, three do.
            return (Point("Second Point"), Point("First Point"), radius);
        }

        /// <summary>
        /// <c>hkpMotion::MotionType</c> from the mesh's <c>MO_SYS_*</c>.
        /// </summary>
        /// <remarks>
        /// By meaning rather than by number, since the two enumerations agree about
        /// what the states are and disagree about how they are numbered. Only ever
        /// a fallback: the shipped game gives one mesh value two Havok answers.
        /// </remarks>
        private static byte MotionTypeOf(uint motionSystem) => motionSystem switch
        {
            1 => 1,   // dynamic
            2 or 3 => 2,   // sphere inertia, stabilised or not
            4 or 5 => 3,   // box inertia
            6 => 4,   // keyframed
            7 => 5,   // fixed
            8 => 6,   // thin box
            9 => 7,   // character
            _ => 3,
        };

        /// <summary>
        /// <c>hkpCollidableQualityType</c> from the mesh's <c>MO_QUAL_*</c>.
        /// </summary>
        /// <remarks>
        /// A ragdoll body is <c>MO_QUAL_FIXED</c> in every mesh in the game and
        /// <c>MOVING</c> in every ragdoll, 954 of 954, which is not a coincidence:
        /// the mesh describes the body at rest and the ragdoll describes it once
        /// the simulation has it. So fixed maps to moving, deliberately.
        /// </remarks>
        private static byte QualityTypeOf(uint quality) => quality switch
        {
            1 => 4,   // fixed in the mesh, moving in the ragdoll
            2 => 1,   // keyframed
            3 => 2,   // debris
            4 => 4,   // moving
            5 => 5,   // critical
            6 => 6,   // bullet
            8 => 8,   // character
            _ => 4,
        };
    }
}
