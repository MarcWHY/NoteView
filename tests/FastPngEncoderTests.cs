using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoteView
{
    public static class FastPngEncoderTests
    {
        private static int checks;
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                var encoder = new FastPngEncoder();
                byte[] matrix = new byte[256 * 256 * 4];
                for (int alpha = 0; alpha < 256; alpha++)
                    for (int channel = 0; channel < 256; channel++)
                    {
                        int p = (alpha * 256 + channel) * 4;
                        matrix[p] = (byte)Math.Min(alpha, channel);
                        matrix[p + 1] = (byte)(alpha - matrix[p]);
                        matrix[p + 2] = (byte)(alpha == 0 ? 0 : channel % (alpha + 1));
                        matrix[p + 3] = (byte)alpha;
                    }
                foreach (byte filter in new byte[] { 0, 1, 2 })
                { encoder.RowFilter = filter; RoundTrip(encoder, matrix, 256, 256, 1024, "all-alpha-matrix filter=" + filter); }
                var random = new Random(704);
                foreach (int width in new[] { 1, 3, 41, 1680 })
                {
                    int height = width == 1680 ? 960 : 27, stride = width * 4 + 12;
                    byte[] pixels = new byte[stride * height]; random.NextBytes(pixels);
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            int p = y * stride + x * 4, a = pixels[p + 3];
                            for (int c = 0; c < 3; c++) pixels[p + c] = (byte)(pixels[p + c] * a / 255);
                        }
                    RoundTrip(encoder, pixels, width, height, stride, "random-" + width);
                }
                CheckThrows(delegate { encoder.EncodePbgra32(null, 1, 1, 4); }, "null buffer rejected");
                CheckThrows(delegate { encoder.EncodePbgra32(new byte[4], 0, 1, 4); }, "zero dimensions rejected");
                CheckThrows(delegate { encoder.EncodePbgra32(new byte[4], 2, 1, 8); }, "short buffer rejected");
                CheckThrows(delegate { encoder.EncodePbgra32(new byte[8], 2, 1, 4); }, "short stride rejected");
                CheckThrows(delegate { encoder.EncodePbgra32(new byte[4], 1, -1, 4); }, "negative height rejected");
                CheckThrows(delegate { encoder.EncodePbgra32(new byte[4], 1, 1, -4); }, "negative stride rejected");
                CheckThrows(delegate { encoder.EncodePbgra32(new byte[4], int.MaxValue, 1, 4); }, "row-byte overflow rejected before pinning");
                CheckThrows(delegate { encoder.EncodePbgra32(new byte[4], 1, int.MaxValue, int.MaxValue); }, "stride-times-height overflow rejected before pinning");
                RoundTrip(encoder, new byte[] { 37, 77, 103, 255 }, 1, 1, int.MaxValue, "last-row padding is not read");

                foreach (string path in Directory.GetFiles("artifacts/obs-renderer-tests", "*.png"))
                {
                    BitmapSource source = Load(File.ReadAllBytes(path));
                    byte[] expected = Pixels(source);
                    byte[] png = encoder.Encode(source);
                    CheckPixels(expected, Pixels(Load(png)), Path.GetFileName(path));
                    Check(source.PixelWidth == Load(png).PixelWidth && source.PixelHeight == Load(png).PixelHeight, "real image dimensions preserved");
                    CheckChunks(png);
                    File.WriteAllBytes(Path.Combine("artifacts/fast-png-tests", Path.GetFileName(path)), png);
                    if (args.Length > 0 && args[0] == "--benchmark")
                        foreach (byte filter in new byte[] { 0, 1, 2 })
                        { encoder.RowFilter = filter; Benchmark(encoder, source, Path.GetFileName(path) + " filter=" + filter); }
                }
                Console.WriteLine("FastPngEncoderTests: PASS (" + checks + " assertions).");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void RoundTrip(FastPngEncoder encoder, byte[] pixels, int width, int height, int stride, string label)
        {
            byte[] png = encoder.EncodePbgra32(pixels, width, height, stride);
            var packed = new byte[width * height * 4];
            for (int y = 0; y < height; y++) Buffer.BlockCopy(pixels, y * stride, packed, y * width * 4, width * 4);
            BitmapSource decoded = Load(png);
            Check(decoded.PixelWidth == width && decoded.PixelHeight == height, label + " dimensions");
            CheckPixels(packed, Pixels(decoded), label);
            CheckChunks(png);
        }

        private static void CheckChunks(byte[] png)
        {
            Check(png.Length > 60 && png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71, "PNG signature");
            int cursor = 8, chunks = 0;
            while (cursor < png.Length)
            {
                int length = checked((int)ReadUInt32(png, cursor)); cursor += 4;
                uint crc = 0xffffffffu;
                for (int i = 0; i < length + 4; i++)
                {
                    crc ^= png[cursor + i];
                    for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
                }
                Check((crc ^ 0xffffffffu) == ReadUInt32(png, cursor + length + 4), "independent bitwise chunk CRC");
                if (png[cursor] == 73 && png[cursor + 1] == 68)
                {
                    int start = cursor + 4;
                    Check((png[start] * 256 + png[start + 1]) % 31 == 0 && (png[start + 1] & 32) == 0,
                        "zlib header checksum and no external dictionary");
                    using (var input = new MemoryStream(png, start + 2, length - 6))
                    using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                    {
                        uint a = 1, b = 0;
                        var buffer = new byte[8192]; int count;
                        while ((count = deflate.Read(buffer, 0, buffer.Length)) > 0)
                            for (int i = 0; i < count; i++) { a = (a + buffer[i]) % 65521; b = (b + a) % 65521; }
                        Check(((b << 16) | a) == ReadUInt32(png, start + length - 4), "independent per-byte Adler32 over inflated data");
                    }
                }
                cursor += length + 8; chunks++;
            }
            Check(cursor == png.Length && chunks == 3, "complete IHDR/IDAT/IEND PNG with no trailing data");
        }

        private static void Benchmark(FastPngEncoder encoder, BitmapSource source, string label)
        {
            double fast = 0, wic = 0; int fastBytes = 0, wicBytes = 0;
            byte[] pixels = Pixels(source);
            var watch = new Stopwatch();
            for (int i = -3; i < 20; i++)
            {
                watch.Restart(); byte[] result = encoder.EncodePbgra32(pixels, source.PixelWidth, source.PixelHeight, source.PixelWidth * 4); watch.Stop();
                if (i >= 0) { fast += watch.Elapsed.TotalMilliseconds; fastBytes = result.Length; }
                watch.Restart(); result = EncodeWic(source); watch.Stop();
                if (i >= 0) { wic += watch.Elapsed.TotalMilliseconds; wicBytes = result.Length; }
            }
            Console.WriteLine(label + ": WIC=" + (wic / 20).ToString("F2") + "ms / " + wicBytes + "B; fast=" +
                (fast / 20).ToString("F2") + "ms / " + fastBytes + "B; speedup=" + (wic / fast).ToString("F2") + "x");
            if (encoder.RowFilter == 2)
            {
                watch.Restart(); typeof(FastPngEncoder).GetMethod("FilterRows", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(encoder, new object[] { pixels, source.PixelWidth, source.PixelHeight, source.PixelWidth * 4, source.PixelWidth * 4 });
                watch.Stop(); double filterTime = watch.Elapsed.TotalMilliseconds;
                var filtered = (byte[])typeof(FastPngEncoder).GetField("filtered", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(encoder);
                int length = (source.PixelWidth * 4 + 1) * source.PixelHeight;
                string deflaterName;
                watch.Restart(); using (var stream = new MemoryStream())
                using (var deflate = new DeflateStream(stream, CompressionLevel.Fastest))
                {
                    var deflaterField = typeof(DeflateStream).GetField("deflater", BindingFlags.NonPublic | BindingFlags.Instance);
                    deflaterName = deflaterField == null ? "unknown" : deflaterField.GetValue(deflate).GetType().Name;
                    deflate.Write(filtered, 0, length);
                }
                watch.Stop();
                Console.WriteLine("Stages: filter+adler=" + filterTime.ToString("F2") +
                    "; deflate=" + watch.Elapsed.TotalMilliseconds.ToString("F2") + "ms; " + deflaterName);
            }
        }
        private static byte[] EncodeWic(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = new MemoryStream()) { encoder.Save(stream); return stream.ToArray(); }
        }
        private static BitmapSource Load(byte[] png)
        {
            using (var stream = new MemoryStream(png))
            {
                BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                var result = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0); result.Freeze(); return result;
            }
        }
        private static byte[] Pixels(BitmapSource source)
        { var pixels = new byte[source.PixelWidth * source.PixelHeight * 4]; source.CopyPixels(pixels, source.PixelWidth * 4, 0); return pixels; }
        private static void CheckPixels(byte[] expected, byte[] actual, string label)
        {
            Check(expected.Length == actual.Length, label + " pixel buffer size");
            for (int i = 0; i < expected.Length; i++)
                if (expected[i] != actual[i]) throw new Exception(label + " pixel mismatch at " + i + ": " + expected[i] + " != " + actual[i]);
            Check(true, label + " exact premultiplied RGBA round trip");
        }
        private static uint ReadUInt32(byte[] data, int offset)
        { return (uint)data[offset] << 24 | (uint)data[offset + 1] << 16 | (uint)data[offset + 2] << 8 | data[offset + 3]; }
        private static void CheckThrows(Action action, string label)
        { bool threw = false; try { action(); } catch (ArgumentException) { threw = true; } catch (OverflowException) { threw = true; } Check(threw, label); }
        private static void Check(bool condition, string label)
        { checks++; if (!condition) throw new Exception("FAILED: " + label); }
    }
}
