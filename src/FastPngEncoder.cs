using System;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoteView
{
    /// <summary>
    /// Reusable, single-threaded PNG encoder for live WPF output. It preserves
    /// premultiplied display pixels while favoring latency over smallest files.
    /// PNG layout: https://www.w3.org/TR/png-3/ .
    /// </summary>
    public sealed class FastPngEncoder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly byte[] Ihdr = { 73, 72, 68, 82 }, Idat = { 73, 68, 65, 84 }, Iend = { 73, 69, 78, 68 };
        private static readonly byte[] Unpremultiply = BuildUnpremultiply();
        private static readonly uint[] CrcTable = BuildCrcTable();
        private readonly MemoryStream compressed = new MemoryStream(512 * 1024);
        private readonly MemoryStream output = new MemoryStream(512 * 1024);
        private readonly byte[] header = new byte[13];
        private byte[] pixels = new byte[0], filtered = new byte[0], previous = new byte[0];
        internal byte RowFilter = 2;

        public byte[] Encode(BitmapSource source)
        {
            if (source == null) throw new ArgumentNullException("source");
            BitmapSource premultiplied = source.Format == PixelFormats.Pbgra32 ? source :
                new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            int stride = checked(premultiplied.PixelWidth * 4);
            int size = checked(stride * premultiplied.PixelHeight);
            if (pixels.Length < size) pixels = new byte[size];
            premultiplied.CopyPixels(pixels, stride, 0);
            return EncodePbgra32(pixels, premultiplied.PixelWidth, premultiplied.PixelHeight, stride);
        }

        public byte[] EncodePbgra32(byte[] source, int width, int height, int stride)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (width <= 0) throw new ArgumentOutOfRangeException("width", "PNG dimensions must be positive.");
            if (height <= 0) throw new ArgumentOutOfRangeException("height", "PNG dimensions must be positive.");
            int rowBytes = checked(width * 4);
            if (stride < rowBytes || (long)(height - 1) * stride + rowBytes > source.Length)
                throw new ArgumentException("The pixel buffer or stride is too small.", "source");
            int length = checked((rowBytes + 1) * height);
            if (filtered.Length < length) filtered = new byte[length];
            if (previous.Length < rowBytes) previous = new byte[rowBytes];
            Array.Clear(previous, 0, rowBytes);

            // The Up filter is especially cheap for a largely static staff and
            // vertical key faces. Lookup-based unpremultiplication avoids divisions
            // in the per-frame pixel loop; alpha-zero hidden RGB becomes zero.
            uint adler = FilterRows(source, width, height, stride, rowBytes);

            compressed.SetLength(0); compressed.Position = 0;
            // .NET Framework DeflateStream emits raw DEFLATE. PNG requires the
            // surrounding zlib CMF/FLG header and Adler-32 of filtered scanlines.
            compressed.WriteByte(0x78); compressed.WriteByte(0x01);
            using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, true))
                deflate.Write(filtered, 0, length);
            WriteUInt32(compressed, adler);

            output.SetLength(0); output.Position = 0;
            output.Write(Signature, 0, Signature.Length);
            PutUInt32(header, 0, (uint)width); PutUInt32(header, 4, (uint)height);
            header[8] = 8; header[9] = 6; // 8-bit RGBA, non-interlaced.
            WriteChunk(output, Ihdr, header, 13);
            WriteChunk(output, Idat, compressed.GetBuffer(), checked((int)compressed.Length));
            WriteChunk(output, Iend, header, 0);
            return output.ToArray();
        }

        private unsafe uint FilterRows(byte[] source, int width, int height, int stride, int rowBytes)
        {
            uint sum = 1, weighted = 0;
            fixed (byte* inputBase = source, outputBase = filtered, previousBase = previous, tableBase = Unpremultiply)
            {
                for (int y = 0; y < height; y++)
                {
                    byte* input = inputBase + y * stride, destination = outputBase + y * (rowBytes + 1), prior = previousBase;
                    *destination++ = RowFilter;
                    sum += RowFilter; weighted += sum;
                    uint left = 0;
                    for (int x = 0; x < width; x++, input += 4, destination += 4, prior += 4)
                    {
                        byte a = input[3];
                        byte* table = tableBase + (a << 8);
                        uint rgba = (uint)(table[input[2]] | (table[input[1]] << 8) | (table[input[0]] << 16) | (a << 24));
                        if (RowFilter == 0) *(uint*)destination = rgba;
                        else
                        {
                            uint neighbor = RowFilter == 1 ? left : *(uint*)prior;
                            // Independent 8-bit differences; packed subtraction would borrow between channels.
                            destination[0] = unchecked((byte)((byte)rgba - (byte)neighbor));
                            destination[1] = unchecked((byte)((byte)(rgba >> 8) - (byte)(neighbor >> 8)));
                            destination[2] = unchecked((byte)((byte)(rgba >> 16) - (byte)(neighbor >> 16)));
                            destination[3] = unchecked((byte)((byte)(rgba >> 24) - (byte)(neighbor >> 24)));
                        }
                        uint d0 = destination[0], d1 = destination[1], d2 = destination[2], d3 = destination[3];
                        weighted += (sum << 2) + (d0 << 2) + d1 * 3 + (d2 << 1) + d3;
                        sum += d0 + d1 + d2 + d3;
                        if ((x & 1023) == 1023) { sum %= 65521; weighted %= 65521; }
                        *(uint*)prior = rgba; left = rgba;
                    }
                    sum %= 65521; weighted %= 65521;
                }
            }
            return (weighted << 16) | sum;
        }

        private static byte[] BuildUnpremultiply()
        {
            var table = new byte[256 * 256];
            for (int alpha = 1; alpha < 256; alpha++)
                for (int value = 0; value < 256; value++)
                    table[(alpha << 8) + value] = (byte)Math.Min(255, (value * 255 + alpha / 2) / alpha);
            return table;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
                table[i] = value;
            }
            return table;
        }

        private static void WriteChunk(Stream stream, byte[] type, byte[] data, int length)
        {
            WriteUInt32(stream, (uint)length);
            stream.Write(type, 0, 4); stream.Write(data, 0, length);
            uint crc = 0xffffffffu;
            for (int i = 0; i < 4; i++) crc = CrcTable[(crc ^ type[i]) & 255] ^ (crc >> 8);
            for (int i = 0; i < length; i++) crc = CrcTable[(crc ^ data[i]) & 255] ^ (crc >> 8);
            WriteUInt32(stream, crc ^ 0xffffffffu);
        }
        private static void PutUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24); buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8); buffer[offset + 3] = (byte)value;
        }
        private static void WriteUInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value >> 24)); stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value);
        }
    }
}
