using System.Buffers;
using System.Buffers.Binary;

namespace Eleris.RawNet.Internal;

/// <summary>
/// Decodes the lossless variant of the Nikon compression (TIFF compression 34713). The facts of the format it relies on:
/// <list type="bullet">
/// <item>The samples of the image are stored row after row in one bit stream, most significant bit first, with no padding
/// between rows and no stuffing.</item>
/// <item>Each sample is a difference from a prediction: a prefix code gives the number n of bits that follow, then n bits give
/// the difference in the usual JPEG way (a value whose top bit is clear stands for that value minus 2^n minus 1).</item>
/// <item>For the first two samples of a row the prediction is the first or second sample two rows above (the rows alternate
/// between two pairs of starting values); for the others it is the sample two positions to the left, in the same row. The four
/// starting values are in the compression record of the MakerNote.</item>
/// <item>The prefix code is a canonical one given by the number of codes of each length 1 to 16 and the list of the symbols in
/// code order; there is one for 12 bit samples and one for 14 bit samples.</item>
/// <item>The record of the lossless variant holds no linearization curve: the decoded values are the samples.</item>
/// </list>
/// </summary>
internal static class NikonLosslessDecoder
{
    private const int BufferBytes = 1 << 18;

    // Number of codes of each length (1 to 16), then the symbols (the number of bits that follow) in code order.
    internal static readonly byte[] Counts12 = [0, 1, 4, 2, 3, 1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    internal static readonly byte[] Symbols12 = [5, 4, 6, 3, 7, 2, 8, 1, 9, 0, 10, 11, 12];
    internal static readonly byte[] Counts14 = [0, 1, 4, 2, 2, 3, 1, 2, 0, 0, 0, 0, 0, 0, 0, 0];
    internal static readonly byte[] Symbols14 = [7, 6, 8, 5, 9, 4, 10, 3, 11, 12, 2, 0, 1, 13, 14];

    private static readonly int MaxLength12 = MaxLengthOf(Counts12), MaxLength14 = MaxLengthOf(Counts14);
    private static readonly ushort[] Table12 = BuildTable(Counts12, Symbols12, MaxLength12);
    private static readonly ushort[] Table14 = BuildTable(Counts14, Symbols14, MaxLength14);

    public static bool IsSupported(int bitsPerSample) => bitsPerSample is 12 or 14;

    private static readonly int MinBits12 = MinBitsOf(Counts12, Symbols12), MinBits14 = MinBitsOf(Counts14, Symbols14);

    /// <summary>The fewest bytes a stream of the lossless format can have for an image: every sample takes at least the bits of its shortest code with its difference.</summary>
    public static long MinimumBytes(int width, int height, int bitsPerSample) =>
        (((long)width * height * (bitsPerSample == 12 ? MinBits12 : MinBits14)) + 7) / 8;

    /// <summary>
    /// Decodes the image into <paramref name="destination"/> and returns the number of bytes of the strips the stream used.
    /// </summary>
    public static long Decode(ByteSource source, NefStructure structure, NikonCompressionHeader header, Span<ushort> destination, CancellationToken cancellationToken)
    {
        RawMetadata metadata = structure.Metadata;
        int width = metadata.ImageWidth, height = metadata.ImageHeight, bits = metadata.BitsPerSample;
        ushort[] table = bits == 12 ? Table12 : Table14;
        int tableBits = bits == 12 ? MaxLength12 : MaxLength14;
        uint limit = 1u << bits;
        long total = 0;
        foreach (ByteRange strip in structure.RawStrips)
        {
            total += strip.Length;
        }

        var stream = new StripStream(source, structure.RawStrips);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
        try
        {
            var reader = new BitReader(stream, buffer);
            Span<int> vertical = [header.Predictor0, header.Predictor1, header.Predictor2, header.Predictor3];
            for (int row = 0; row < height; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Span<ushort> line = destination.Slice(row * width, width);
                int h0 = 0, h1 = 0;
                for (int col = 0; col < width; col++)
                {
                    reader.Ensure();
                    ushort entry = table[reader.Peek(tableBits)];
                    int length = entry >> 8;
                    if (length == 0)
                    {
                        throw new CorruptRawFileException("The compressed raw image holds a code that does not exist", null);
                    }

                    reader.Skip(length);
                    int count = entry & 0xFF;
                    int difference = 0;
                    if (count > 0)
                    {
                        difference = (int)reader.Peek(count);
                        reader.Skip(count);
                        if (difference < (1 << (count - 1)))
                        {
                            difference -= (1 << count) - 1;
                        }
                    }

                    int value;
                    if (col < 2)
                    {
                        ref int predictor = ref vertical[((row & 1) * 2) + col];
                        predictor += difference;
                        value = predictor;
                        if (col == 0)
                        {
                            h0 = value;
                        }
                        else
                        {
                            h1 = value;
                        }
                    }
                    else if ((col & 1) == 0)
                    {
                        h0 += difference;
                        value = h0;
                    }
                    else
                    {
                        h1 += difference;
                        value = h1;
                    }

                    if ((uint)value >= limit)
                    {
                        throw new CorruptRawFileException("The compressed raw image holds a sample out of range", null);
                    }

                    line[col] = (ushort)value;
                }
            }

            long used = reader.BytesUsed;
            if (used > total)
            {
                throw new CorruptRawFileException("The compressed raw image goes beyond the end of its strips", null);
            }

            return used;
        }
        catch (EndOfStreamException ex)
        {
            throw new CorruptRawFileException("The file ends inside the raw image", null, ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int MaxLengthOf(byte[] counts) => Array.FindLastIndex(counts, c => c > 0) + 1;

    private static int MinBitsOf(byte[] counts, byte[] symbols)
    {
        int min = int.MaxValue, next = 0;
        for (int length = 1; length <= counts.Length; length++)
        {
            for (int i = 0; i < counts[length - 1]; i++)
            {
                min = Math.Min(min, length + symbols[next++]);
            }
        }

        return min;
    }

    /// <summary>A lookup table indexed by the next <c>maxLength</c> bits: the length of the code in the high byte, its symbol in the low byte; zero for no code.</summary>
    private static ushort[] BuildTable(byte[] counts, byte[] symbols, int maxLength)
    {

        ushort[] table = new ushort[1 << maxLength];
        int code = 0, next = 0;
        for (int length = 1; length <= maxLength; length++)
        {
            for (int i = 0; i < counts[length - 1]; i++)
            {
                int first = code << (maxLength - length);
                for (int j = 0; j < 1 << (maxLength - length); j++)
                {
                    table[first + j] = (ushort)((length << 8) | symbols[next]);
                }

                code++;
                next++;
            }

            code <<= 1;
        }

        return table;
    }

    /// <summary>Reads bits, most significant first, from the strips through a buffer. Past the end of the data it supplies zeros.</summary>
    private ref struct BitReader(StripStream stream, byte[] buffer)
    {
        private ulong _bits;
        private int _held;
        private int _position;
        private int _end;
        private long _fed;

        /// <summary>The number of bytes of the stream that were consumed.</summary>
        public readonly long BytesUsed => ((_fed * 8) - _held + 7) / 8;

        /// <summary>Makes sure at least 32 bits are held.</summary>
        public void Ensure()
        {
            if (_held > 32)
            {
                return;
            }

            if (_end - _position < 4)
            {
                Refill();
            }

            _bits = (_bits << 32) | BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(_position, 4));
            _position += 4;
            _fed += 4;
            _held += 32;
        }

        public readonly uint Peek(int count) => (uint)(_bits >> (_held - count)) & ((1u << count) - 1);

        public void Skip(int count) => _held -= count;

        private void Refill()
        {
            int left = _end - _position;
            buffer.AsSpan(_position, left).CopyTo(buffer);
            _position = 0;
            _end = left;
            while (_end < 4)
            {
                int read = stream.Read(buffer.AsSpan(_end, buffer.Length - _end));
                if (read == 0)
                {
                    // Past the last strip: zeros; the caller detects that more bits were used than the strips hold.
                    buffer.AsSpan(_end, 4 - _end).Clear();
                    _end = 4;
                    break;
                }

                _end += read;
            }
        }
    }
}
