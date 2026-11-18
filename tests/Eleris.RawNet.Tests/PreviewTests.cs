using System.Buffers.Binary;
using Eleris.RawNet.Tests.Synthetic;

namespace Eleris.RawNet.Tests;

public class PreviewTests
{
    private static readonly byte[] SmallJpeg = [0xFF, 0xD8, 1, 2, 3, 0xFF, 0xD9];
    private static readonly byte[] LargeJpeg = [0xFF, 0xD8, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0xFF, 0xD9];

    [Fact]
    public void A_file_with_one_preview_gives_its_bytes()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { PreviewJpeg = SmallJpeg });
        using RawFile raw = RawFile.Open(file);

        Assert.Single(raw.Previews);
        Assert.Equal(SmallJpeg.Length, raw.Previews[0].ByteCount);
        Assert.Equal(0, raw.Previews[0].Index);
        Assert.Equal(SmallJpeg, raw.ReadPreviewJpeg());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Previews_are_listed_from_the_largest_whatever_the_directory_that_holds_them(bool bigEndian)
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { PreviewJpeg = SmallJpeg, MakerNotePreviewJpeg = LargeJpeg, BigEndian = bigEndian });
        using RawFile raw = RawFile.Open(file);

        Assert.Equal([LargeJpeg.Length, SmallJpeg.Length], raw.Previews.Select(p => (int)p.ByteCount));
        Assert.Equal([0, 1], raw.Previews.Select(p => p.Index));
        Assert.Equal(LargeJpeg, raw.ReadPreviewJpeg());
        Assert.Equal(SmallJpeg, raw.ReadPreviewJpeg(raw.Previews[1]));
    }

    [Fact]
    public void A_preview_can_be_read_into_a_buffer_of_the_caller()
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { PreviewJpeg = LargeJpeg }));
        byte[] buffer = new byte[LargeJpeg.Length + 5];

        int written = raw.ReadPreviewJpeg(raw.Previews[0], buffer);

        Assert.Equal(LargeJpeg.Length, written);
        Assert.Equal(LargeJpeg, buffer[..written]);
        Assert.Throws<ArgumentException>(() => raw.ReadPreviewJpeg(raw.Previews[0], new byte[LargeJpeg.Length - 1]));
    }

    [Fact]
    public void A_file_without_preview_has_none()
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { PreviewJpeg = [] }));

        Assert.Empty(raw.Previews);
        Assert.Null(raw.ReadPreviewJpeg());
    }

    [Fact]
    public void A_preview_that_goes_beyond_the_end_of_the_file_is_left_out()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { PreviewJpeg = SmallJpeg });
        using (RawFile intact = RawFile.Open(file))
        {
            Assert.Single(intact.Previews);
        }

        int lengthTag = FindPreviewLengthValue(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(lengthTag), 0x7FFFFFF0);

        using RawFile raw = RawFile.Open(file);
        Assert.Empty(raw.Previews);
        Assert.Equal("NIKON Z 6", raw.Metadata.Model);
    }

    [Fact]
    public void Data_that_is_not_a_JPEG_stream_is_reported_as_corrupt()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { PreviewJpeg = SmallJpeg });
        int start = file.AsSpan().IndexOf(SmallJpeg);
        file[start] = 0;
        using RawFile raw = RawFile.Open(file);

        var error = Assert.Throws<CorruptRawFileException>(() => raw.ReadPreviewJpeg());

        Assert.Equal(start, error.Position);
    }

    [Fact]
    public void A_preview_of_another_file_or_a_disposed_file_is_refused()
    {
        byte[] bytes = SyntheticNef.Build(new SyntheticNefOptions { PreviewJpeg = SmallJpeg });
        using RawFile first = RawFile.Open(bytes);
        RawFile second = RawFile.Open(bytes);

        Assert.Throws<ArgumentException>(() => first.ReadPreviewJpeg(second.Previews[0]));
        Assert.Throws<ArgumentNullException>(() => first.ReadPreviewJpeg(null!));
        second.Dispose();
        Assert.Throws<ObjectDisposedException>(() => second.ReadPreviewJpeg());
    }

    [Fact]
    public void Reading_a_preview_into_a_buffer_does_not_allocate()
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { PreviewJpeg = LargeJpeg }));
        byte[] buffer = new byte[64];
        raw.ReadPreviewJpeg(raw.Previews[0], buffer);

        long before = GC.GetAllocatedBytesForCurrentThread();
        raw.ReadPreviewJpeg(raw.Previews[0], buffer);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [SkippableFact]
    public void The_preview_of_a_real_file_is_a_JPEG_stream_and_is_read_quickly()
    {
        string? directory = Environment.GetEnvironmentVariable("RAWNET_SAMPLES_DIR");
        Skip.If(string.IsNullOrEmpty(directory) || !Directory.Exists(directory), "RAWNET_SAMPLES_DIR is not set to a folder.");
        string? path = Directory.EnumerateFiles(directory!, "*.NEF").Order(StringComparer.Ordinal).FirstOrDefault();
        Skip.If(path is null, "No .NEF file in RAWNET_SAMPLES_DIR.");

        using RawFile raw = RawFile.Open(path!);
        byte[]? jpeg = raw.ReadPreviewJpeg();

        Assert.NotNull(jpeg);
        Assert.True(jpeg.Length > 1000 && jpeg[0] == 0xFF && jpeg[1] == 0xD8 && jpeg[^2] == 0xFF && jpeg[^1] == 0xD9);
    }

    [Fact]
    public void A_preview_that_is_listed_twice_is_listed_once_and_ties_keep_the_file_order()
    {
        var ranges = new List<Eleris.RawNet.Internal.ByteRange>
        {
            new(500, 10),
            new(100, 30),
            new(100, 30),
            new(300, 30),
            new(100, 10),
        };

        Eleris.RawNet.Internal.ByteRange[] sorted = Eleris.RawNet.Internal.NefStructureReader.LargestFirst(ranges);

        Assert.Equal(
            [new Eleris.RawNet.Internal.ByteRange(100, 30), new(300, 30), new(100, 10), new(500, 10)],
            sorted);
        Assert.Empty(Eleris.RawNet.Internal.NefStructureReader.LargestFirst([]));
    }

    [Theory]
    [InlineData(0, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(0, 1, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 7, true)]
    public void The_range_of_a_preview_must_end_at_the_end_of_the_file_at_the_latest(int overshoot, int length, bool accepted)
    {
        // The preview data is moved to the end of the file: its range may touch the end, not go beyond it.
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { PreviewJpeg = SmallJpeg, RawStrip = [0xFF, 0xD8, 0xFF, 0xD9, 0xFF, 0xD9, 0xFF, 0xD9] });
        List<TinyEntry> main = TinyTiff.ReadIfd(file, 8);
        uint previewIfd = TinyTiff.Longs(file, TinyTiff.Find(main, 330))[0];
        List<TinyEntry> entries = TinyTiff.ReadIfd(file, previewIfd);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan((int)TinyTiff.Find(entries, 513).ValueOffset), (uint)(file.Length - length + overshoot));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan((int)TinyTiff.Find(entries, 514).ValueOffset), (uint)length);

        using RawFile raw = RawFile.Open(file);

        Assert.Equal(accepted ? 1 : 0, raw.Previews.Count);
    }

    [Fact]
    public void Every_way_to_read_a_preview_refuses_a_disposed_file()
    {
        RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { PreviewJpeg = SmallJpeg }));
        RawPreview preview = raw.Previews[0];
        raw.Dispose();
        raw.Dispose();

        Assert.Throws<ObjectDisposedException>(() => raw.ReadPreviewJpeg(preview));
        Assert.Throws<ObjectDisposedException>(() => raw.ReadPreviewJpeg(preview, new byte[16]));
        Assert.Throws<ObjectDisposedException>(() => raw.ReadPreviewJpeg());
    }

    [Fact]
    public void A_file_opened_from_a_path_can_be_read_by_several_threads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rawnet-{Guid.NewGuid():N}.nef");
        File.WriteAllBytes(path, SyntheticNef.Build(new SyntheticNefOptions { PreviewJpeg = LargeJpeg, MakerNotePreviewJpeg = SmallJpeg }));
        try
        {
            using RawFile raw = RawFile.Open(path);

            Parallel.For(0, 200, _ =>
            {
                Assert.Equal(LargeJpeg, raw.ReadPreviewJpeg());
                Assert.Equal(SmallJpeg, raw.ReadPreviewJpeg(raw.Previews[1]));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The offset of the LONG value of tag 514 in the preview directory (the first sub-directory of the default file).
    private static int FindPreviewLengthValue(byte[] file)
    {
        List<TinyEntry> main = TinyTiff.ReadIfd(file, 8);
        uint previewIfd = TinyTiff.Longs(file, TinyTiff.Find(main, 330))[0];
        return (int)TinyTiff.Find(TinyTiff.ReadIfd(file, previewIfd), 514).ValueOffset;
    }
}
