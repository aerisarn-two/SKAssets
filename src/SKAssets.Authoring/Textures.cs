using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using StbImageSharp;

namespace SKAssets.Authoring
{
    /// <summary>Turns an image file into a DDS the game can read.</summary>
    public interface ITextureConverter
    {
        /// <summary>Writes <paramref name="source"/> to <paramref name="dds"/> as a DDS.</summary>
        void Convert(string source, string dds);
    }

    /// <summary>
    /// A DDS copied as it is; PNG, TGA, JPEG and BMP decoded and written as BC7 with mipmaps.
    /// </summary>
    /// <remarks>
    /// BC7 because Skyrim Special Edition reads it and it keeps both colour and alpha well, so
    /// one format serves a diffuse, a normal map and a cutout alike. A texture that has to be
    /// something else -- an uncompressed height map, a cube map -- should be authored as a DDS
    /// and is then copied untouched.
    /// </remarks>
    public sealed class DdsTextureConverter : ITextureConverter
    {
        /// <summary>The block compression written; BC7 by default.</summary>
        public CompressionFormat Format { get; init; } = CompressionFormat.Bc7;

        /// <summary>How hard the encoder tries; the fastest by default, which BC7 is good at.</summary>
        public CompressionQuality Quality { get; init; } = CompressionQuality.Fast;

        public void Convert(string source, string dds)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dds)!);

            if (string.Equals(Path.GetExtension(source), ".dds", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(source, dds, overwrite: true);
                return;
            }

            ImageResult image = ImageResult.FromMemory(File.ReadAllBytes(source), ColorComponents.RedGreenBlueAlpha);

            var encoder = new BcEncoder();
            encoder.OutputOptions.GenerateMipMaps = true;
            encoder.OutputOptions.Quality = Quality;
            encoder.OutputOptions.Format = Format;
            encoder.OutputOptions.FileFormat = OutputFileFormat.Dds;

            using var stream = File.Create(dds);
            encoder.EncodeToStream(image.Data, image.Width, image.Height, PixelFormat.Rgba32, stream);
        }
    }
}
