using Eleris.RawNet.Internal;
using Eleris.RawNet.Tests.Synthetic;

namespace Eleris.RawNet.Tests;

public class LosslessTests
{
    /// <summary>The compression record: lossless version bytes, then the four starting values of the predictors (little endian).</summary>
    private static byte[] Header(ushort start = 2048, byte version0 = 0x46, bool bigEndian = false) =>
        [version0, 0x30, .. new[] { start, start, start, start }.SelectMany(v => bigEndian ? new[] { (byte)(v >> 8), (byte)v } : new[] { (byte)v, (byte)(v >> 8) })];

    /// <summary>A bit writer and the encoder of the lossless format, written independently of the decoder.</summary>
    internal static byte[] Encode(ushort[] samples, int width, int bits, ushort start = 2048)
    {
        byte[] counts = bits == 12 ? NikonLosslessDecoder.Counts12 : NikonLosslessDecoder.Counts14;
        byte[] symbols = bits == 12 ? NikonLosslessDecoder.Symbols12 : NikonLosslessDecoder.Symbols14;
        var codes = new (int Length, int Code)[16];
        int code = 0, next = 0;
        for (int length = 1; length <= 16; length++)
        {
            for (int i = 0; i < counts[length - 1]; i++)
            {
                codes[symbols[next++]] = (length, code++);
            }

            code <<= 1;
        }

        var output = new List<byte>();
        ulong accumulator = 0;
        int held = 0;
        void Put(int value, int count)
        {
            accumulator = (accumulator << count) | (uint)(value & ((1 << count) - 1));
            held += count;
            while (held >= 8)
            {
                output.Add((byte)(accumulator >> (held - 8)));
                held -= 8;
            }
        }

        int[,] vertical = { { start, start }, { start, start } };
        int[] horizontal = new int[2];
        for (int i = 0; i < samples.Length; i++)
        {
            int row = i / width, col = i % width;
            int prediction = col < 2 ? vertical[row & 1, col] : horizontal[col & 1];
            int difference = samples[i] - prediction;
            if (col < 2)
            {
                vertical[row & 1, col] = samples[i];
            }

            horizontal[col & 1] = samples[i];
            int count = difference == 0 ? 0 : 32 - int.LeadingZeroCount(Math.Abs(difference));
            Put(codes[count].Code, codes[count].Length);
            if (count > 0)
            {
                Put(difference > 0 ? difference : difference + (1 << count) - 1, count);
            }
        }

        if (held > 0)
        {
            Put(0, 8 - held);
        }

        return [.. output];
    }

    private static ushort[] Smooth(int count, int bits, int seed, int noise)
    {
        var random = new Random(seed);
        ushort[] samples = new ushort[count];
        int level = 1 << (bits - 3);
        for (int i = 0; i < count; i++)
        {
            samples[i] = (ushort)Math.Clamp(level + random.Next(-noise, noise + 1), 0, (1 << bits) - 1);
        }

        return samples;
    }

    private static byte[] File(ushort[] samples, int width, int height, int bits, byte[]? strip = null, byte[]? header = null, bool bigEndian = false) =>
        SyntheticNef.Build(new SyntheticNefOptions
        {
            RawWidth = width,
            RawHeight = height,
            BitsPerSample = bits,
            ActiveArea = [0, 0, (ushort)width, (ushort)height],
            RawStrip = strip ?? Encode(samples, width, bits),
            CompressionHeader = header ?? Header(bigEndian: bigEndian),
            BigEndian = bigEndian,
        });

    [Theory]
    [InlineData(12, 64, 48, 5, false)]
    [InlineData(14, 64, 48, 40, false)]
    [InlineData(14, 64, 48, 40, true)]
    [InlineData(12, 9, 7, 300, false)]
    [InlineData(14, 3, 3, 4000, false)]
    [InlineData(14, 1, 1, 5, false)]
    [InlineData(14, 2, 6, 1, false)]
    public void A_compressed_image_is_decoded_back_to_its_samples(int bits, int width, int height, int noise, bool bigEndian)
    {
        ushort[] expected = Smooth(width * height, bits, width + height, noise);
        using RawFile raw = RawFile.Open(File(expected, width, height, bits, bigEndian: bigEndian));

        RawImage image = raw.Decode();

        Assert.Equal(expected, image.Mosaic.ToArray());
        Assert.Equal(RawCompression.NikonCompressed, raw.Metadata.Compression);
    }

    [Fact]
    public void The_extreme_samples_are_decoded()
    {
        foreach (int bits in new[] { 12, 14 })
        {
            ushort max = (ushort)((1 << bits) - 1);
            ushort[] expected = [0, max, max, 0, max, 0, 0, max, 2048, 2048, 0, max];
            using RawFile raw = RawFile.Open(File(expected, 4, 3, bits));

            Assert.Equal(expected, raw.Decode().Mosaic.ToArray());
        }
    }

    [Fact]
    public void The_starting_values_of_the_predictors_come_from_the_record_of_the_file()
    {
        ushort[] expected = Smooth(16 * 8, 14, 1, 20);
        byte[] strip = Encode(expected, 16, 14, start: 1000);
        using RawFile raw = RawFile.Open(File(expected, 16, 8, 14, strip, Header(1000)));

        Assert.Equal(expected, raw.Decode().Mosaic.ToArray());
    }

    [Fact]
    public void The_bytes_the_stream_uses_are_reported_for_checking_against_the_strip()
    {
        ushort[] expected = Smooth(64 * 48, 14, 7, 30);
        byte[] strip = Encode(expected, 64, 14);
        byte[] file = File(expected, 64, 48, 14, strip);
        using var source = ByteSource.FromMemory(file);
        NefStructure structure = new NefStructureReader().Read(source);

        long used = NikonLosslessDecoder.Decode(source, structure, structure.CompressionHeader!.Value, new ushort[64 * 48], CancellationToken.None);

        Assert.Equal(strip.Length, used);
    }

    [Fact]
    public void A_strip_that_ends_early_is_reported_as_corrupt()
    {
        ushort[] samples = Smooth(64 * 48, 14, 2, 200);
        byte[] strip = Encode(samples, 64, 14);

        using RawFile raw = RawFile.Open(File(samples, 64, 48, 14, strip[..(strip.Length / 2)]));

        Assert.Throws<CorruptRawFileException>(() => raw.Decode());
        Assert.Throws<CorruptRawFileException>(() => raw.Decode(new ushort[64 * 48]));
    }

    [Fact]
    public void A_strip_that_misses_its_last_bytes_is_reported_as_corrupt()
    {
        ushort[] samples = Smooth(64 * 48, 14, 3, 200);
        byte[] strip = Encode(samples, 64, 14);

        using RawFile raw = RawFile.Open(File(samples, 64, 48, 14, strip[..^6]));

        Assert.Throws<CorruptRawFileException>(() => raw.Decode());
    }

    [Fact]
    public void A_sample_out_of_range_is_reported_as_corrupt()
    {
        // The image was encoded from 16000, the file says 17000: the first sample is above the 14 bits.
        ushort[] samples = new ushort[16 * 8];
        Array.Fill(samples, (ushort)16000);
        byte[] strip = Encode(samples, 16, 14, start: 16000);
        using RawFile raw = RawFile.Open(File(samples, 16, 8, 14, strip, Header(17000)));

        Assert.Throws<CorruptRawFileException>(() => raw.Decode());
    }

    [Fact]
    public void A_compressed_image_without_a_lossless_record_is_reported_as_an_unsupported_compression()
    {
        byte[] strip = Encode(Smooth(16 * 8, 14, 5, 5), 16, 14);
        foreach (byte[]? header in new byte[]?[] { null, Header(version0: 0x44), [0x46, 0x30] })
        {
            byte[] file = SyntheticNef.Build(new SyntheticNefOptions { RawWidth = 16, RawHeight = 8, BitsPerSample = 14, RawStrip = strip, CompressionHeader = header });
            using RawFile raw = RawFile.Open(file);

            var error = Assert.Throws<UnsupportedCompressionException>(() => raw.Decode());
            Assert.Equal(34713, error.Code);
        }
    }

    [Fact]
    public void A_cancelled_decoding_of_a_compressed_image_stops()
    {
        ushort[] samples = Smooth(64 * 128, 14, 6, 50);
        using RawFile raw = RawFile.Open(File(samples, 64, 128, 14));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => raw.Decode(cancelled.Token));
    }

    [Fact]
    public void A_damaged_compressed_file_fails_to_decode_only_with_a_raw_exception()
    {
        byte[] intact = File(Smooth(32 * 16, 14, 4, 100), 32, 16, 14);
        var random = new Random(7);
        for (int attempt = 0; attempt < 3000; attempt++)
        {
            byte[] file = (byte[])intact.Clone();
            for (int k = 0; k < 1 + random.Next(4); k++)
            {
                file[random.Next(file.Length)] = (byte)random.Next(256);
            }

            if (attempt % 5 == 0)
            {
                file = file[..random.Next(file.Length)];
            }

            try
            {
                using RawFile raw = RawFile.Open(file);
                raw.Decode();
            }
            catch (RawException)
            {
            }
        }
    }

    [Fact]
    public void Decoding_a_compressed_image_into_a_buffer_does_not_allocate_more_than_a_little()
    {
        ushort[] samples = Smooth(64 * 48, 14, 5, 50);
        using RawFile raw = RawFile.Open(File(samples, 64, 48, 14));
        ushort[] buffer = new ushort[64 * 48];
        raw.Decode(buffer); // warms the pool of buffers

        long before = GC.GetAllocatedBytesForCurrentThread();
        raw.Decode(buffer);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= 512, $"{allocated} bytes allocated");
    }

    [Fact]
    public void The_prefix_codes_are_complete_so_every_bit_pattern_is_a_code()
    {
        foreach (byte[] counts in new[] { NikonLosslessDecoder.Counts12, NikonLosslessDecoder.Counts14 })
        {
            double kraft = 0;
            for (int length = 1; length <= counts.Length; length++)
            {
                kraft += counts[length - 1] * Math.Pow(2, -length);
            }

            Assert.Equal(1.0, kraft);
        }

        Assert.Equal(13, NikonLosslessDecoder.Symbols12.Length);
        Assert.Equal(NikonLosslessDecoder.Counts12.Sum(c => c), NikonLosslessDecoder.Symbols12.Length);
        Assert.Equal(NikonLosslessDecoder.Counts14.Sum(c => c), NikonLosslessDecoder.Symbols14.Length);
    }

    [Fact]
    public void An_image_whose_size_the_strip_cannot_hold_is_refused_before_anything_is_allocated()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { RawWidth = 1_000_000, RawHeight = 2_000, BitsPerSample = 14, RawStrip = new byte[64], CompressionHeader = Header() });
        using RawFile raw = RawFile.Open(file);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<CorruptRawFileException>(() => raw.Decode());
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 100_000);
    }

    [Fact]
    public void A_stream_larger_than_the_buffer_and_split_in_strips_is_decoded()
    {
        const int width = 1200, height = 400, bits = 14;
        ushort[] expected = Smooth(width * height, bits, 31, 3000);
        byte[] data = Encode(expected, width, bits);
        Assert.True(data.Length > 300_000);
        // Strips of unequal sizes, one empty, not in the order of the file, with other bytes around.
        int[] cuts = [0, 5, 5, 100_000, 100_003, data.Length];
        byte[] file = new byte[data.Length + 5000];
        var strips = new List<ByteRange>();
        long place = 1000;
        for (int i = 0; i < cuts.Length - 1; i++)
        {
            data.AsSpan(cuts[i], cuts[i + 1] - cuts[i]).CopyTo(file.AsSpan((int)place));
            strips.Add(new ByteRange(place, cuts[i + 1] - cuts[i]));
            place += cuts[i + 1] - cuts[i];
        }

        NefStructure structure = DecodeTests.Structure(width, height, bits, strips, compression: RawCompression.NikonCompressed);
        ushort[] decoded = new ushort[width * height];

        long used = NikonLosslessDecoder.Decode(ByteSource.FromMemory(file), structure, new NikonCompressionHeader(0x46, 0x30, 2048, 2048, 2048, 2048), decoded, CancellationToken.None);

        Assert.Equal(expected, decoded);
        Assert.Equal(data.Length, used);
    }

    [Fact]
    public void A_stream_that_ends_where_zeros_would_still_be_valid_samples_is_reported_as_corrupt()
    {
        // A constant image: every difference is zero, so the zeros read past the end could be taken for samples.
        ushort[] samples = new ushort[64 * 48];
        Array.Fill(samples, (ushort)2048);
        byte[] strip = Encode(samples, 64, 12);
        using RawFile intact = RawFile.Open(File(samples, 64, 48, 12, strip));
        Assert.Equal(samples, intact.Decode().Mosaic.ToArray());

        using RawFile cut = RawFile.Open(File(samples, 64, 48, 12, strip[..(strip.Length - 40)]));
        Assert.Throws<CorruptRawFileException>(() => cut.Decode());
    }

    [Fact]
    public void A_narrow_image_of_many_rows_is_decoded()
    {
        ushort[] expected = Smooth(1 * 257, 14, 9, 500);
        using RawFile raw = RawFile.Open(File(expected, 1, 257, 14));

        Assert.Equal(expected, raw.Decode().Mosaic.ToArray());
    }
}
