using Eleris.RawNet.Tests.Synthetic;

namespace Eleris.RawNet.Tests;

public class BlackLevelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_black_level_of_the_four_photosites_is_read_in_the_order_of_the_pattern(bool bigEndian)
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { BlackLevel = [1008, 1010, 1012, 1014], BigEndian = bigEndian }));

        Assert.Equal(new RawBlackLevel(1008, 1010, 1012, 1014), raw.Metadata.BlackLevel);
    }

    [Fact]
    public void The_default_synthetic_file_has_the_black_level_of_a_Z6()
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build());

        Assert.Equal(new RawBlackLevel(1008, 1008, 1008, 1008), raw.Metadata.BlackLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void A_record_that_does_not_hold_four_values_is_no_black_level(int count)
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { BlackLevel = [.. Enumerable.Repeat((ushort)1008, count)] }));

        Assert.Null(raw.Metadata.BlackLevel);
    }

    [Theory]
    [InlineData(12, 0, 1, 1, 2)]
    [InlineData(14, 1, 0, 2, 1)]
    [InlineData(14, 1, 2, 0, 1)]
    [InlineData(14, 2, 1, 1, 0)]
    public void A_black_level_is_only_read_for_the_layout_that_was_checked_on_camera_files(int bits, byte a, byte b, byte c, byte d)
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { BlackLevel = [100, 100, 100, 100], BitsPerSample = bits, CfaPattern = [a, b, c, d] }));

        Assert.Null(raw.Metadata.BlackLevel);
    }

    [Fact]
    public void A_value_that_does_not_fit_the_samples_is_no_black_level()
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { BlackLevel = [1008, 1008, 1008, 1 << 14], BitsPerSample = 14 }));
        Assert.Null(raw.Metadata.BlackLevel);

        using RawFile zero = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { BlackLevel = [0, 16383, 0, 16383] }));
        Assert.Equal(new RawBlackLevel(0, 16383, 0, 16383), zero.Metadata.BlackLevel);
    }

    [Fact]
    public void The_level_of_a_sample_follows_its_place_in_the_pattern()
    {
        var level = new RawBlackLevel(1, 2, 3, 4);

        Assert.Equal([1, 2, 1, 2], new[] { level.At(0, 0), level.At(1, 0), level.At(2, 0), level.At(3, 2) });
        Assert.Equal([3, 4, 3, 4], new[] { level.At(0, 1), level.At(1, 1), level.At(2, 3), level.At(3, 5) });
    }

    [Fact]
    public void The_decoded_image_carries_the_black_level()
    {
        ushort[] samples = new ushort[16 * 8];
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions
        {
            RawWidth = 16,
            RawHeight = 8,
            BitsPerSample = 14,
            Compression = 1,
            ActiveArea = [0, 0, 16, 8],
            BlackLevel = [10, 20, 30, 40],
            RawStrip = DecodeTests.Pack(samples, 16, 14),
        });
        using RawFile raw = RawFile.Open(file);

        Assert.Equal(new RawBlackLevel(10, 20, 30, 40), raw.Decode().BlackLevel);
    }

    [SkippableFact]
    public void Every_real_file_has_a_black_level_that_fits_its_samples()
    {
        string? directory = Environment.GetEnvironmentVariable("RAWNET_SAMPLES_DIR");
        Skip.If(string.IsNullOrEmpty(directory) || !Directory.Exists(directory), "RAWNET_SAMPLES_DIR is not set to a folder.");
        string[] files = [.. Directory.EnumerateFiles(directory!, "*.NEF")];
        Skip.If(files.Length == 0, "No .NEF file in RAWNET_SAMPLES_DIR.");

        foreach (string path in files)
        {
            RawMetadata metadata = RawFile.ReadMetadata(path);
            Assert.NotNull(metadata.BlackLevel);
            RawBlackLevel level = metadata.BlackLevel.Value;
            Assert.All(new[] { level.TopLeft, level.TopRight, level.BottomLeft, level.BottomRight }, v => Assert.InRange(v, 0, (1 << metadata.BitsPerSample) - 1));
        }
    }
}
