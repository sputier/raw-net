using Eleris.RawNet.Internal;
using Eleris.RawNet.Tests.Synthetic;

namespace Eleris.RawNet.Tests;

public class DecodeTests
{
    /// <summary>Packs samples the way an uncompressed raw image stores them: most significant bit first, each row on a byte boundary.</summary>
    internal static byte[] Pack(ushort[] samples, int width, int bits)
    {
        int height = samples.Length / width;
        int rowBytes = ((width * bits) + 7) / 8;
        byte[] packed = new byte[rowBytes * height];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int start = col * bits;
                for (int bit = 0; bit < bits; bit++)
                {
                    if ((samples[(row * width) + col] >> (bits - 1 - bit) & 1) != 0)
                    {
                        int position = start + bit;
                        packed[(row * rowBytes) + (position >> 3)] |= (byte)(0x80 >> (position & 7));
                    }
                }
            }
        }

        return packed;
    }

    private static ushort[] RandomSamples(int count, int bits, int seed)
    {
        var random = new Random(seed);
        ushort[] samples = new ushort[count];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (ushort)random.Next(1 << bits);
        }

        return samples;
    }

    private static byte[] Uncompressed(ushort[] samples, int width, int height, int bits, bool bigEndian = false, int[]? activeArea = null) =>
        SyntheticNef.Build(new SyntheticNefOptions
        {
            RawWidth = width,
            RawHeight = height,
            BitsPerSample = bits,
            Compression = 1,
            BigEndian = bigEndian,
            ActiveArea = activeArea is null ? [0, 0, (ushort)width, (ushort)height] : [.. activeArea.Select(v => (ushort)v)],
            RawStrip = Pack(samples, width, bits),
        });

    [Theory]
    [InlineData(12, 64, 48, false)]
    [InlineData(14, 64, 48, false)]
    [InlineData(12, 64, 48, true)]
    [InlineData(14, 64, 48, true)]
    [InlineData(12, 9, 7, false)]
    [InlineData(14, 11, 5, true)]
    [InlineData(14, 1, 1, false)]
    public void The_samples_of_an_uncompressed_image_are_decoded_whatever_the_width(int bits, int width, int height, bool bigEndian)
    {
        ushort[] expected = RandomSamples(width * height, bits, width + height);
        using RawFile raw = RawFile.Open(Uncompressed(expected, width, height, bits, bigEndian));

        RawImage image = raw.Decode();

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(bits, image.BitsPerSample);
        Assert.Equal(expected, image.Mosaic.ToArray());
    }

    [Fact]
    public void The_extreme_samples_are_kept()
    {
        foreach (int bits in new[] { 12, 14 })
        {
            ushort max = (ushort)((1 << bits) - 1);
            ushort[] expected = [0, max, max, 0, max, 0, 0, max];
            using RawFile raw = RawFile.Open(Uncompressed(expected, 4, 2, bits));

            Assert.Equal(expected, raw.Decode().Mosaic.ToArray());
        }
    }

    [Fact]
    public void The_image_carries_what_describes_it()
    {
        ushort[] samples = RandomSamples(16 * 8, 14, 3);
        using RawFile raw = RawFile.Open(Uncompressed(samples, 16, 8, 14, activeArea: [2, 1, 12, 6]));

        RawImage image = raw.Decode();

        Assert.Equal(raw.Metadata.ActiveArea, image.ActiveArea);
        Assert.Equal(new RawRectangle(2, 1, 12, 6), image.ActiveArea);
        Assert.Equal(raw.Metadata.CfaPattern, image.CfaPattern);
    }

    [Fact]
    public void An_image_can_be_decoded_into_a_buffer_that_is_larger_than_needed()
    {
        ushort[] expected = RandomSamples(10 * 6, 12, 9);
        using RawFile raw = RawFile.Open(Uncompressed(expected, 10, 6, 12));
        ushort[] buffer = new ushort[(10 * 6) + 5];
        Array.Fill(buffer, (ushort)0xFFFF);

        int written = raw.Decode(buffer);

        Assert.Equal(60, written);
        Assert.Equal(expected, buffer[..60]);
        Assert.All(buffer[60..], v => Assert.Equal(0xFFFF, v));
        Assert.Throws<ArgumentException>(() => raw.Decode(new ushort[59]));
    }

    [Fact]
    public void Decoding_into_a_buffer_does_not_allocate_more_than_a_little()
    {
        ushort[] samples = RandomSamples(64 * 48, 14, 5);
        using RawFile raw = RawFile.Open(Uncompressed(samples, 64, 48, 14));
        ushort[] buffer = new ushort[64 * 48];
        raw.Decode(buffer);

        long before = GC.GetAllocatedBytesForCurrentThread();
        raw.Decode(buffer);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= 512, $"{allocated} bytes allocated");
    }

    [Fact]
    public void Strips_that_end_inside_a_row_are_read_as_one_run_of_bytes()
    {
        const int width = 9, height = 6, bits = 14;
        ushort[] expected = RandomSamples(width * height, bits, 11);
        byte[] data = Pack(expected, width, bits);
        // Three strips of unequal sizes, the first two ending inside a row, in the middle of a file with other bytes around.
        int[] cuts = [0, 7, 40, data.Length];
        byte[] file = new byte[1000];
        var strips = new List<ByteRange>();
        long place = 100;
        for (int i = 0; i < 3; i++)
        {
            data.AsSpan(cuts[i], cuts[i + 1] - cuts[i]).CopyTo(file.AsSpan((int)place));
            strips.Add(new ByteRange(place, cuts[i + 1] - cuts[i]));
            place += 300;
        }

        RawMetadata metadata = new()
        {
            MediaType = RawMediaTypes.NikonNef,
            Make = "m",
            Model = "m",
            ImageWidth = width,
            ImageHeight = height,
            ActiveArea = new RawRectangle(0, 0, width, height),
            CfaPattern = CfaPattern.Rggb,
            BitsPerSample = bits,
            Compression = RawCompression.Uncompressed,
            CompressionCode = 1,
        };
        var structure = new NefStructure(metadata, strips, []);
        ushort[] decoded = new ushort[width * height];

        UncompressedDecoder.Decode(ByteSource.FromMemory(file), structure, decoded, CancellationToken.None);

        Assert.Equal(expected, decoded);
    }

    private static NefStructure Structure(int width, int height, int bits, List<ByteRange> strips) =>
        new(
            new RawMetadata
            {
                MediaType = RawMediaTypes.NikonNef,
                Make = "m",
                Model = "m",
                ImageWidth = width,
                ImageHeight = height,
                ActiveArea = new RawRectangle(0, 0, width, height),
                CfaPattern = CfaPattern.Rggb,
                BitsPerSample = bits,
                Compression = RawCompression.Uncompressed,
                CompressionCode = 1,
            },
            strips,
            []);

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(41)]
    [InlineData(1000)]
    public void Decoding_by_blocks_of_any_size_gives_the_same_samples(int chunkBytes)
    {
        const int width = 13, height = 11, bits = 14;
        ushort[] expected = RandomSamples(width * height, bits, 21);
        byte[] data = Pack(expected, width, bits);
        ushort[] decoded = new ushort[width * height];

        UncompressedDecoder.Decode(ByteSource.FromMemory(data), Structure(width, height, bits, [new ByteRange(0, 50), new ByteRange(50, data.Length - 50)]), decoded, CancellationToken.None, chunkBytes);

        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void Strips_that_overlap_are_refused_but_strips_given_out_of_order_are_not()
    {
        const int width = 8, height = 4, bits = 12;
        int rowBytes = width * bits / 8;

        Assert.Throws<CorruptRawFileException>(() => UncompressedDecoder.CheckStrips(Structure(width, height, bits, [new ByteRange(0, rowBytes * 3), new ByteRange(0, rowBytes * 3)])));
        Assert.Throws<CorruptRawFileException>(() => UncompressedDecoder.CheckStrips(Structure(width, height, bits, [new ByteRange(0, rowBytes * 2), new ByteRange(rowBytes, rowBytes * 2)])));
        UncompressedDecoder.CheckStrips(Structure(width, height, bits, [new ByteRange(rowBytes * 2, rowBytes * 2), new ByteRange(0, rowBytes * 2), new ByteRange(rowBytes * 4, 0)]));
    }

    [Fact]
    public void An_image_with_more_samples_than_an_array_can_hold_is_refused_before_anything_is_allocated()
    {
        byte[] huge = SyntheticNef.Build(new SyntheticNefOptions { RawWidth = 1 << 20, RawHeight = 1 << 20, BitsPerSample = 12, Compression = 1, RawStrip = new byte[64] });
        using RawFile raw = RawFile.Open(huge);
        Assert.Throws<UnsupportedFormatException>(() => raw.Decode());

        byte[] large = SyntheticNef.Build(new SyntheticNefOptions { RawWidth = 40000, RawHeight = 40000, BitsPerSample = 12, Compression = 1, RawStrip = new byte[64] });
        using RawFile other = RawFile.Open(large);
        Assert.Throws<CorruptRawFileException>(() => other.Decode());
    }

    [Fact]
    public void A_file_opened_from_a_stream_is_decoded_and_leaves_the_stream_where_it_can_be_reused()
    {
        ushort[] expected = RandomSamples(20 * 10, 12, 6);
        using var stream = new MemoryStream(Uncompressed(expected, 20, 10, 12));
        using RawFile raw = RawFile.Open(stream);

        Assert.Equal(expected, raw.Decode().Mosaic.ToArray());
        Assert.Equal(expected, raw.Decode().Mosaic.ToArray());
    }

    [Theory]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(13)]
    [InlineData(16)]
    public void Unpacking_matches_a_reading_bit_by_bit(int bits)
    {
        ushort[] expected = RandomSamples(37, bits, bits);
        byte[] packed = Pack(expected, 37, bits);
        ushort[] samples = new ushort[37];

        UncompressedDecoder.Unpack(packed, bits, samples);

        Assert.Equal(expected, samples);
    }

    [Fact]
    public void Samples_of_a_size_the_library_does_not_decode_are_refused()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { RawWidth = 8, RawHeight = 4, BitsPerSample = 16, Compression = 1, RawStrip = new byte[64] });
        using RawFile raw = RawFile.Open(file);

        Assert.Throws<UnsupportedFormatException>(() => raw.Decode());
        Assert.Throws<UnsupportedFormatException>(() => raw.Decode(new ushort[32]));
    }

    [Fact]
    public void A_compression_the_library_does_not_decode_is_reported_with_its_code()
    {
        using RawFile nikon = RawFile.Open(SyntheticNef.Build());
        var error = Assert.Throws<UnsupportedCompressionException>(() => nikon.Decode());
        Assert.Equal(34713, error.Code);
        Assert.Equal("Nikon compressed", error.Name);

        using RawFile other = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { Compression = 7 }));
        var unknown = Assert.Throws<UnsupportedCompressionException>(() => other.Decode(new ushort[64 * 48]));
        Assert.Equal(7, unknown.Code);
        Assert.Null(unknown.Name);
    }

    [Fact]
    public void Strips_that_are_too_short_for_the_image_are_reported_as_corrupt_before_anything_is_allocated()
    {
        ushort[] samples = RandomSamples(16 * 8, 12, 1);
        byte[] packed = Pack(samples, 16, 12);
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { RawWidth = 16, RawHeight = 8, BitsPerSample = 12, Compression = 1, RawStrip = packed[..^1] });
        using RawFile raw = RawFile.Open(file);

        Assert.Throws<CorruptRawFileException>(() => raw.Decode());
        Assert.Throws<CorruptRawFileException>(() => raw.Decode(new ushort[16 * 8]));
    }

    [Fact]
    public void A_cancelled_decoding_stops()
    {
        using RawFile raw = RawFile.Open(Uncompressed(RandomSamples(64 * 48, 14, 2), 64, 48, 14));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => raw.Decode(cancelled.Token));
        Assert.Equal(64 * 48, raw.Decode().Mosaic.Length);
    }

    [Fact]
    public void A_disposed_file_cannot_be_decoded()
    {
        RawFile raw = RawFile.Open(Uncompressed(RandomSamples(64 * 48, 14, 2), 64, 48, 14));
        raw.Dispose();

        Assert.Throws<ObjectDisposedException>(() => raw.Decode());
        Assert.Throws<ObjectDisposedException>(() => raw.Decode(new ushort[64 * 48]));
    }

    [Fact]
    public void A_file_opened_from_a_path_is_decoded_by_several_threads()
    {
        ushort[] expected = RandomSamples(64 * 48, 14, 8);
        string path = Path.Combine(Path.GetTempPath(), $"rawnet-{Guid.NewGuid():N}.nef");
        File.WriteAllBytes(path, Uncompressed(expected, 64, 48, 14));
        try
        {
            using RawFile raw = RawFile.Open(path);

            Parallel.For(0, 100, _ => Assert.Equal(expected, raw.Decode().Mosaic.ToArray()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_damaged_file_fails_to_decode_only_with_a_raw_exception()
    {
        byte[] intact = Uncompressed(RandomSamples(32 * 16, 14, 4), 32, 16, 14);
        var random = new Random(42);
        for (int attempt = 0; attempt < 2000; attempt++)
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

    [SkippableFact]
    public void A_real_file_that_is_compressed_is_refused_cleanly_until_its_compression_is_decoded()
    {
        string? directory = Environment.GetEnvironmentVariable("RAWNET_SAMPLES_DIR");
        Skip.If(string.IsNullOrEmpty(directory) || !Directory.Exists(directory), "RAWNET_SAMPLES_DIR is not set to a folder.");
        string? path = Directory.EnumerateFiles(directory!, "*.NEF").Order(StringComparer.Ordinal).FirstOrDefault();
        Skip.If(path is null, "No .NEF file in RAWNET_SAMPLES_DIR.");

        using RawFile raw = RawFile.Open(path!);
        Skip.If(raw.Metadata.Compression == RawCompression.Uncompressed, "The sample is uncompressed.");

        Assert.Throws<UnsupportedCompressionException>(() => raw.Decode());
    }
}
