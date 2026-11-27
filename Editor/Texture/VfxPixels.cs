using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// An 8-bit RGBA image in a managed array, and the deterministic pixel steps of <c>import-texture</c>.
    /// </summary>
    /// <remarks>
    /// <para>Rows run bottom to top, as in <see cref="Texture2D.GetPixels32()"/>: index
    /// <c>((y * Width) + x) * 4</c> holds R, G, B, A of the pixel in column x, row y from the bottom.</para>
    /// <para>Every step uses integer arithmetic only (no GPU, no floating-point filter), so the same input
    /// gives the same bytes on every machine. Decoding and encoding use Unity's CPU PNG codec
    /// (<see cref="ImageConversion.LoadImage(Texture2D, byte[], bool)"/>,
    /// <see cref="ImageConversion.EncodeArrayToPNG(Array, GraphicsFormat, uint, uint, uint)"/>).</para>
    /// </remarks>
    public sealed class VfxPixels
    {
        /// <summary>The eight bytes every PNG file starts with.</summary>
        static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>Creates a transparent black image.</summary>
        public VfxPixels(int width, int height)
            : this(width, height, new byte[checked(width * height * 4)])
        {
        }

        /// <summary>Wraps RGBA bytes (bottom row first); the array is used, not copied.</summary>
        public VfxPixels(int width, int height, byte[] rgba)
        {
            if (width < 1 || height < 1)
                throw new ArgumentOutOfRangeException(nameof(width), $"image size {width}x{height} is empty");
            if (rgba == null || rgba.Length != width * height * 4)
                throw new ArgumentException($"expected {width * height * 4} bytes for {width}x{height} RGBA", nameof(rgba));
            Width = width;
            Height = height;
            Rgba = rgba;
        }

        /// <summary>Width in pixels.</summary>
        public int Width { get; }

        /// <summary>Height in pixels.</summary>
        public int Height { get; }

        /// <summary>The RGBA bytes, bottom row first.</summary>
        public byte[] Rgba { get; }

        /// <summary>The byte index of channel <paramref name="channel"/> (0 R, 1 G, 2 B, 3 A) of a pixel.</summary>
        public int Index(int x, int y, int channel) => (((y * Width) + x) * 4) + channel;

        /// <summary>A copy of the image.</summary>
        public VfxPixels Clone() => new VfxPixels(Width, Height, (byte[])Rgba.Clone());

        /// <summary>True when any pixel has an alpha below 255.</summary>
        public bool HasTranslucentAlpha()
        {
            for (int i = 3; i < Rgba.Length; i += 4)
                if (Rgba[i] != 255)
                    return true;
            return false;
        }

        /// <summary>True when <paramref name="bytes"/> starts with the PNG signature.</summary>
        public static bool IsPng(byte[] bytes)
        {
            if (bytes == null || bytes.Length < PngSignature.Length)
                return false;
            for (int i = 0; i < PngSignature.Length; i++)
                if (bytes[i] != PngSignature[i])
                    return false;
            return true;
        }

        /// <summary>Decodes a PNG to 8-bit RGBA (16-bit channels are reduced by Unity's decoder).</summary>
        /// <exception cref="FormatException">The bytes are not a PNG Unity can decode.</exception>
        public static VfxPixels DecodePng(byte[] png)
        {
            if (!IsPng(png))
                throw new FormatException("not a PNG file (the PNG signature is missing)");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!texture.LoadImage(png, false))
                    throw new FormatException("Unity could not decode the PNG");
                var pixels = texture.GetPixels32();
                var rgba = new byte[pixels.Length * 4];
                for (int i = 0; i < pixels.Length; i++)
                {
                    rgba[i * 4] = pixels[i].r;
                    rgba[(i * 4) + 1] = pixels[i].g;
                    rgba[(i * 4) + 2] = pixels[i].b;
                    rgba[(i * 4) + 3] = pixels[i].a;
                }
                return new VfxPixels(texture.width, texture.height, rgba);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Encodes the image as an 8-bit RGBA PNG.</summary>
        public byte[] EncodePng() =>
            ImageConversion.EncodeArrayToPNG(Rgba, GraphicsFormat.R8G8B8A8_UNorm, (uint)Width, (uint)Height, 0);

        /// <summary>Rec. 709 luminance of 8-bit RGB with integer weights (2126, 7152, 722 per 10000), rounded.</summary>
        public static int Luminance(int r, int g, int b) => ((2126 * r) + (7152 * g) + (722 * b) + 5000) / 10000;

        /// <summary><c>round(c * a / 255)</c> in integers (255 is odd, so a half never occurs).</summary>
        public static int MultiplyByAlpha(int c, int a) => ((c * a) + 127) / 255;

        /// <summary><c>min(255, round(c * 255 / a))</c>; 0 when <paramref name="a"/> is 0.</summary>
        public static int DivideByAlpha(int c, int a) => a == 0 ? 0 : Math.Min(255, ((c * 255) + (a / 2)) / a);

        /// <summary>Byte value of a constant in [0, 1]: <c>floor(v * 255 + 0.5)</c>.</summary>
        public static byte ConstantByte(double value) => (byte)Math.Floor((Math.Max(0, Math.Min(1, value)) * 255) + 0.5);

        /// <summary><c>luminance_to_alpha</c>: alpha becomes the luminance; <paramref name="color"/> is keep, white or unpremultiply.</summary>
        public void LuminanceToAlpha(string color)
        {
            for (int i = 0; i < Rgba.Length; i += 4)
            {
                int r = Rgba[i], g = Rgba[i + 1], b = Rgba[i + 2];
                var a = Luminance(r, g, b);
                switch (color)
                {
                    case "white":
                        Rgba[i] = Rgba[i + 1] = Rgba[i + 2] = 255;
                        break;
                    case "unpremultiply":
                        Rgba[i] = (byte)DivideByAlpha(r, a);
                        Rgba[i + 1] = (byte)DivideByAlpha(g, a);
                        Rgba[i + 2] = (byte)DivideByAlpha(b, a);
                        break;
                }
                Rgba[i + 3] = (byte)a;
            }
        }

        /// <summary><c>premultiply</c>: RGB multiplied by alpha.</summary>
        public void Premultiply()
        {
            for (int i = 0; i < Rgba.Length; i += 4)
            {
                int a = Rgba[i + 3];
                Rgba[i] = (byte)MultiplyByAlpha(Rgba[i], a);
                Rgba[i + 1] = (byte)MultiplyByAlpha(Rgba[i + 1], a);
                Rgba[i + 2] = (byte)MultiplyByAlpha(Rgba[i + 2], a);
            }
        }

        /// <summary>The value of a channel (r, g, b, a or luminance) at a byte offset of a pixel.</summary>
        public byte Channel(int pixelOffset, string channel)
        {
            switch (channel)
            {
                case "r": return Rgba[pixelOffset];
                case "g": return Rgba[pixelOffset + 1];
                case "b": return Rgba[pixelOffset + 2];
                case "a": return Rgba[pixelOffset + 3];
                case "luminance": return (byte)Luminance(Rgba[pixelOffset], Rgba[pixelOffset + 1], Rgba[pixelOffset + 2]);
                default: throw new ArgumentException($"unknown channel '{channel}'", nameof(channel));
            }
        }

        /// <summary>
        /// Each axis to its nearest power of two (geometric: n goes to p when n * n &lt; 2 * p * p for the
        /// power of two p &lt;= n &lt; 2p, else to 2p), then both axes halved until neither exceeds
        /// <paramref name="maxSize"/>.
        /// </summary>
        public static (int width, int height) PowerOfTwoSize(int width, int height, int maxSize)
        {
            var w = NearestPowerOfTwo(width);
            var h = NearestPowerOfTwo(height);
            while (Math.Max(w, h) > maxSize)
            {
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }
            return (w, h);
        }

        /// <summary>The nearest power of two of <paramref name="n"/> (geometric, ties up).</summary>
        public static int NearestPowerOfTwo(int n)
        {
            if (n < 1)
                throw new ArgumentOutOfRangeException(nameof(n));
            long p = 1;
            while (p * 2 <= n)
                p *= 2;
            return (long)n * n < 2 * p * p ? (int)p : (int)(p * 2);
        }

        /// <summary>
        /// Resamples to <paramref name="width"/> x <paramref name="height"/> with an exact area average
        /// (a box filter) in integer arithmetic: an output pixel is the coverage-weighted mean of the
        /// source pixels it covers, rounded once at the end. Upscaling repeats pixels (blending at the
        /// seams between them).
        /// </summary>
        public VfxPixels Resize(int width, int height)
        {
            if (width == Width && height == Height)
                return Clone();
            var columns = Weights(Width, width);
            var rows = Weights(Height, height);

            // Horizontal pass: unnormalised sums (at most 255 * Width per channel, fits an int).
            var horizontal = new int[width * Height * 4];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    foreach (var (index, weight) in columns[x])
                    {
                        var s = ((y * Width) + index) * 4;
                        r += Rgba[s] * weight;
                        g += Rgba[s + 1] * weight;
                        b += Rgba[s + 2] * weight;
                        a += Rgba[s + 3] * weight;
                    }
                    var d = ((y * width) + x) * 4;
                    horizontal[d] = r;
                    horizontal[d + 1] = g;
                    horizontal[d + 2] = b;
                    horizontal[d + 3] = a;
                }
            }

            // Vertical pass: the total weight of every output pixel is Width * Height.
            long total = (long)Width * Height;
            var output = new VfxPixels(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    long r = 0, g = 0, b = 0, a = 0;
                    foreach (var (index, weight) in rows[y])
                    {
                        var s = ((index * width) + x) * 4;
                        r += (long)horizontal[s] * weight;
                        g += (long)horizontal[s + 1] * weight;
                        b += (long)horizontal[s + 2] * weight;
                        a += (long)horizontal[s + 3] * weight;
                    }
                    var d = ((y * width) + x) * 4;
                    output.Rgba[d] = (byte)((r + (total / 2)) / total);
                    output.Rgba[d + 1] = (byte)((g + (total / 2)) / total);
                    output.Rgba[d + 2] = (byte)((b + (total / 2)) / total);
                    output.Rgba[d + 3] = (byte)((a + (total / 2)) / total);
                }
            }
            return output;
        }

        /// <summary>
        /// For each of <paramref name="target"/> output pixels, the source pixels it covers with integer
        /// weights. Coordinates are scaled by <c>source * target</c>: output j covers
        /// <c>[j * source, (j + 1) * source)</c>, source i covers <c>[i * target, (i + 1) * target)</c>,
        /// so the weights of one output sum to <paramref name="source"/>.
        /// </summary>
        static (int index, int weight)[][] Weights(int source, int target)
        {
            var weights = new (int, int)[target][];
            for (int j = 0; j < target; j++)
            {
                long start = (long)j * source, end = (long)(j + 1) * source;
                var first = (int)(start / target);
                var last = (int)((end - 1) / target);
                var list = new (int, int)[last - first + 1];
                for (int i = first; i <= last; i++)
                {
                    long lo = Math.Max(start, (long)i * target), hi = Math.Min(end, (long)(i + 1) * target);
                    list[i - first] = (i, (int)(hi - lo));
                }
                weights[j] = list;
            }
            return weights;
        }
    }
}
