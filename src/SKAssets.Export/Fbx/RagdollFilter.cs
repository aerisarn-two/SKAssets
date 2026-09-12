using NIFSharp;

namespace SKAssets.Export.Fbx
{
    /// <summary>
    /// Havok's packed collision filter for a ragdoll body.
    /// </summary>
    /// <remarks>
    /// This is <c>hkpGroupFilter::calcFilterInfo</c>, and it is not data to be
    /// recovered so much as data to be allocated. The two five-bit identifier
    /// fields are relative: Havok's test is that two bodies skip collision when
    /// their <c>systemGroup</c> matches and one's <c>subSystemId</c> equals the
    /// other's <c>subSystemDontCollideWith</c>. Nothing outside the filter refers
    /// to the numbers, so any internally consistent assignment is correct.
    ///
    /// The layout, measured over the 954 ragdoll bodies of the 45 vanilla creatures
    /// that ship both files:
    ///
    /// <code>
    /// bits  0-4   layer                     0 on 954 of 954
    /// bits  5-9   subSystemId               1..30 observed
    /// bits 10-14  subSystemDontCollideWith  0..26 observed
    /// bits 16-31  systemGroup               1 in the ragdoll, 0 outside it
    /// </code>
    ///
    /// And one rule holds without exception: **a body's
    /// <c>subSystemDontCollideWith</c> is its parent body's
    /// <c>subSystemId</c></b>, with zero at the root — 954 of 954, and 45 of 45
    /// roots. That is what the field is for: stopping a forearm fighting the upper
    /// arm it hangs from.
    ///
    /// The identifiers themselves are not a function of bone or body order — the
    /// closest fit over the corpus is 61.9% — so vanilla's particular numbers are
    /// an allocation and are reproduced only by carrying them. What is generated
    /// here is *correct* rather than *identical*, which is the right thing for new
    /// content and the reason a byte-exact round trip still carries the original.
    /// </remarks>
    public static class RagdollFilter
    {
        /// <summary>The layer every vanilla ragdoll body uses.</summary>
        public const int RagdollLayer = 0;

        /// <summary>The system group a ragdoll's own bodies share.</summary>
        public const int RagdollSystemGroup = 1;

        /// <summary>And what the bodies outside the ragdoll use instead.</summary>
        public const int LooseSystemGroup = 0;

        /// <summary>Five bits, so the identifiers run 1 to 31.</summary>
        public const int MaxSubSystemId = 31;

        /// <summary>Packs the four fields the way Havok reads them.</summary>
        public static uint Pack(int layer, int systemGroup, int subSystemId, int dontCollideWith) =>
            ((uint)systemGroup << 16)
            | (((uint)dontCollideWith & 0x1Fu) << 10)
            | (((uint)subSystemId & 0x1Fu) << 5)
            | ((uint)layer & 0x1Fu);

        /// <summary>Unpacks one, for reading a file back.</summary>
        public static (int Layer, int SystemGroup, int SubSystemId, int DontCollideWith) Unpack(uint filter) =>
            ((int)(filter & 0x1Fu),
             (int)(filter >> 16),
             (int)((filter >> 5) & 0x1Fu),
             (int)((filter >> 10) & 0x1Fu));

        /// <summary>
        /// A filter for every body, from the shape of the ragdoll alone.
        /// </summary>
        /// <param name="bodies">
        /// The bodies, in the order identifiers should be handed out. Any order
        /// works; a stable one makes the result reproducible.
        /// </param>
        /// <param name="parentOf">
        /// Each body's parent, where it has one. A body absent from this is a root.
        /// </param>
        public static Dictionary<string, uint> Allocate(
            IReadOnlyList<string> bodies,
            IReadOnlyDictionary<string, string> parentOf)
        {
            ArgumentNullException.ThrowIfNull(bodies);
            ArgumentNullException.ThrowIfNull(parentOf);

            var id = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Handed out in order, wrapping at 31 because the field is five bits and
            // the frostbite spider has 48 bodies. Reuse is safe between bodies that
            // are never each other's parent, so a clash with the parent -- the one
            // pairing the field exists to express -- steps to the next free number.
            for (int i = 0; i < bodies.Count; i++)
            {
                int candidate = (i % MaxSubSystemId) + 1;

                if (parentOf.TryGetValue(bodies[i], out string? parent)
                    && id.TryGetValue(parent, out int parentId))
                {
                    for (int step = 0; step < MaxSubSystemId && candidate == parentId; step++)
                        candidate = (candidate % MaxSubSystemId) + 1;
                }

                id[bodies[i]] = candidate;
            }

            var filters = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

            foreach (string body in bodies)
            {
                int dontCollideWith =
                    parentOf.TryGetValue(body, out string? parent) && id.TryGetValue(parent, out int parentId)
                        ? parentId
                        : 0;

                filters[body] = Pack(RagdollLayer, RagdollSystemGroup, id[body], dontCollideWith);
            }

            return filters;
        }

        /// <summary>
        /// The parent of each body, read off the mesh's constraints.
        /// </summary>
        /// <remarks>
        /// A constraint sits on the body it moves — Havok's entity A — and names the
        /// body it hangs from as entity B. So the constraint graph is the ragdoll's
        /// hierarchy, and the mesh has it even though it has no ragdoll skeleton.
        /// </remarks>
        public static Dictionary<string, string> ParentsFrom(NifModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            var owner = new Dictionary<NifItem, string>();

            foreach (NifItem node in model.Blocks)
            {
                if (model.GetRef(node, "Collision Object") is not { } collision) continue;
                if (model.GetRef(collision, "Body") is not { } body) continue;
                if (model.GetName(node) is { Length: > 0 } name) owner[body] = name;
            }

            var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach ((NifItem body, string name) in owner)
            {
                foreach (NifItem constraint in model.GetRefArray(body, "Constraints"))
                {
                    NifItem descriptor = model.ConstraintDescriptor(constraint);
                    NifItem? other = model.GetRef(descriptor, "Entity B") ?? model.GetRef(constraint, "Entity B");

                    if (other is not null && owner.TryGetValue(other, out string? parent) && parent != name)
                    {
                        parents[name] = parent;
                        break;
                    }
                }
            }

            return parents;
        }

        /// <summary>The bodies a mesh carries, in block order.</summary>
        public static List<string> BodiesOf(NifModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            var bodies = new List<string>();

            foreach (NifItem node in model.Blocks)
            {
                if (model.GetRef(node, "Collision Object") is not { } collision) continue;
                if (model.GetRef(collision, "Body") is null) continue;
                if (model.GetName(node) is { Length: > 0 } name) bodies.Add(name);
            }

            return bodies;
        }
    }
}
