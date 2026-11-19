using System.Globalization;

namespace Eleris.RawNet.Internal;

/// <summary>Reads the structure of a NEF file with the TIFF reader of the library.</summary>
internal sealed class NefStructureReader : INefStructureReader
{
    private const ushort MakeTag = 271, ModelTag = 272, SoftwareTag = 305, DateTimeTag = 306, SubIfdsTag = 330, ExifTag = 34665;
    private const ushort WidthTag = 256, HeightTag = 257, BitsTag = 258, CompressionTag = 259, PhotometricTag = 262, StripOffsetsTag = 273, StripByteCountsTag = 279;
    private const ushort CfaDimensionTag = 33421, CfaPatternTag = 33422;
    private const ushort ExposureTimeTag = 33434, FNumberTag = 33437, IsoTag = 34855, DateTimeOriginalTag = 36867, OffsetTimeTag = 36880, OffsetTimeOriginalTag = 36881;
    private const ushort FocalLengthTag = 37386, MakerNoteTag = 37500, BodySerialTag = 42033, LensModelTag = 42036;
    private const ushort JpegOffsetTag = 513, JpegLengthTag = 514;
    private const int MaxPreviewBytes = 64 * 1024 * 1024;
    private const ushort NikonPreviewTag = 0x11;
    private const ushort NikonSerialTag = 0x1D, NikonActiveAreaTag = 0x45, NikonShutterCountTag = 0xA7, NikonCompressionTag = 0x96, NikonBlackLevelTag = 0x3D;
    private const int NikonHeaderBytes = 10;
    private const int CfaPhotometric = 32803;
    private const int MaxSubDirectories = 64;
    private const int MaxStrips = 65536;
    private const int MaxDimension = 1 << 20;

    public NefStructure Read(ByteSource source)
    {
        if (!TiffReader.TryReadHeader(source, 0, out TiffReader reader, out long firstDirectory))
        {
            throw new UnsupportedFormatException("The file is not a TIFF based file, so it is not a NEF.");
        }

        Ifd main = reader.ReadIfd(firstDirectory);
        string? make = main.TryGet(MakeTag, out IfdEntry makeEntry) ? reader.ReadText(makeEntry) : null;
        string? model = main.TryGet(ModelTag, out IfdEntry modelEntry) ? reader.ReadText(modelEntry) : null;
        if (string.IsNullOrEmpty(make))
        {
            throw new UnsupportedFormatException("The TIFF file does not say which camera made it, so it is not a camera raw file.");
        }

        if (!make.Contains("NIKON", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsupportedCameraException(make, model);
        }

        var previews = new List<ByteRange>();
        RawSubImage raw = ReadRawImage(reader, main, source.Length, previews);
        Ifd? exif = null;
        if (main.TryGet(ExifTag, out IfdEntry exifEntry) && reader.TryReadInteger(exifEntry, 0, out long exifOffset))
        {
            exif = reader.ReadIfd(exifOffset);
        }

        (TiffReader Reader, Ifd Directory)? makerNote = exif is not null ? ReadMakerNote(reader, exif) : null;
        RawRectangle activeArea = ReadActiveArea(makerNote, raw.Width, raw.Height);
        if (makerNote is { } preview && preview.Directory.TryGet(NikonPreviewTag, out IfdEntry previewEntry) && preview.Reader.TryReadInteger(previewEntry, 0, out long previewOffset))
        {
            ReadMakerNotePreview(preview.Reader, previewOffset, source.Length, previews);
        }

        // The clock offset must be the one of the date that is reported: OffsetTimeOriginal goes with DateTimeOriginal, OffsetTime with DateTime.
        string? dateText = Text(reader, exif, DateTimeOriginalTag);
        string? offsetText = dateText is null ? null : Text(reader, exif, OffsetTimeOriginalTag);
        if (dateText is null)
        {
            dateText = Text(reader, main, DateTimeTag);
            offsetText = Text(reader, exif, OffsetTimeTag);
        }
        string? serial = Text(reader, exif, BodySerialTag);
        long? shutterCount = null;
        if (makerNote is { } note)
        {
            serial ??= Text(note.Reader, note.Directory, NikonSerialTag);
            if (note.Directory.TryGet(NikonShutterCountTag, out IfdEntry countEntry) && note.Reader.TryReadInteger(countEntry, 0, out long count))
            {
                shutterCount = count;
            }
        }

        NikonCompressionHeader? compression = null;
        if (makerNote is { } header
            && header.Directory.TryGet(NikonCompressionTag, out IfdEntry compressionEntry)
            && compressionEntry.ByteLength >= NikonHeaderBytes)
        {
            Span<byte> bytes = stackalloc byte[NikonHeaderBytes];
            source.ReadExactly(compressionEntry.ValueOffset, bytes);
            compression = new NikonCompressionHeader(
                bytes[0],
                bytes[1],
                header.Reader.ReadUInt16(bytes[2..]),
                header.Reader.ReadUInt16(bytes[4..]),
                header.Reader.ReadUInt16(bytes[6..]),
                header.Reader.ReadUInt16(bytes[8..]));
        }

        var metadata = new RawMetadata
        {
            MediaType = RawMediaTypes.NikonNef,
            Make = make,
            Model = model ?? string.Empty,
            SerialNumber = string.IsNullOrEmpty(serial) ? null : serial,
            Software = Text(reader, main, SoftwareTag),
            LensModel = Text(reader, exif, LensModelTag),
            FocalLengthMillimetres = Rational(reader, exif, FocalLengthTag),
            FNumber = Rational(reader, exif, FNumberTag),
            ExposureTimeSeconds = Rational(reader, exif, ExposureTimeTag),
            Iso = exif is not null && exif.TryGet(IsoTag, out IfdEntry isoEntry) && reader.TryReadInteger(isoEntry, 0, out long iso) && iso is > 0 and <= int.MaxValue ? (int)iso : null,
            CaptureDateTime = ParseDateTime(dateText),
            CaptureUtcOffset = ParseOffset(offsetText),
            ShutterCount = shutterCount,
            ImageWidth = raw.Width,
            ImageHeight = raw.Height,
            ActiveArea = activeArea,
            BlackLevel = raw.Pattern == CfaPattern.Rggb && raw.Bits == 14 ? ReadBlackLevel(makerNote, raw.Bits) : null,
            CfaPattern = raw.Pattern,
            BitsPerSample = raw.Bits,
            Compression = Enum.IsDefined<RawCompression>((RawCompression)raw.Compression) ? (RawCompression)raw.Compression : RawCompression.Unknown,
            CompressionCode = raw.Compression,
        };
        return new NefStructure(metadata, raw.Strips, LargestFirst(previews), compression);
    }

    // The previews, largest first, each place once (a preview can be listed by several directories).
    internal static ByteRange[] LargestFirst(List<ByteRange> previews)
    {
        previews.Sort(static (a, b) => b.Length != a.Length ? b.Length.CompareTo(a.Length) : a.Offset.CompareTo(b.Offset));
        int kept = 0;
        for (int i = 0; i < previews.Count; i++)
        {
            if (kept == 0 || previews[i] != previews[kept - 1])
            {
                previews[kept++] = previews[i];
            }
        }

        if (kept == 0)
        {
            return [];
        }

        var result = new ByteRange[kept];
        previews.CopyTo(0, result, 0, kept);
        return result;
    }

    private sealed record RawSubImage(int Width, int Height, int Bits, int Compression, CfaPattern Pattern, ByteRange[] Strips);

    private static RawSubImage ReadRawImage(TiffReader reader, Ifd main, long fileLength, List<ByteRange> previews)
    {
        if (!main.TryGet(SubIfdsTag, out IfdEntry subEntry))
        {
            throw new UnsupportedFormatException("The file has no sub-directory, so it holds no raw image.");
        }

        // The raw image is the largest colour filter array image of the sub-directories (the file also holds reduced ones).
        // A sub-directory that cannot be read is skipped: another one may hold the image.
        Ifd? best = null;
        long bestArea = 0;
        bool damaged = false;
        int count = (int)Math.Min(subEntry.Count, MaxSubDirectories);
        for (int i = 0; i < count; i++)
        {
            if (!reader.TryReadInteger(subEntry, i, out long offset))
            {
                break;
            }

            Ifd sub;
            try
            {
                sub = reader.ReadIfd(offset);
            }
            catch (CorruptRawFileException)
            {
                damaged = true;
                continue;
            }

            TryAddPreview(reader, sub, reader.Origin, fileLength, previews);
            if (sub.TryGet(PhotometricTag, out IfdEntry photometric)
                && reader.TryReadInteger(photometric, 0, out long value)
                && value == CfaPhotometric
                && Integer(reader, sub, WidthTag) is > 0 and <= MaxDimension and long width
                && Integer(reader, sub, HeightTag) is > 0 and <= MaxDimension and long height
                && width * height > bestArea)
            {
                best = sub;
                bestArea = width * height;
            }
        }

        if (best is null)
        {
            throw damaged
                ? new CorruptRawFileException("The sub-directories that should hold the raw image are damaged")
                : new UnsupportedFormatException("The file holds no raw image of a kind the library reads (no colour filter array image in its sub-directories).");
        }

        long w = Integer(reader, best, WidthTag) ?? 0, h = Integer(reader, best, HeightTag) ?? 0;
        if (w is <= 0 or > MaxDimension || h is <= 0 or > MaxDimension)
        {
            throw new CorruptRawFileException("The raw image has an impossible size");
        }

        long bits = Integer(reader, best, BitsTag) ?? throw new CorruptRawFileException("The raw image does not say how many bits its samples have");
        if (bits is < 1 or > 32)
        {
            throw new CorruptRawFileException("The raw image has an impossible sample size");
        }

        long compression = Integer(reader, best, CompressionTag) ?? 1;
        if (compression is < 0 or > ushort.MaxValue)
        {
            throw new CorruptRawFileException("The raw image has an impossible compression code");
        }

        return new RawSubImage((int)w, (int)h, (int)bits, (int)compression, ReadPattern(reader, best), ReadStrips(reader, best, fileLength));
    }

    private static CfaPattern ReadPattern(TiffReader reader, Ifd raw)
    {
        if (!raw.TryGet(CfaDimensionTag, out IfdEntry dimension)
            || !reader.TryReadInteger(dimension, 0, out long columns)
            || !reader.TryReadInteger(dimension, 1, out long rows)
            || columns != 2
            || rows != 2
            || !raw.TryGet(CfaPatternTag, out IfdEntry pattern)
            || pattern.Count != 4)
        {
            return CfaPattern.Unknown;
        }

        Span<byte> colours = stackalloc byte[4];
        if (!reader.TryReadBytes(pattern, colours))
        {
            return CfaPattern.Unknown;
        }

        // The colours are 0 red, 1 green, 2 blue.
        return (colours[0], colours[1], colours[2], colours[3]) switch
        {
            (0, 1, 1, 2) => CfaPattern.Rggb,
            (1, 0, 2, 1) => CfaPattern.Grbg,
            (1, 2, 0, 1) => CfaPattern.Gbrg,
            (2, 1, 1, 0) => CfaPattern.Bggr,
            _ => CfaPattern.Unknown,
        };
    }

    // A preview is a JPEG stream whose position and length a directory gives; offsets count from the origin of the reader. One that does not fit in the file is left out.
    private static void TryAddPreview(TiffReader reader, Ifd directory, long origin, long fileLength, List<ByteRange> previews)
    {
        if (directory.TryGet(JpegOffsetTag, out IfdEntry offsetEntry)
            && directory.TryGet(JpegLengthTag, out IfdEntry lengthEntry)
            && reader.TryReadInteger(offsetEntry, 0, out long offset)
            && reader.TryReadInteger(lengthEntry, 0, out long length)
            && offset >= 0
            && length is >= 2 and <= MaxPreviewBytes
            && origin + offset <= fileLength - length)
        {
            previews.Add(new ByteRange(origin + offset, length));
        }
    }

    private static void ReadMakerNotePreview(TiffReader noteReader, long directoryOffset, long fileLength, List<ByteRange> previews)
    {
        try
        {
            TryAddPreview(noteReader, noteReader.ReadIfd(noteReader.Origin + directoryOffset), noteReader.Origin, fileLength, previews);
        }
        catch (CorruptRawFileException)
        {
            // The note is optional: its preview is lost, not the file.
        }
    }

    private static ByteRange[] ReadStrips(TiffReader reader, Ifd raw, long fileLength)
    {
        if (!raw.TryGet(StripOffsetsTag, out IfdEntry offsets) || !raw.TryGet(StripByteCountsTag, out IfdEntry counts))
        {
            throw new CorruptRawFileException("The raw image does not say where its data is");
        }

        if (offsets.Count == 0 || offsets.Count > MaxStrips || offsets.Count != counts.Count)
        {
            throw new CorruptRawFileException("The raw image has an impossible number of strips");
        }

        var strips = new ByteRange[offsets.Count];
        for (int i = 0; i < strips.Length; i++)
        {
            if (!reader.TryReadInteger(offsets, i, out long offset) || !reader.TryReadInteger(counts, i, out long length))
            {
                throw new CorruptRawFileException("The strips of the raw image cannot be read");
            }

            if (offset < 0 || length < 0 || offset > fileLength || length > fileLength - offset)
            {
                throw new CorruptRawFileException("The raw data extends beyond the end of the file (the file is truncated)", offset);
            }

            strips[i] = new ByteRange(offset, length);
        }

        return strips;
    }

    private static (TiffReader Reader, Ifd Directory)? ReadMakerNote(TiffReader reader, Ifd exif)
    {
        if (!exif.TryGet(MakerNoteTag, out IfdEntry entry) || entry.Count < 18)
        {
            return null;
        }

        Span<byte> prefix = stackalloc byte[6];
        reader.Source.ReadExactly(entry.ValueOffset, prefix);
        if (!prefix.SequenceEqual("Nikon\0"u8)
            || !TiffReader.TryReadHeader(reader.Source, entry.ValueOffset + 10, out TiffReader noteReader, out long firstDirectory))
        {
            return null;
        }

        // The note is optional: a damaged one costs the values it holds, not the file.
        try
        {
            return (noteReader, noteReader.ReadIfd(firstDirectory));
        }
        catch (CorruptRawFileException)
        {
            return null;
        }
    }

    private static RawRectangle ReadActiveArea((TiffReader Reader, Ifd Directory)? note, int width, int height)
    {
        var whole = new RawRectangle(0, 0, width, height);
        if (note is not { } n || !n.Directory.TryGet(NikonActiveAreaTag, out IfdEntry entry) || entry.Count != 4)
        {
            return whole;
        }

        long[] v = new long[4];
        for (int i = 0; i < 4; i++)
        {
            if (!n.Reader.TryReadInteger(entry, i, out v[i]))
            {
                return whole;
            }
        }

        bool fits = v[0] >= 0 && v[1] >= 0 && v[2] > 0 && v[3] > 0 && v[0] + v[2] <= width && v[1] + v[3] <= height;
        return fits ? new RawRectangle((int)v[0], (int)v[1], (int)v[2], (int)v[3]) : whole;
    }

    // Four values, one per photosite of the 2 x 2 pattern. Only asked for the layout that was checked on camera files (a red, green, green, blue pattern and 14 bit samples): for another pattern the order of the values (by colour or by position) and for another depth their scale are not established; a missing, short or implausible record is no black level.
    private static RawBlackLevel? ReadBlackLevel((TiffReader Reader, Ifd Directory)? note, int bits)
    {
        if (note is not { } n || !n.Directory.TryGet(NikonBlackLevelTag, out IfdEntry entry) || entry.Count != 4)
        {
            return null;
        }

        Span<int> v = stackalloc int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!n.Reader.TryReadInteger(entry, i, out long value) || value < 0 || value >= (1L << bits))
            {
                return null;
            }

            v[i] = (int)value;
        }

        return new RawBlackLevel(v[0], v[1], v[2], v[3]);
    }

    private static long? Integer(TiffReader reader, Ifd directory, ushort tag) =>
        directory.TryGet(tag, out IfdEntry entry) && reader.TryReadInteger(entry, 0, out long value) ? value : null;

    private static string? Text(TiffReader reader, Ifd? directory, ushort tag)
    {
        if (directory is null || !directory.TryGet(tag, out IfdEntry entry))
        {
            return null;
        }

        string? text = reader.ReadText(entry);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static double? Rational(TiffReader reader, Ifd? directory, ushort tag) =>
        directory is not null && directory.TryGet(tag, out IfdEntry entry) && reader.TryReadRational(entry, 0, out double value) && double.IsFinite(value) && value > 0 ? value : null;

    private static DateTime? ParseDateTime(string? text)
    {
        if (text is null || text.Length < 19)
        {
            return null;
        }

        return DateTime.TryParseExact(text.AsSpan(0, 19), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value) ? value : null;
    }

    private static TimeSpan? ParseOffset(string? text)
    {
        // "+01:00" or "-05:30".
        if (text is not { Length: 6 } || (text[0] != '+' && text[0] != '-') || text[3] != ':'
            || !int.TryParse(text.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
            || !int.TryParse(text.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || hours > 14 || minutes > 59)
        {
            return null;
        }

        var offset = new TimeSpan(hours, minutes, 0);
        return text[0] == '-' ? -offset : offset;
    }
}
