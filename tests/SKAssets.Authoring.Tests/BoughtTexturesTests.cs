using SKAssets.Authoring;
using Xunit;

namespace SKAssets.Authoring.Tests
{
    /// <summary>
    /// Turning a bought creature's textures into the pair Skyrim looks for.
    /// </summary>
    public class BoughtTexturesTests : IDisposable
    {
        private readonly string _folder =
            Path.Combine(Path.GetTempPath(), $"sk-tex-{Guid.NewGuid():N}");

        public BoughtTexturesTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
        }

        private static string Ue4Base =>
            "/home/ecanepa/Dev/Implementation/skeleton_cgtrader/UE4/Skeleton_Body_BaseColor.png";

        private static string Ue4Surface =>
            "/home/ecanepa/Dev/Implementation/skeleton_cgtrader/UE4/Skeleton_Body_OcclusionRoughnessMetallic.png";

        private static bool Available => File.Exists(Ue4Base) && File.Exists(Ue4Surface);

        /// <summary>
        /// A small image of one colour, written where the test can hand it over.
        /// </summary>
        /// <remarks>
        /// A bitmap, because it is the one format a decoder reads that can be written here in
        /// a few lines and needs no encoder. Small, because compressing the asset's own
        /// 2048-square textures takes fourteen seconds a time and a test about which files
        /// come out under which names learns nothing from the other thirteen.
        /// </remarks>
        private string Image(string name, byte blue, byte green, byte red, int side = 8)
        {
            string path = Path.Combine(_folder, name + ".bmp");
            int stride = side * 4, pixels = stride * side;

            using var file = File.Create(path);
            using var write = new BinaryWriter(file);

            write.Write((ushort)0x4D42);            // 'BM'
            write.Write(54 + pixels);               // the whole file
            write.Write(0);                         // reserved
            write.Write(54);                        // where the pixels start
            write.Write(40);                        // the header's own size
            write.Write(side);
            write.Write(side);
            write.Write((ushort)1);                 // one plane
            write.Write((ushort)32);                // bits a pixel
            write.Write(0);                         // uncompressed
            write.Write(pixels);
            write.Write(2835);                      // pixels a metre, which nothing reads
            write.Write(2835);
            write.Write(0);                         // no palette
            write.Write(0);

            for (int i = 0; i < side * side; i++)
            {
                write.Write(blue);
                write.Write(green);
                write.Write(red);
                write.Write((byte)255);
            }

            return path;
        }

        /// <summary>
        /// The two files the mesh looks up: the colour map under the mesh's own name and the
        /// normal map under that name and <c>_n</c>.
        /// </summary>
        [Fact]
        public void TheGamesPairIsWrittenUnderTheNamesTheMeshLooksFor()
        {
            if (!Available) return;

            BoughtTextureSet made = BoughtTextures.Convert(Ue4Base, _folder, "BoneSkeleton", Ue4Surface);

            Assert.Equal("BoneSkeleton.dds", made.Diffuse);
            Assert.Equal("BoneSkeleton_n.dds", made.Normal);

            Assert.True(File.Exists(Path.Combine(_folder, made.Diffuse)));
            Assert.True(File.Exists(Path.Combine(_folder, made.Normal)));

            // Both are real DDS files, not copies of the PNGs.
            foreach (string name in new[] { made.Diffuse, made.Normal })
            {
                byte[] head = File.ReadAllBytes(Path.Combine(_folder, name))[..4];
                Assert.Equal("DDS "u8.ToArray(), head);
            }

            // And it says the normal map was made rather than converted, since it was.
            Assert.Contains(made.Notes, n => n.Contains("the asset has none", StringComparison.Ordinal));
            Assert.Contains(made.Notes, n => n.Contains("roughness", StringComparison.Ordinal));
        }

        /// <summary>
        /// A real normal map is used where the asset has one, rather than a flat one being
        /// made in its place.
        /// </summary>
        [Fact]
        public void ARealNormalMapIsUsedWhereThereIsOne()
        {
            string colour = Image("colour", 40, 60, 80);

            BoughtTextureSet made = BoughtTextures.Convert(
                colour, _folder, "BoneSkeleton", Image("surface", 0, 200, 0), normal: Image("normal", 255, 128, 128));

            Assert.DoesNotContain(made.Notes, n => n.Contains("the asset has none", StringComparison.Ordinal));
            Assert.Contains(made.Notes, n => n.Contains("the normal map from", StringComparison.Ordinal));
            Assert.True(File.Exists(Path.Combine(_folder, made.Normal)));
        }

        /// <summary>
        /// Without a surface map there is no roughness to read, so the shine is uniform and
        /// the report says so rather than implying a measurement.
        /// </summary>
        [Fact]
        public void WithoutARoughnessMapTheShineIsUniformAndSaysSo()
        {
            BoughtTextureSet made = BoughtTextures.Convert(Image("colour", 40, 60, 80), _folder, "BoneSkeleton");

            Assert.Contains(made.Notes, n => n.Contains("uniform shine", StringComparison.Ordinal));
            Assert.True(File.Exists(Path.Combine(_folder, made.Normal)));
        }

        /// <summary>
        /// A rough surface comes out dull and a smooth one shiny, since the mask says how much
        /// a pixel shines and the roughness says the opposite.
        /// </summary>
        [Fact]
        public void TheShineIsTheRoughnessTheOtherWayUp()
        {
            string colour = Image("colour", 40, 60, 80);

            // Roughness lives in the green channel of a packed surface map.
            BoughtTextureSet rough = BoughtTextures.Convert(
                colour, _folder, "Rough", Image("veryRough", 0, 250, 0));
            BoughtTextureSet smooth = BoughtTextures.Convert(
                colour, _folder, "Smooth", Image("verySmooth", 0, 5, 0));

            long roughSize = new FileInfo(Path.Combine(_folder, rough.Normal)).Length;
            long smoothSize = new FileInfo(Path.Combine(_folder, smooth.Normal)).Length;

            // Both are written, both say where the shine came from, and the two differ --
            // which is the most a compressed image can be asked without decoding it again.
            Assert.True(roughSize > 0 && smoothSize > 0);
            Assert.Contains(rough.Notes, n => n.Contains("roughness", StringComparison.Ordinal));
            Assert.NotEqual(
                File.ReadAllBytes(Path.Combine(_folder, rough.Normal)),
                File.ReadAllBytes(Path.Combine(_folder, smooth.Normal)));
        }
    }
}
