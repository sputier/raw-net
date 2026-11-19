using Eleris.RawNet.Tests.Synthetic;

namespace Eleris.RawNet.Tests;

public class WhiteBalanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_multipliers_are_read_from_the_record_of_the_maker_note(bool bigEndian)
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { BigEndian = bigEndian }));

        RawWhiteBalance balance = raw.Metadata.WhiteBalance!.Value;

        Assert.Equal(854 / 512.0, balance.Red);
        Assert.Equal(683 / 512.0, balance.Blue);
        Assert.Equal(1.0, balance.Green1);
        Assert.Equal(1.0, balance.Green2);
        Assert.Equal(RawWhiteBalanceSource.NikonMakerNoteLevels, balance.Source);
    }

    [Fact]
    public void The_order_of_the_record_is_red_blue_green_green()
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { WhiteBalance = [(2, 1), (3, 1), (5, 4), (7, 4)] }));

        Assert.Equal(new RawWhiteBalance(2, 1.25, 1.75, 3, RawWhiteBalanceSource.NikonMakerNoteLevels), raw.Metadata.WhiteBalance);
    }

    [Theory]
    [InlineData(0u, 1u, 0)]
    [InlineData(1u, 0u, 1)]
    [InlineData(1000u, 1u, 2)]
    [InlineData(1u, 4294967295u, 3)]
    [InlineData(65u, 1u, 0)]
    [InlineData(1u, 65u, 1)]
    public void A_record_with_an_impossible_multiplier_is_no_white_balance(uint numerator, uint denominator, int position)
    {
        var record = new (uint, uint)[] { (2, 1), (3, 1), (1, 1), (1, 1) };
        record[position] = (numerator, denominator);
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { WhiteBalance = record }));

        Assert.Null(raw.Metadata.WhiteBalance);
    }

    [Theory]
    [InlineData(64u, 1u)]
    [InlineData(1u, 64u)]
    public void The_limits_of_the_plausible_range_are_accepted(uint numerator, uint denominator)
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { WhiteBalance = [(numerator, denominator), (numerator, denominator), (numerator, denominator), (numerator, denominator)] }));

        Assert.NotNull(raw.Metadata.WhiteBalance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void A_record_that_does_not_hold_four_values_is_no_white_balance(int count)
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { WhiteBalance = [.. Enumerable.Repeat((2u, 1u), count)] }));

        Assert.Null(raw.Metadata.WhiteBalance);
    }

    [Fact]
    public void The_decoded_image_carries_the_white_balance()
    {
        byte[] file = SyntheticNef.Build(new SyntheticNefOptions
        {
            RawWidth = 16,
            RawHeight = 8,
            BitsPerSample = 14,
            Compression = 1,
            ActiveArea = [0, 0, 16, 8],
            RawStrip = DecodeTests.Pack(new ushort[16 * 8], 16, 14),
        });
        using RawFile raw = RawFile.Open(file);

        RawImage image = raw.Decode();

        Assert.Equal(raw.Metadata.WhiteBalance, image.WhiteBalance);
        Assert.NotNull(image.WhiteBalance);
    }

    [Fact]
    public void A_default_white_balance_has_an_unknown_source()
    {
        Assert.Equal(RawWhiteBalanceSource.Unknown, default(RawWhiteBalance).Source);
    }

    [Fact]
    public void A_white_balance_is_read_whatever_the_colour_filter_pattern()
    {
        using RawFile raw = RawFile.Open(SyntheticNef.Build(new SyntheticNefOptions { CfaPattern = [1, 0, 2, 1] }));

        Assert.NotNull(raw.Metadata.WhiteBalance);
    }

    [SkippableFact]
    public void Every_real_file_has_a_plausible_white_balance()
    {
        string? directory = Environment.GetEnvironmentVariable("RAWNET_SAMPLES_DIR");
        Skip.If(string.IsNullOrEmpty(directory) || !Directory.Exists(directory), "RAWNET_SAMPLES_DIR is not set to a folder.");
        string[] files = [.. Directory.EnumerateFiles(directory!, "*.NEF")];
        Skip.If(files.Length == 0, "No .NEF file in RAWNET_SAMPLES_DIR.");

        foreach (string path in files)
        {
            RawWhiteBalance balance = RawFile.ReadMetadata(path).WhiteBalance
                ?? throw new Xunit.Sdk.XunitException("A real file has no white balance.");
            Assert.InRange(balance.Red, 0.5, 8);
            Assert.InRange(balance.Blue, 0.5, 8);
            Assert.InRange(balance.Green1, 0.5, 2);
            Assert.InRange(balance.Green2, 0.5, 2);
        }
    }
}
