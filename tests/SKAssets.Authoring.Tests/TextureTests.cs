using Xunit;

namespace SKAssets.Authoring.Tests
{
    public sealed class TextureTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "skassets-textures-" + Guid.NewGuid().ToString("N"));

        public TextureTests() => Directory.CreateDirectory(_dir);

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        /// <summary>An uncompressed 32-bit TGA, the simplest image a DCC tool writes.</summary>
        private string Tga(int width, int height)
        {
            var bytes = new List<byte> { 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 32, 8 };
            for (int i = 0; i < width * height; i++) bytes.AddRange([(byte)(i * 16), 128, 255, 255]);

            string path = Path.Combine(_dir, "sword_d.tga");
            File.WriteAllBytes(path, [.. bytes]);
            return path;
        }

        /// <summary>An image a DCC tool writes comes out as a DDS with its mip chain.</summary>
        [Fact]
        public void AnImageBecomesADdsWithMipmaps()
        {
            string dds = Path.Combine(_dir, "out", "sword_d.dds");
            new DdsTextureConverter().Convert(Tga(16, 16), dds);

            byte[] written = File.ReadAllBytes(dds);
            Assert.Equal("DDS "u8.ToArray(), written[..4]);
            Assert.Equal(16, BitConverter.ToInt32(written, 12));   // height
            Assert.Equal(16, BitConverter.ToInt32(written, 16));   // width
            Assert.Equal(5, BitConverter.ToInt32(written, 28));    // 16, 8, 4, 2, 1
        }

        /// <summary>A texture already a DDS is the author's choice of format, and is copied untouched.</summary>
        [Fact]
        public void ADdsIsCopiedAsItIs()
        {
            string source = Path.Combine(_dir, "sword_n.dds");
            byte[] bytes = [.. "DDS "u8.ToArray(), .. Enumerable.Range(0, 200).Select(i => (byte)i)];
            File.WriteAllBytes(source, bytes);

            string dds = Path.Combine(_dir, "out", "sword_n.dds");
            new DdsTextureConverter().Convert(source, dds);

            Assert.Equal(bytes, File.ReadAllBytes(dds));
        }
    }
}
