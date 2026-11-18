using System.Buffers.Binary;
using System.Text;

namespace Eleris.RawNet.Tests.Synthetic;

public class SyntheticNefTests
{
    private static List<TinyEntry> MainIfd(byte[] file) => TinyTiff.ReadIfd(file, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)));

    private static List<TinyEntry> RawIfd(byte[] file)
    {
        uint[] subs = TinyTiff.Longs(file, TinyTiff.Find(MainIfd(file), 330));
        return TinyTiff.ReadIfd(file, subs[1]);
    }

    private static List<TinyEntry> Exif(byte[] file) =>
        TinyTiff.ReadIfd(file, TinyTiff.Longs(file, TinyTiff.Find(MainIfd(file), 34665))[0]);

    [Fact]
    public void The_file_starts_with_a_little_endian_TIFF_header()
    {
        byte[] file = SyntheticNef.Build();

        Assert.Equal("II", Encoding.ASCII.GetString(file, 0, 2));
        Assert.Equal(42, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(2)));
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)));
    }

    [Fact]
    public void Two_builds_with_the_same_options_are_identical()
    {
        Assert.Equal(SyntheticNef.Build(), SyntheticNef.Build());
    }

    [Fact]
    public void Tags_are_sorted_and_values_lie_inside_the_file_in_every_directory()
    {
        byte[] file = SyntheticNef.Build();
        uint[] subs = TinyTiff.Longs(file, TinyTiff.Find(MainIfd(file), 330));
        var directories = new List<List<TinyEntry>> { MainIfd(file), Exif(file) };
        directories.AddRange(subs.Select(s => TinyTiff.ReadIfd(file, s)));

        foreach (List<TinyEntry> directory in directories)
        {
            Assert.Equal(directory.Select(e => e.Tag).Order(), directory.Select(e => e.Tag));
            foreach (TinyEntry entry in directory)
            {
                Assert.InRange(entry.ValueOffset + ((long)entry.Count * TinyTiff.TypeSize(entry.Type)), 0, file.Length);
            }
        }
    }

    [Fact]
    public void The_main_directory_describes_the_camera()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { Make = "MAKER", Model = "Model X" });
        List<TinyEntry> main = MainIfd(file);

        Assert.Equal("MAKER", TinyTiff.Text(file, TinyTiff.Find(main, 271)));
        Assert.Equal("Model X", TinyTiff.Text(file, TinyTiff.Find(main, 272)));
        Assert.Equal(2, TinyTiff.Longs(file, TinyTiff.Find(main, 330)).Length);
    }

    [Fact]
    public void The_raw_directory_describes_a_CFA_image_and_points_at_the_strip()
    {
        byte[] strip = [1, 2, 3, 4, 5, 6];
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { RawStrip = strip, RawWidth = 3, RawHeight = 1 });
        List<TinyEntry> raw = RawIfd(file);

        Assert.Equal(3u, TinyTiff.Longs(file, TinyTiff.Find(raw, 256))[0]);
        Assert.Equal(1u, TinyTiff.Longs(file, TinyTiff.Find(raw, 257))[0]);
        Assert.Equal(14, TinyTiff.Shorts(file, TinyTiff.Find(raw, 258))[0]);
        Assert.Equal(SyntheticNef.NikonCompression, TinyTiff.Shorts(file, TinyTiff.Find(raw, 259))[0]);
        Assert.Equal(SyntheticNef.CfaPhotometric, TinyTiff.Shorts(file, TinyTiff.Find(raw, 262))[0]);
        Assert.Equal<byte>([0, 1, 1, 2], file.AsSpan((int)TinyTiff.Find(raw, 33422).ValueOffset, 4).ToArray());
        int offset = (int)TinyTiff.Longs(file, TinyTiff.Find(raw, 273))[0];
        int length = (int)TinyTiff.Longs(file, TinyTiff.Find(raw, 279))[0];
        Assert.Equal(strip, file.AsSpan(offset, length).ToArray());
    }

    [Fact]
    public void The_default_strip_has_two_bytes_per_pixel()
    {
        byte[] file = SyntheticNef.Build();

        Assert.Equal(64u * 48u * 2u, TinyTiff.Longs(file, TinyTiff.Find(RawIfd(file), 279))[0]);
    }

    [Fact]
    public void The_Exif_directory_holds_the_shooting_data()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { Iso = 3200, ExposureTime = (1, 250) });
        List<TinyEntry> exif = Exif(file);

        Assert.Equal(3200, TinyTiff.Shorts(file, TinyTiff.Find(exif, 34855))[0]);
        Assert.Equal<(uint, uint)>((1, 250), TinyTiff.Rationals(file, TinyTiff.Find(exif, 33434))[0]);
        Assert.Equal("+01:00", TinyTiff.Text(file, TinyTiff.Find(exif, 36880)));
    }

    [Fact]
    public void The_MakerNote_has_its_own_header_and_offsets_that_count_from_it()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { SerialNumber = "7654321", ShutterCount = 4242 });
        TinyEntry makerNote = TinyTiff.Find(Exif(file), 37500);
        long start = makerNote.ValueOffset;

        Assert.Equal("Nikon\0", Encoding.ASCII.GetString(file, (int)start, 6));
        long origin = start + 10;
        Assert.Equal("II", Encoding.ASCII.GetString(file, (int)origin, 2));
        long directory = origin + BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)origin + 4));
        List<TinyEntry> entries = TinyTiff.ReadIfd(file, directory, origin);

        Assert.Equal("7654321", TinyTiff.Text(file, TinyTiff.Find(entries, 0x1D)));
        Assert.Equal(4242u, TinyTiff.Longs(file, TinyTiff.Find(entries, 0xA7))[0]);
        Assert.Equal<ushort>([1008, 1008, 1008, 1008], TinyTiff.Shorts(file, TinyTiff.Find(entries, 0x3D)));
        Assert.Equal<ushort>([4, 4, 56, 40], TinyTiff.Shorts(file, TinyTiff.Find(entries, 0x45)));
        Assert.Equal((854u, 512u), TinyTiff.Rationals(file, TinyTiff.Find(entries, 0x0C))[0]);
    }

    private static List<(string Name, long Start, long End)> Regions(byte[] file)
    {
        var regions = new List<(string, long, long)> { ("header", 0, 8) };
        uint[] subs = TinyTiff.Longs(file, TinyTiff.Find(MainIfd(file), 330));
        long mainOffset = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4));
        var directories = new List<(string Name, long Offset, List<TinyEntry> Entries)>
        {
            ("main", mainOffset, MainIfd(file)),
            ("preview", subs[0], TinyTiff.ReadIfd(file, subs[0])),
            ("raw", subs[1], TinyTiff.ReadIfd(file, subs[1])),
            ("exif", TinyTiff.Longs(file, TinyTiff.Find(MainIfd(file), 34665))[0], Exif(file)),
        };
        foreach ((string name, long offset, List<TinyEntry> entries) in directories)
        {
            regions.Add((name + " directory", offset, offset + 2 + (12 * entries.Count) + 4));
            foreach (TinyEntry entry in entries.Where(e => e.Count * TinyTiff.TypeSize(e.Type) > 4))
            {
                regions.Add(($"{name} value {entry.Tag}", entry.ValueOffset, entry.ValueOffset + (entry.Count * TinyTiff.TypeSize(entry.Type))));
            }
        }

        List<TinyEntry> raw = TinyTiff.ReadIfd(file, subs[1]);
        List<TinyEntry> preview = TinyTiff.ReadIfd(file, subs[0]);
        List<TinyEntry> main = MainIfd(file);
        regions.Add(("thumbnail", TinyTiff.Longs(file, TinyTiff.Find(main, 273))[0], TinyTiff.Longs(file, TinyTiff.Find(main, 273))[0] + TinyTiff.Longs(file, TinyTiff.Find(main, 279))[0]));
        regions.Add(("preview data", TinyTiff.Longs(file, TinyTiff.Find(preview, 513))[0], TinyTiff.Longs(file, TinyTiff.Find(preview, 513))[0] + TinyTiff.Longs(file, TinyTiff.Find(preview, 514))[0]));
        regions.Add(("raw strip", TinyTiff.Longs(file, TinyTiff.Find(raw, 273))[0], TinyTiff.Longs(file, TinyTiff.Find(raw, 273))[0] + TinyTiff.Longs(file, TinyTiff.Find(raw, 279))[0]));
        return regions;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 5)]
    [InlineData(6, 4)]
    public void Regions_of_the_file_do_not_overlap_and_the_values_start_on_even_offsets(int stripLength, int previewLength)
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions
        {
            RawStrip = stripLength == 0 ? null : new byte[stripLength],
            PreviewJpeg = new byte[previewLength == 0 ? 4 : previewLength],
        });

        var regions = Regions(file).OrderBy(r => r.Start).ToList();

        for (int i = 1; i < regions.Count; i++)
        {
            Assert.True(regions[i - 1].End <= regions[i].Start, $"{regions[i - 1].Name} overlaps {regions[i].Name}");
        }

        Assert.All(regions.Where(r => r.Name != "header"), r => Assert.Equal(0, r.Start % 2));
        Assert.Equal(0, file.Length % 2);
        Assert.InRange(file.Length - regions[^1].End, 0, 1);
    }

    [Fact]
    public void An_odd_strip_keeps_its_exact_length_and_the_file_stays_even()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { RawStrip = [1, 2, 3, 4, 5, 6, 7], PreviewJpeg = [1, 2, 3, 4, 5] });
        List<TinyEntry> raw = RawIfd(file);

        Assert.Equal(7u, TinyTiff.Longs(file, TinyTiff.Find(raw, 279))[0]);
        int offset = (int)TinyTiff.Longs(file, TinyTiff.Find(raw, 273))[0];
        Assert.Equal<byte>([1, 2, 3, 4, 5, 6, 7], file.AsSpan(offset, 7).ToArray());
    }

    [Fact]
    public void Descriptive_text_and_the_preview_are_stored_as_given()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { Software = "Soft 1", LensModel = "Lens X", PreviewJpeg = [0xFF, 0xD8, 9, 0xFF, 0xD9] });
        uint[] subs = TinyTiff.Longs(file, TinyTiff.Find(MainIfd(file), 330));
        List<TinyEntry> preview = TinyTiff.ReadIfd(file, subs[0]);

        Assert.Equal("Soft 1", TinyTiff.Text(file, TinyTiff.Find(MainIfd(file), 305)));
        Assert.Equal("Lens X", TinyTiff.Text(file, TinyTiff.Find(Exif(file), 42036)));
        Assert.Equal(5u, TinyTiff.Longs(file, TinyTiff.Find(preview, 514))[0]);
        int offset = (int)TinyTiff.Longs(file, TinyTiff.Find(preview, 513))[0];
        Assert.Equal<byte>([0xFF, 0xD8, 9, 0xFF, 0xD9], file.AsSpan(offset, 5).ToArray());
    }

    [Fact]
    public void The_default_strip_follows_its_documented_pattern()
    {
        byte[] file = SyntheticNef.Build();
        int offset = (int)TinyTiff.Longs(file, TinyTiff.Find(RawIfd(file), 273))[0];

        Assert.Equal(0, file[offset]);
        Assert.Equal(31, file[offset + 1]);
    }

    [Fact]
    public void A_directory_refuses_a_tag_twice()
    {
        var directory = new IfdBuilder().Shorts(256, 1);

        Assert.Throws<InvalidOperationException>(() => directory.Shorts(256, 2));
    }

    [Fact]
    public void Options_change_the_raw_description()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions { BitsPerSample = 12, Compression = 1, RawWidth = 10, RawHeight = 6 });
        List<TinyEntry> raw = RawIfd(file);

        Assert.Equal(12, TinyTiff.Shorts(file, TinyTiff.Find(raw, 258))[0]);
        Assert.Equal(1, TinyTiff.Shorts(file, TinyTiff.Find(raw, 259))[0]);
        Assert.Equal(10u, TinyTiff.Longs(file, TinyTiff.Find(raw, 256))[0]);
    }
}
