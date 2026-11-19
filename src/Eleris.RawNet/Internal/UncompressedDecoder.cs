using System.Buffers;

namespace Eleris.RawNet.Internal;

/// <summary>
/// Decodes a raw image whose samples are stored as they are: the samples of a row follow one another, most significant bit first
/// and without padding between them, and each row starts on a byte boundary (the packing rules of TIFF for a sample size that is
/// not a whole number of bytes). The strips are read as one continuous run of bytes.
/// </summary>
internal static class UncompressedDecoder
{
    private const int ChunkBytes = 1 << 20;

    /// <summary>The sample sizes that are decoded.</summary>
    public static bool IsSupported(int bitsPerSample) => bitsPerSample is 12 or 14;

    /// <summary>The size in bytes of a row of <paramref name="width"/> samples.</summary>
    public static long RowBytes(int width, int bitsPerSample) => (((long)width * bitsPerSample) + 7) / 8;

    /// <summary>
    /// Checks that the image can be decoded, before anything is allocated for it: its samples fit in an array, its strips do not
    /// overlap (so the bytes they hold are distinct bytes of the file) and they hold at least <c>minimumBytes</c> (what the image needs at the very least, which bounds what is allocated by the size of the file).
    /// </summary>
    public static void CheckStrips(NefStructure structure, long? minimumBytes = null)
    {
        RawMetadata metadata = structure.Metadata;
        if ((long)metadata.ImageWidth * metadata.ImageHeight > Array.MaxLength)
        {
            throw new UnsupportedFormatException("The raw image has more samples than an array can hold.");
        }

        long needed = minimumBytes ?? RowBytes(metadata.ImageWidth, metadata.BitsPerSample) * metadata.ImageHeight;
        ByteRange[] sorted = [.. structure.RawStrips];
        Array.Sort(sorted, static (a, b) => a.Offset.CompareTo(b.Offset));
        long available = 0, end = 0;
        foreach (ByteRange strip in sorted)
        {
            if (strip.Length > 0 && strip.Offset < end)
            {
                throw new CorruptRawFileException("The strips of the raw image overlap", strip.Offset);
            }

            end = Math.Max(end, strip.Offset + strip.Length);
            available += strip.Length;
        }

        if (available < needed)
        {
            throw new CorruptRawFileException($"The raw image needs {needed} bytes and its strips hold {available}");
        }
    }

    public static void Decode(ByteSource source, NefStructure structure, Span<ushort> destination, CancellationToken cancellationToken, int chunkBytes = ChunkBytes)
    {
        RawMetadata metadata = structure.Metadata;
        int width = metadata.ImageWidth, height = metadata.ImageHeight, bits = metadata.BitsPerSample;
        int rowBytes = checked((int)RowBytes(width, bits));
        int rowsPerChunk = Math.Min(height, Math.Max(1, chunkBytes / rowBytes));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(checked(rowsPerChunk * rowBytes));
        try
        {
            var stream = new StripStream(source, structure.RawStrips);
            for (int row = 0; row < height; row += rowsPerChunk)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int rows = Math.Min(rowsPerChunk, height - row);
                stream.ReadExactly(buffer.AsSpan(0, rows * rowBytes));
                for (int r = 0; r < rows; r++)
                {
                    Unpack(buffer.AsSpan(r * rowBytes, rowBytes), bits, destination.Slice((row + r) * width, width));
                }
            }
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

    /// <summary>Unpacks the samples of a row, <paramref name="bits"/> bits each, most significant bit first.</summary>
    internal static void Unpack(ReadOnlySpan<byte> row, int bits, Span<ushort> samples)
    {
        uint mask = (1u << bits) - 1;
        ulong accumulator = 0;
        int held = 0, next = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            while (held < bits)
            {
                accumulator = (accumulator << 8) | row[next++];
                held += 8;
            }

            held -= bits;
            samples[i] = (ushort)((uint)(accumulator >> held) & mask);
        }
    }
}
