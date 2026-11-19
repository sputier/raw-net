using Eleris.RawNet.Tests.Synthetic;

namespace Eleris.RawNet.Tests;

public class RawFileTests
{
    private static RawMetadata Read(byte[] file)
    {
        using RawFile raw = RawFile.Open(file);
        return raw.Metadata;
    }

    // Everything the library can read from a file: its metadata and every preview (damage may show only when the preview is read).
    private static RawMetadata ReadEverything(byte[] file)
    {
        using RawFile raw = RawFile.Open(file);
        foreach (RawPreview preview in raw.Previews)
        {
            raw.ReadPreviewJpeg(preview);
        }

        return raw.Metadata;
    }

    [Fact]
    public void The_metadata_of_a_file_describes_the_shot_and_the_raw_image()
    {
        RawMetadata m = Read(SyntheticNef.Build());

        Assert.Equal(RawMediaTypes.NikonNef, m.MediaType);
        Assert.Equal("NIKON CORPORATION", m.Make);
        Assert.Equal("NIKON Z 6", m.Model);
        Assert.Equal("1234567", m.SerialNumber);
        Assert.Equal("Ver.00.00", m.Software);
        Assert.Equal("Test lens 50mm f/1.8", m.LensModel);
        Assert.Equal(50.0, m.FocalLengthMillimetres);
        Assert.Equal(1.8, m.FNumber);
        Assert.Equal(1.6, m.ExposureTimeSeconds);
        Assert.Equal(1600, m.Iso);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified), m.CaptureDateTime);
        Assert.Equal(TimeSpan.FromHours(1), m.CaptureUtcOffset);
        Assert.Equal(100, m.ShutterCount);
        Assert.Equal(64, m.ImageWidth);
        Assert.Equal(48, m.ImageHeight);
        Assert.Equal(new RawRectangle(4, 4, 56, 40), m.ActiveArea);
        Assert.Equal(CfaPattern.Rggb, m.CfaPattern);
        Assert.Equal(14, m.BitsPerSample);
        Assert.Equal(RawCompression.NikonCompressed, m.Compression);
        Assert.Equal(34713, m.CompressionCode);
    }

    [Fact]
    public void A_big_endian_file_reads_the_same_as_a_little_endian_one()
    {
        Assert.Equal(Read(SyntheticNef.Build()), Read(SyntheticNef.Build(new SyntheticNefOptions { BigEndian = true })));
    }

    [Theory]
    [InlineData(new byte[] { 0, 1, 1, 2 }, CfaPattern.Rggb)]
    [InlineData(new byte[] { 1, 0, 2, 1 }, CfaPattern.Grbg)]
    [InlineData(new byte[] { 1, 2, 0, 1 }, CfaPattern.Gbrg)]
    [InlineData(new byte[] { 2, 1, 1, 0 }, CfaPattern.Bggr)]
    [InlineData(new byte[] { 0, 1, 2, 1 }, CfaPattern.Unknown)]
    public void The_colour_filter_pattern_is_read_from_the_file(byte[] colours, CfaPattern expected)
    {
        Assert.Equal(expected, Read(SyntheticNef.Build(new SyntheticNefOptions { CfaPattern = colours })).CfaPattern);
    }

    [Theory]
    [InlineData(1, RawCompression.Uncompressed)]
    [InlineData(34713, RawCompression.NikonCompressed)]
    [InlineData(99, RawCompression.Unknown)]
    public void The_compression_is_named_when_the_library_knows_it_and_its_code_is_always_kept(int code, RawCompression expected)
    {
        RawMetadata m = Read(SyntheticNef.Build(new SyntheticNefOptions { Compression = code }));

        Assert.Equal(expected, m.Compression);
        Assert.Equal(code, m.CompressionCode);
    }

    [Fact]
    public void The_bit_depth_comes_from_the_file()
    {
        Assert.Equal(12, Read(SyntheticNef.Build(new SyntheticNefOptions { BitsPerSample = 12 })).BitsPerSample);
    }

    [Theory]
    [InlineData(new ushort[] { 0, 0, 0, 0 })]
    [InlineData(new ushort[] { 4, 4, 100, 40 })]
    [InlineData(new ushort[] { 60, 4, 10, 40 })]
    public void An_impossible_active_area_falls_back_to_the_whole_image(ushort[] area)
    {
        Assert.Equal(new RawRectangle(0, 0, 64, 48), Read(SyntheticNef.Build(new SyntheticNefOptions { ActiveArea = area })).ActiveArea);
    }

    [Fact]
    public void The_serial_number_falls_back_to_the_MakerNote()
    {
        Assert.Equal("7654321", Read(SyntheticNef.Build(new SyntheticNefOptions { SerialNumber = "7654321", IncludeExifSerial = false })).SerialNumber);
    }

    [Fact]
    public void Values_the_library_cannot_read_are_null_and_do_not_fail_the_file()
    {
        RawMetadata m = Read(SyntheticNef.Build(new SyntheticNefOptions
        {
            ExposureTime = (1, 0),
            DateTime = "not a date at all.",
            OffsetTime = "later",
        }));

        Assert.Null(m.ExposureTimeSeconds);
        Assert.Null(m.CaptureDateTime);
        Assert.Null(m.CaptureUtcOffset);
    }

    [Theory]
    [InlineData("+01:00", 60)]
    [InlineData("-05:30", -330)]
    [InlineData("+00:00", 0)]
    public void The_clock_offset_is_read_with_its_sign(string text, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), Read(SyntheticNef.Build(new SyntheticNefOptions { OffsetTime = text })).CaptureUtcOffset);
    }

    [Fact]
    public void A_file_from_another_maker_is_an_unsupported_camera()
    {
        var error = Assert.Throws<UnsupportedCameraException>(() => Read(SyntheticNef.Build(new SyntheticNefOptions { Make = "OTHER", Model = "Y 1" })));

        Assert.Equal("OTHER", error.Make);
        Assert.Equal("Y 1", error.Model);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 42, 0 })]
    [InlineData(new byte[] { (byte)'P', (byte)'K', 3, 4, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 43, 0, 8, 0, 0, 0 })]
    public void Something_that_is_not_a_TIFF_based_file_is_an_unsupported_format(byte[] content)
    {
        Assert.Throws<UnsupportedFormatException>(() => Read(content));
    }

    [Fact]
    public void A_file_cut_in_its_raw_data_is_reported_as_corrupt_with_a_position()
    {
        byte[] file = SyntheticNef.Build();

        var error = Assert.Throws<CorruptRawFileException>(() => Read(file[..^1]));

        Assert.NotNull(error.Position);
    }

    [Fact]
    public void A_file_cut_in_its_directories_is_reported_as_corrupt()
    {
        byte[] file = SyntheticNef.Build();

        Assert.Throws<CorruptRawFileException>(() => Read(file[..20]));
    }

    [Fact]
    public void A_path_a_stream_and_memory_give_the_same_metadata()
    {
        byte[] file = SyntheticNef.Build();
        string path = Path.Combine(Path.GetTempPath(), $"rawnet-{Guid.NewGuid():N}.nef");
        File.WriteAllBytes(path, file);
        try
        {
            using var stream = new MemoryStream(file);
            using RawFile fromStream = RawFile.Open(stream, leaveOpen: true);

            Assert.Equal(Read(file), fromStream.Metadata);
            Assert.Equal(Read(file), RawFile.ReadMetadata(path));
            using RawFile fromPath = RawFile.Open(path);
            Assert.Equal(Read(file), fromPath.Metadata);
            Assert.True(stream.CanRead);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Disposing_a_file_closes_a_stream_unless_it_was_left_open()
    {
        byte[] file = SyntheticNef.Build();
        var closed = new MemoryStream(file);
        var open = new MemoryStream(file);

        RawFile.Open(closed).Dispose();
        RawFile.Open(open, leaveOpen: true).Dispose();

        Assert.False(closed.CanRead);
        Assert.True(open.CanRead);
    }

    [Fact]
    public async Task Disposing_asynchronously_releases_the_stream()
    {
        var stream = new MemoryStream(SyntheticNef.Build());

        await RawFile.Open(stream).DisposeAsync();

        Assert.False(stream.CanRead);
    }

    [Fact]
    public void A_wrong_call_is_an_ArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => RawFile.Open((Stream)null!));
        Assert.Throws<ArgumentException>(() => RawFile.Open(new NonSeekableStream()));
        Assert.Throws<ArgumentException>(() => RawFile.Open(string.Empty));
        Assert.Throws<ArgumentNullException>(() => RawFile.Open((string)null!));
    }

    [Fact]
    public void A_stream_is_read_from_its_start_whatever_its_position()
    {
        using var stream = new MemoryStream(SyntheticNef.Build()) { Position = 37 };

        using RawFile file = RawFile.Open(stream, leaveOpen: true);

        Assert.Equal("NIKON Z 6", file.Metadata.Model);
    }

    [Fact]
    public void The_offset_belongs_to_the_date_that_is_reported()
    {
        var options = new SyntheticNefOptions { OffsetTime = "+02:00", OffsetTimeOriginal = "+05:00" };

        Assert.Equal(TimeSpan.FromHours(5), Read(SyntheticNef.Build(options)).CaptureUtcOffset);
        RawMetadata withoutOriginal = Read(SyntheticNef.Build(options with { IncludeDateTimeOriginal = false }));
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), withoutOriginal.CaptureDateTime);
        Assert.Equal(TimeSpan.FromHours(2), withoutOriginal.CaptureUtcOffset);
    }

    [Fact]
    public void A_focal_length_or_an_iso_of_zero_means_unknown()
    {
        RawMetadata m = Read(SyntheticNef.Build(new SyntheticNefOptions { FocalLength = (0, 1), Iso = 0, FNumber = (0, 1) }));

        Assert.Null(m.FocalLengthMillimetres);
        Assert.Null(m.Iso);
        Assert.Null(m.FNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_raw_image_is_the_largest_colour_filter_array_image_whatever_its_place(bool bigEndian)
    {
        RawMetadata m = Read(SyntheticNef.Build(new SyntheticNefOptions { SmallCfaSubImage = true, BigEndian = bigEndian }));

        Assert.Equal(64, m.ImageWidth);
        Assert.Equal(48, m.ImageHeight);
        Assert.Equal(14, m.BitsPerSample);
    }

    [Fact]
    public void A_damaged_MakerNote_costs_its_values_and_not_the_file()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { ShutterCount = 77 });
        int start = file.AsSpan().IndexOf("Nikon\0"u8);
        file[start] = (byte)'X';

        RawMetadata m = Read(file);

        Assert.Null(m.ShutterCount);
        Assert.Equal(new RawRectangle(0, 0, 64, 48), m.ActiveArea);
        Assert.Equal("1234567", m.SerialNumber);
        Assert.Equal("NIKON Z 6", m.Model);
    }

    [Fact]
    public void A_sub_directory_that_cannot_be_read_is_reported_as_corrupt_not_unsupported()
    {
        byte[] file = SyntheticNef.Build();
        List<TinyEntry> main = TinyTiff.ReadIfd(file, 8);
        TinyEntry subs = TinyTiff.Find(main, 330);
        // Point both sub-directories past the end of the file.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan((int)subs.ValueOffset), 0x7FFFFFF0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan((int)subs.ValueOffset + 4), 0x7FFFFFF0);

        Assert.Throws<CorruptRawFileException>(() => Read(file));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Boundary_values_in_every_field_of_the_structure_never_fail_with_anything_but_a_RawException(bool bigEndian)
    {
        byte[] original = SyntheticNef.Build(new SyntheticNefOptions { RawWidth = 8, RawHeight = 4, BigEndian = bigEndian, SmallCfaSubImage = true, MakerNotePreviewJpeg = [0xFF, 0xD8, 1, 2, 0xFF, 0xD9] });
        uint[] values = [0, 1, 0x7FFF, 0xFFFF, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, 64, 4096];
        var watch = System.Diagnostics.Stopwatch.StartNew();

        for (int position = 0; position + 4 <= original.Length; position++)
        {
            foreach (uint value in values)
            {
                foreach (int width in new[] { 2, 4 })
                {
                    byte[] file = (byte[])original.Clone();
                    Span<byte> target = file.AsSpan(position, width);
                    if (width == 2)
                    {
                        if (bigEndian)
                        {
                            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(target, (ushort)value);
                        }
                        else
                        {
                            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)value);
                        }
                    }
                    else if (bigEndian)
                    {
                        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(target, value);
                    }
                    else
                    {
                        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(target, value);
                    }

                    Exception? error = Record.Exception(() => ReadEverything(file));
                    Assert.True(error is null or RawException, $"position {position}, value {value:X}, width {width}: {error?.GetType().Name}: {error?.Message}");
                }
            }
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), "A damaged file must not make the reader loop or crawl.");
    }

    [Fact]
    public void A_failed_open_closes_the_stream_it_owns()
    {
        var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9]);

        Assert.Throws<UnsupportedFormatException>(() => RawFile.Open(stream));

        Assert.False(stream.CanRead);
    }

    [Fact]
    public void A_missing_file_fails_with_an_IOException_not_a_RawException()
    {
        Assert.ThrowsAny<IOException>(() => RawFile.Open(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.nef")));
    }

    [Fact]
    public void Truncating_a_file_anywhere_fails_with_a_RawException_and_nothing_else()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { RawWidth = 8, RawHeight = 4 });

        for (int length = 0; length < file.Length; length++)
        {
            byte[] cut = file[..length];
            Exception? error = Record.Exception(() => Read(cut));
            Assert.True(error is RawException, $"length {length}: {error?.GetType().Name ?? "no error"}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Damaging_bytes_never_fails_with_anything_but_a_RawException(bool bigEndian)
    {
        byte[] original = SyntheticNef.Build(new SyntheticNefOptions { RawWidth = 8, RawHeight = 4, BigEndian = bigEndian, MakerNotePreviewJpeg = [0xFF, 0xD8, 1, 2, 0xFF, 0xD9] });
        var random = new Random(12345);

        for (int iteration = 0; iteration < 5000; iteration++)
        {
            byte[] file = (byte[])original.Clone();
            int changes = random.Next(1, 6);
            for (int i = 0; i < changes; i++)
            {
                file[random.Next(file.Length)] = (byte)random.Next(256);
            }

            Exception? error = Record.Exception(() => ReadEverything(file));
            Assert.True(error is null or RawException, $"iteration {iteration}: {error?.GetType().Name}: {error?.Message}");
        }
    }

    [Fact]
    public void Reading_the_metadata_of_a_file_in_memory_stays_within_an_allocation_budget()
    {
        byte[] file = SyntheticNef.Build();
        Read(file);

        long before = GC.GetAllocatedBytesForCurrentThread();
        RawMetadata metadata = Read(file);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotNull(metadata);
        Assert.True(allocated < 4_000, $"{allocated} bytes allocated");
    }

    [SkippableFact]
    public void Every_real_file_of_the_sample_folder_can_be_read_quickly()
    {
        string? directory = Environment.GetEnvironmentVariable("RAWNET_SAMPLES_DIR");
        Skip.If(string.IsNullOrEmpty(directory) || !Directory.Exists(directory), "RAWNET_SAMPLES_DIR is not set to a folder.");
        string[] files = Directory.GetFiles(directory!, "*.NEF");
        Skip.If(files.Length == 0, "No .NEF file in RAWNET_SAMPLES_DIR.");
        RawFile.ReadMetadata(files[0]);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (string file in files)
        {
            RawMetadata m = RawFile.ReadMetadata(file);
            Assert.True(m.ImageWidth > 0 && m.ImageHeight > 0 && m.CfaPattern != CfaPattern.Unknown && m.Iso > 0);
        }

        double perFile = watch.Elapsed.TotalMilliseconds / files.Length;
        Assert.True(perFile < 50, $"{perFile:F2} ms per file");
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
