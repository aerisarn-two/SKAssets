using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using StbImageSharp;

namespace SKAssets.Authoring
{
    /// <summary>What converting a bought creature's textures wrote.</summary>
    /// <param name="Diffuse">The colour map, relative to the textures folder.</param>
    /// <param name="Normal">The normal map, relative to the textures folder.</param>
    /// <param name="Notes">What was decided, and what was made up rather than converted.</param>
    public sealed record BoughtTextureSet(string Diffuse, string Normal, IReadOnlyList<string> Notes)
    {
        public override string ToString() => $"{Diffuse} and {Normal}";
    }

    /// <summary>
    /// Turns a bought creature's textures into the pair Skyrim looks for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A creature's body fills two texture slots and may fill more: slot 0 is the colour map
    /// under the mesh's own name, slot 1 the normal map under that name and <c>_n</c>. The
    /// wolf fills those two and a <c>_sk</c> beside them; the player's body fills four. Two
    /// is the minimum, and a mesh with a colour map and no normal map is one the game draws
    /// flat and shining.
    /// </para>
    /// <para>
    /// An asset authored for a modern engine has neither of those two. It has a base colour,
    /// which is the colour map under another name, and it has its surface packed into one
    /// image a channel at a time -- occlusion, roughness and metalness -- which is how a
    /// physically-based shader wants it and is nothing Skyrim reads. What it has not got is a
    /// normal map at all, because the detail a normal map carries was baked into the
    /// high-poly source and thrown away.
    /// </para>
    /// <para>
    /// So the normal map is made rather than converted, and the making is deliberately dull: a
    /// flat surface, every pixel facing straight out, which is a normal map that says "no
    /// detail beyond the geometry". That is honest about what the asset knows. Inventing
    /// detail from the colour map's brightness is the tempting alternative and it is wrong --
    /// it carves grooves wherever the texture happens to be dark, so a skeleton's painted
    /// shadows become dents.
    /// </para>
    /// <para>
    /// The roughness is carried into the normal map's alpha, where Skyrim's shaders read a
    /// specular mask, inverted because a rough surface is a dull one. That much is the usual
    /// account of the format rather than something measured here: the game's own textures are
    /// not in the extracted corpus to check against. It is a safe thing to be wrong about --
    /// if the channel is read, a rough-looking creature is what the asset asked for, and if
    /// it is not, nothing looks at it.
    /// </para>
    /// </remarks>
    public static class BoughtTextures
    {
        /// <summary>A flat normal in Skyrim's tangent space: straight out of the surface.</summary>
        private const byte Flat = 128, Out = 255;

        /// <summary>Converts a bought set into the game's pair.</summary>
        /// <param name="baseColor">The colour map, in any format the converter reads.</param>
        /// <param name="folder">Where to write, which is the creature's textures folder.</param>
        /// <param name="stem">
        /// The name the mesh looks them up by: <c>BoneSkeleton</c> gives
        /// <c>BoneSkeleton.dds</c> and <c>BoneSkeleton_n.dds</c>.
        /// </param>
        /// <param name="surface">
        /// The packed surface map, occlusion in red, roughness in green and metalness in blue,
        /// as an engine that wants one writes it. Null makes a normal map of uniform shine.
        /// </param>
        /// <param name="normal">
        /// A real normal map, where the asset has one, which is used instead of a flat one.
        /// </param>
        /// <param name="converter">What writes a DDS; the ordinary one when null.</param>
        public static BoughtTextureSet Convert(
            string baseColor,
            string folder,
            string stem,
            string? surface = null,
            string? normal = null,
            ITextureConverter? converter = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(baseColor);
            ArgumentException.ThrowIfNullOrEmpty(folder);
            ArgumentException.ThrowIfNullOrEmpty(stem);

            var notes = new List<string>();
            ITextureConverter writer = converter ?? new DdsTextureConverter();
            Directory.CreateDirectory(folder);

            string diffuse = stem + ".dds";
            writer.Convert(baseColor, Path.Combine(folder, diffuse));
            notes.Add($"the colour map from '{Path.GetFileName(baseColor)}'");

            string normalName = stem + "_n.dds";
            string normalPath = Path.Combine(folder, normalName);

            if (normal is { Length: > 0 })
            {
                writer.Convert(normal, normalPath);
                notes.Add($"the normal map from '{Path.GetFileName(normal)}'");
            }
            else
            {
                ImageResult colour = Read(baseColor);
                byte[] shine = surface is { Length: > 0 } ? Shine(surface, colour.Width, colour.Height) : [];

                Flatten(normalPath, colour.Width, colour.Height, shine);

                notes.Add(shine.Length > 0
                    ? $"a flat normal map, {colour.Width}x{colour.Height}, since the asset has none, "
                      + $"with the shine taken from the roughness in '{Path.GetFileName(surface)}'"
                    : $"a flat normal map, {colour.Width}x{colour.Height}, since the asset has none, "
                      + "of uniform shine, since it has no roughness either");
            }

            return new BoughtTextureSet(diffuse, normalName, notes);
        }

        /// <summary>Writes a normal map of a flat surface, with a shine mask in its alpha.</summary>
        private static void Flatten(string dds, int width, int height, byte[] shine)
        {
            var pixels = new byte[width * height * 4];

            for (int i = 0; i < width * height; i++)
            {
                pixels[i * 4] = Flat;
                pixels[i * 4 + 1] = Flat;
                pixels[i * 4 + 2] = Out;
                pixels[i * 4 + 3] = shine.Length == width * height ? shine[i] : Out;
            }

            var encoder = new BcEncoder();
            encoder.OutputOptions.GenerateMipMaps = true;
            encoder.OutputOptions.Quality = CompressionQuality.Fast;
            encoder.OutputOptions.Format = CompressionFormat.Bc7;
            encoder.OutputOptions.FileFormat = OutputFileFormat.Dds;

            Directory.CreateDirectory(Path.GetDirectoryName(dds)!);
            using var stream = File.Create(dds);
            encoder.EncodeToStream(pixels, width, height, PixelFormat.Rgba32, stream);
        }

        /// <summary>
        /// The shine mask, from the roughness a packed surface map keeps in its green channel.
        /// </summary>
        /// <remarks>
        /// Inverted, because a rough surface is a dull one and the mask says how much a pixel
        /// shines. Resized by nearest pixel where the two images differ, which they should not,
        /// since a mismatch is a sign the wrong pair of files was handed over.
        /// </remarks>
        private static byte[] Shine(string surface, int width, int height)
        {
            ImageResult packed = Read(surface);
            var shine = new byte[width * height];

            for (int y = 0; y < height; y++)
            {
                int row = packed.Height == height ? y : y * packed.Height / height;
                for (int x = 0; x < width; x++)
                {
                    int column = packed.Width == width ? x : x * packed.Width / width;
                    byte roughness = packed.Data[(row * packed.Width + column) * 4 + 1];
                    shine[y * width + x] = (byte)(255 - roughness);
                }
            }

            return shine;
        }

        private static ImageResult Read(string path) =>
            ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
    }
}
