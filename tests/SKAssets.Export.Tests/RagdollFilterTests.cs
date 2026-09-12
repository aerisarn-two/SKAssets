using SKAssets.Export.Fbx;
using Xunit;

namespace SKAssets.Export.Tests
{
    /// <summary>
    /// The packed collision filter, and the one rule that governs it.
    /// </summary>
    /// <remarks>
    /// The hierarchies here are written out rather than read from a creature,
    /// because what is being checked is the allocation: that a body never shares an
    /// identifier with its parent, that the wrap at 31 does not break that, and that
    /// the packing is the layout the vanilla files use.
    /// </remarks>
    public class RagdollFilterTests
    {
        /// <summary>A chain of *count* bodies, each hanging off the last.</summary>
        private static (List<string> Bodies, Dictionary<string, string> Parents) Chain(int count)
        {
            var bodies = new List<string>();
            var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                bodies.Add($"b{i}");
                if (i > 0) parents[$"b{i}"] = $"b{i - 1}";
            }

            return (bodies, parents);
        }

        [Fact]
        public void PackAndUnpackAreInverses()
        {
            uint packed = RagdollFilter.Pack(layer: 0, systemGroup: 1, subSystemId: 6, dontCollideWith: 5);
            (int layer, int group, int sub, int dont) = RagdollFilter.Unpack(packed);

            Assert.Equal(0, layer);
            Assert.Equal(1, group);
            Assert.Equal(6, sub);
            Assert.Equal(5, dont);
        }

        /// <summary>
        /// The layout the shipped files use, checked against a value read out of one:
        /// the cow's head body, whose filter is 0x000114C0.
        /// </summary>
        [Fact]
        public void ThePackingMatchesAVanillaValue()
        {
            (int layer, int group, int sub, int dont) = RagdollFilter.Unpack(0x000114C0u);

            Assert.Equal(0, layer);
            Assert.Equal(1, group);
            Assert.Equal(6, sub);
            Assert.Equal(5, dont);

            Assert.Equal(0x000114C0u, RagdollFilter.Pack(layer, group, sub, dont));
        }

        [Fact]
        public void ARootDoesNotAvoidAnything()
        {
            (var bodies, var parents) = Chain(1);

            uint filter = RagdollFilter.Allocate(bodies, parents)["b0"];

            Assert.Equal(0, RagdollFilter.Unpack(filter).DontCollideWith);
        }

        /// <summary>
        /// The rule, which holds for 954 of 954 vanilla bodies: a body avoids its
        /// parent, named by the parent's own identifier.
        /// </summary>
        [Fact]
        public void EveryBodyAvoidsItsParent()
        {
            (var bodies, var parents) = Chain(12);
            var filters = RagdollFilter.Allocate(bodies, parents);

            foreach (string body in bodies)
            {
                if (!parents.TryGetValue(body, out string? parent)) continue;

                int dont = RagdollFilter.Unpack(filters[body]).DontCollideWith;
                int parentId = RagdollFilter.Unpack(filters[parent]).SubSystemId;

                Assert.Equal(parentId, dont);
            }
        }

        [Fact]
        public void EveryBodyIsInTheRagdollsSystemGroupAndLayer()
        {
            (var bodies, var parents) = Chain(6);

            foreach (uint filter in RagdollFilter.Allocate(bodies, parents).Values)
            {
                (int layer, int group, _, _) = RagdollFilter.Unpack(filter);
                Assert.Equal(RagdollFilter.RagdollLayer, layer);
                Assert.Equal(RagdollFilter.RagdollSystemGroup, group);
            }
        }

        /// <summary>
        /// Five bits hold 31 identifiers and the frostbite spider has 48 bodies, so
        /// reuse is not optional. What must not happen is a body sharing with its own
        /// parent, which is the one pairing the field exists to express.
        /// </summary>
        [Fact]
        public void NoBodySharesAnIdentifierWithItsParentEvenPastTheWrap()
        {
            (var bodies, var parents) = Chain(48);
            var filters = RagdollFilter.Allocate(bodies, parents);

            foreach (string body in bodies)
            {
                if (!parents.TryGetValue(body, out string? parent)) continue;

                int mine = RagdollFilter.Unpack(filters[body]).SubSystemId;
                int theirs = RagdollFilter.Unpack(filters[parent]).SubSystemId;

                Assert.NotEqual(theirs, mine);
            }
        }

        [Fact]
        public void IdentifiersStayInsideTheFiveBitsAvailable()
        {
            (var bodies, var parents) = Chain(48);

            foreach (uint filter in RagdollFilter.Allocate(bodies, parents).Values)
            {
                (_, _, int sub, int dont) = RagdollFilter.Unpack(filter);
                Assert.InRange(sub, 1, RagdollFilter.MaxSubSystemId);
                Assert.InRange(dont, 0, RagdollFilter.MaxSubSystemId);
            }
        }

        /// <summary>
        /// A tree rather than a chain, since a ragdoll branches: two legs and a neck
        /// off one pelvis, which is the shape the chicken has.
        /// </summary>
        [Fact]
        public void SiblingsMayShareNothingWithTheParentTheyHangFrom()
        {
            var bodies = new List<string> { "pelvis", "leftLeg", "rightLeg", "neck", "head" };
            var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["leftLeg"] = "pelvis",
                ["rightLeg"] = "pelvis",
                ["neck"] = "pelvis",
                ["head"] = "neck",
            };

            var filters = RagdollFilter.Allocate(bodies, parents);
            int pelvis = RagdollFilter.Unpack(filters["pelvis"]).SubSystemId;

            foreach (string child in new[] { "leftLeg", "rightLeg", "neck" })
                Assert.Equal(pelvis, RagdollFilter.Unpack(filters[child]).DontCollideWith);

            Assert.Equal(
                RagdollFilter.Unpack(filters["neck"]).SubSystemId,
                RagdollFilter.Unpack(filters["head"]).DontCollideWith);
        }

        [Fact]
        public void AllocationIsReproducible()
        {
            (var bodies, var parents) = Chain(20);

            Assert.Equal(
                RagdollFilter.Allocate(bodies, parents),
                RagdollFilter.Allocate(bodies, parents));
        }

        /// <summary>
        /// A body outside the ragdoll keeps the loose system group rather than the
        /// ragdoll's, so it collides with the ragdoll normally.
        /// </summary>
        [Fact]
        public void ABodyOutsideTheRagdollIsNotInItsSystemGroup()
        {
            uint loose = RagdollFilter.Pack(
                RagdollFilter.RagdollLayer, RagdollFilter.LooseSystemGroup, 0, 0);

            Assert.Equal(RagdollFilter.LooseSystemGroup, RagdollFilter.Unpack(loose).SystemGroup);
            Assert.NotEqual(RagdollFilter.RagdollSystemGroup, RagdollFilter.Unpack(loose).SystemGroup);
        }

        /// <summary>
        /// Zero is a value a real file carries -- 172 vanilla bodies have it, being
        /// the ones outside the ragdoll -- so it cannot double as "absent".
        /// </summary>
        [Fact]
        public void ZeroIsAFilterAndNotAnAbsence()
        {
            (int layer, int group, int sub, int dont) = RagdollFilter.Unpack(0u);

            Assert.Equal(0, layer);
            Assert.Equal(0, group);
            Assert.Equal(0, sub);
            Assert.Equal(0, dont);

            // And it is what a loose body legitimately gets.
            Assert.Equal(0u, RagdollFilter.Pack(0, RagdollFilter.LooseSystemGroup, 0, 0));
        }
    }
}
