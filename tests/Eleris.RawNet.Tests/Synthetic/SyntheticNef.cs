using System.Buffers.Binary;

namespace Eleris.RawNet.Tests.Synthetic;

/// <summary>What a synthetic NEF file holds. The defaults describe a tiny file shaped like the output of a Nikon Z 6.</summary>
internal sealed record SyntheticNefOptions
{
    public string Make { get; init; } = "NIKON CORPORATION";

    public string Model { get; init; } = "NIKON Z 6";

    public string Software { get; init; } = "Ver.00.00";

    public string DateTime { get; init; } = "2026:01:02 03:04:05";

    public string OffsetTime { get; init; } = "+01:00";

    public string SerialNumber { get; init; } = "1234567";

    public string LensModel { get; init; } = "Test lens 50mm f/1.8";

    public uint ShutterCount { get; init; } = 100;

    /// <summary>Size of the full raw image, margins included.</summary>
    public int RawWidth { get; init; } = 64;

    public int RawHeight { get; init; } = 48;

    public int BitsPerSample { get; init; } = 14;

    /// <summary>The TIFF compression code of the raw image: 1 for none, 34713 for the Nikon compression.</summary>
    public int Compression { get; init; } = SyntheticNef.NikonCompression;

    /// <summary>The colours of the 2 x 2 pattern, in reading order: 0 red, 1 green, 2 blue.</summary>
    public byte[] CfaPattern { get; init; } = [0, 1, 1, 2];

    public ushort[] BlackLevel { get; init; } = [1008, 1008, 1008, 1008];

    /// <summary>Left, top, width and height of the active area.</summary>
    public ushort[] ActiveArea { get; init; } = [4, 4, 56, 40];

    /// <summary>White balance as fractions: red, blue, green, green.</summary>
    public (uint Numerator, uint Denominator)[] WhiteBalance { get; init; } = [(854, 512), (683, 512), (512, 512), (512, 512)];

    public (uint Numerator, uint Denominator) ExposureTime { get; init; } = (16, 10);

    public (uint Numerator, uint Denominator) FNumber { get; init; } = (180, 100);

    public (uint Numerator, uint Denominator) FocalLength { get; init; } = (500, 10);

    public ushort Iso { get; init; } = 1600;

    /// <summary>True to write the file (and its MakerNote) in big-endian byte order, as older Nikon bodies do.</summary>
    public bool BigEndian { get; init; }

    /// <summary>False to leave the body serial number out of the Exif directory (it stays in the MakerNote).</summary>
    public bool IncludeExifSerial { get; init; } = true;

    /// <summary>False to leave DateTimeOriginal (and so its offset) out: only the date of the main directory remains.</summary>
    public bool IncludeDateTimeOriginal { get; init; } = true;

    /// <summary>The OffsetTimeOriginal tag; when null it has the value of <see cref="OffsetTime"/>.</summary>
    public string? OffsetTimeOriginal { get; init; }

    /// <summary>True to add a small reduced raw image (16 x 8, 12 bit) as the first sub-directory, as the camera does.</summary>
    public bool SmallCfaSubImage { get; init; }

    /// <summary>The bytes of the raw strip; by default a repeatable pattern of <c>RawWidth * RawHeight * 2</c> bytes.</summary>
    public byte[]? RawStrip { get; init; }

    /// <summary>The bytes of the embedded preview; by default a minimal JPEG start and end of image.</summary>
    public byte[] PreviewJpeg { get; init; } = [0xFF, 0xD8, 0xFF, 0xD9];
}

/// <summary>
/// Builds small NEF-like files for tests: a little-endian TIFF with a thumbnail, two sub-directories (a JPEG preview and the
/// raw image), an Exif directory and a Nikon MakerNote, laid out like the files of the camera. The pixel data is whatever the
/// test supplies. No real photograph, and nothing of a real camera, is in these files.
/// </summary>
internal static class SyntheticNef
{
    public const int NikonCompression = 34713;

    public const int CfaPhotometric = 32803;

    public static byte[] Build(SyntheticNefOptions? options = null)
    {
        options ??= new SyntheticNefOptions();
        byte[] rawStrip = options.RawStrip ?? DefaultStrip(options);
        byte[] thumbnail = new byte[8 * 6 * 3];

        uint thumbnailOffset = 0, previewOffset = 0, rawOffset = 0, previewIfd = 0, rawIfd = 0, exifIfd = 0;

        byte[] makerNote = BuildMakerNote(options);
        IfdBuilder exif = new IfdBuilder(options.BigEndian)
            .Rationals(33434, options.ExposureTime)
            .Rationals(33437, options.FNumber)
            .Shorts(34855, options.Iso)
            .Text(36880, options.OffsetTime)
            .Text(36881, options.OffsetTimeOriginal ?? options.OffsetTime)
            .Rationals(37386, options.FocalLength)
            .Undefined(37500, makerNote)
            .Text(42036, options.LensModel);
        if (options.IncludeExifSerial)
        {
            exif.Text(42033, options.SerialNumber);
        }

        if (options.IncludeDateTimeOriginal)
        {
            exif.Text(36867, options.DateTime);
        }

        uint smallIfd = 0;
        IfdBuilder small = new IfdBuilder(options.BigEndian)
            .Longs(254, 1)
            .Longs(256, 16)
            .Longs(257, 8)
            .Shorts(258, 12)
            .Shorts(259, (ushort)options.Compression)
            .Shorts(262, CfaPhotometric)
            .LazyLongs(273, 1, () => [thumbnailOffset])
            .Shorts(277, 1)
            .Longs(278, 8)
            .Longs(279, 4)
            .Shorts(33421, 2, 2)
            .Bytes(33422, options.CfaPattern);

        IfdBuilder preview = new IfdBuilder(options.BigEndian)
            .Longs(254, 1)
            .Shorts(259, 6)
            .LazyLongs(513, 1, () => [previewOffset])
            .Longs(514, (uint)options.PreviewJpeg.Length);
        IfdBuilder raw = new IfdBuilder(options.BigEndian)
            .Longs(254, 0)
            .Longs(256, (uint)options.RawWidth)
            .Longs(257, (uint)options.RawHeight)
            .Shorts(258, (ushort)options.BitsPerSample)
            .Shorts(259, (ushort)options.Compression)
            .Shorts(262, CfaPhotometric)
            .LazyLongs(273, 1, () => [rawOffset])
            .Shorts(277, 1)
            .Longs(278, (uint)options.RawHeight)
            .Longs(279, (uint)rawStrip.Length)
            .Shorts(33421, 2, 2)
            .Bytes(33422, options.CfaPattern);
        IfdBuilder main = new IfdBuilder(options.BigEndian)
            .Longs(254, 1)
            .Longs(256, 8)
            .Longs(257, 6)
            .Shorts(258, 8, 8, 8)
            .Shorts(259, 1)
            .Shorts(262, 2)
            .Text(271, options.Make)
            .Text(272, options.Model)
            .LazyLongs(273, 1, () => [thumbnailOffset])
            .Shorts(274, 1)
            .Shorts(277, 3)
            .Longs(278, 6)
            .Longs(279, (uint)thumbnail.Length)
            .Text(305, options.Software)
            .Text(306, options.DateTime)
            .LazyLongs(330, options.SmallCfaSubImage ? 3 : 2, () => options.SmallCfaSubImage ? [smallIfd, previewIfd, rawIfd] : [previewIfd, rawIfd])
            .LazyLongs(34665, 1, () => [exifIfd]);

        // Layout: header, main directory, sub-directories, Exif directory, then the pixel data, all on even offsets.
        long position = 8;
        long mainIfd = position;
        position += main.Size;
        previewIfd = (uint)position;
        position += preview.Size;
        rawIfd = (uint)position;
        position += raw.Size;
        exifIfd = (uint)position;
        position += exif.Size;
        smallIfd = (uint)position;
        if (options.SmallCfaSubImage)
        {
            position += small.Size;
        }

        thumbnailOffset = (uint)position;
        position += thumbnail.Length;
        previewOffset = (uint)position;
        position += Even(options.PreviewJpeg.Length);
        rawOffset = (uint)position;
        position += Even(rawStrip.Length);

        byte[] file = new byte[position];
        file[0] = file[1] = options.BigEndian ? (byte)'M' : (byte)'I';
        if (options.BigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(2), 42);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)mainIfd);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 42);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)mainIfd);
        }
        main.Serialize(mainIfd).CopyTo(file, mainIfd);
        preview.Serialize(previewIfd).CopyTo(file, previewIfd);
        raw.Serialize(rawIfd).CopyTo(file, rawIfd);
        exif.Serialize(exifIfd).CopyTo(file, exifIfd);
        if (options.SmallCfaSubImage)
        {
            small.Serialize(smallIfd).CopyTo(file, smallIfd);
        }

        thumbnail.CopyTo(file, thumbnailOffset);
        options.PreviewJpeg.CopyTo(file, previewOffset);
        rawStrip.CopyTo(file, rawOffset);
        return file;
    }

    /// <summary>The MakerNote: the "Nikon" header, then a TIFF header and a directory whose offsets count from that header.</summary>
    private static byte[] BuildMakerNote(SyntheticNefOptions options)
    {
        IfdBuilder directory = new IfdBuilder(options.BigEndian)
            .Text(0x04, "RAW")
            .Rationals(0x0C, options.WhiteBalance)
            .Text(0x1D, options.SerialNumber)
            .Shorts(0x3D, options.BlackLevel)
            .Shorts(0x45, options.ActiveArea)
            .Longs(0xA7, options.ShutterCount);
        byte[] header = [(byte)'N', (byte)'i', (byte)'k', (byte)'o', (byte)'n', 0, 2, 0x11, 0, 0];
        byte[] tiffHeader = options.BigEndian
            ? [(byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8]
            : [(byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0];
        return [.. header, .. tiffHeader, .. directory.Serialize(8)];
    }

    private static byte[] DefaultStrip(SyntheticNefOptions options)
    {
        byte[] strip = new byte[checked(options.RawWidth * options.RawHeight * 2)];
        for (int i = 0; i < strip.Length; i++)
        {
            strip[i] = (byte)((i * 31) + (i >> 8));
        }

        return strip;
    }

    private static long Even(long length) => (length + 1) & ~1L;
}
