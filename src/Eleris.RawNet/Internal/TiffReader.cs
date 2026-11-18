using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Eleris.RawNet.Internal;

/// <summary>The type of a TIFF directory entry (the values the library reads).</summary>
internal static class TiffTypes
{
    public const ushort Byte = 1, Ascii = 2, Short = 3, Long = 4, Rational = 5, SignedByte = 6, Undefined = 7, SignedShort = 8, SignedLong = 9, SignedRational = 10, Float = 11, Double = 12, Ifd = 13;

    /// <summary>The size in bytes of one value, or 0 for a type the library does not know (the entry is then ignored).</summary>
    public static int Size(ushort type) => type switch
    {
        Byte or Ascii or SignedByte or Undefined => 1,
        Short or SignedShort => 2,
        Long or SignedLong or Float or Ifd => 4,
        Rational or SignedRational or Double => 8,
        _ => 0,
    };
}

/// <summary>An entry of a TIFF directory whose value lies inside the file.</summary>
/// <param name="Tag">The tag.</param>
/// <param name="Type">The type of the values.</param>
/// <param name="Count">The number of values.</param>
/// <param name="ValueOffset">The position in the file of the first byte of the values.</param>
internal readonly record struct IfdEntry(ushort Tag, ushort Type, uint Count, long ValueOffset)
{
    public long ByteLength => (long)Count * TiffTypes.Size(Type);
}

/// <summary>The entries of one directory, in file order.</summary>
internal sealed class Ifd(IfdEntry[] entries)
{
    public IfdEntry[] Entries { get; } = entries;

    public bool TryGet(ushort tag, out IfdEntry entry)
    {
        foreach (IfdEntry candidate in Entries)
        {
            if (candidate.Tag == tag)
            {
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }
}

/// <summary>
/// Reads TIFF directories and their values from a <see cref="ByteSource"/>. The offsets stored in the directories count from
/// <c>origin</c>: 0 for the file, the position of the embedded TIFF header for a Nikon MakerNote.
/// </summary>
internal sealed class TiffReader(ByteSource source, long origin, bool littleEndian)
{
    private const int MaxTextLength = 256;

    public ByteSource Source { get; } = source;

    public long Origin { get; } = origin;

    public bool LittleEndian { get; } = littleEndian;

    /// <summary>
    /// Reads the directory at <paramref name="offset"/>, a position in the file. The position is absolute, whereas the offsets of the values stored in the directory count from the origin of the reader. An entry whose value lies outside the file or
    /// whose type is unknown is left out, as TIFF readers do; a directory that does not fit in the file is an error.
    /// </summary>
    public Ifd ReadIfd(long offset)
    {
        long length = Source.Length;
        if (offset < 0 || offset > length - 2)
        {
            throw new CorruptRawFileException("A directory starts outside the file", offset);
        }

        Span<byte> countBytes = stackalloc byte[2];
        Source.ReadExactly(offset, countBytes);
        int count = ReadUInt16(countBytes);
        int byteCount = count * 12;
        if (offset + 2 + byteCount > length)
        {
            throw new CorruptRawFileException("A directory ends outside the file", offset);
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(byteCount, 1));
        try
        {
            Source.ReadExactly(offset + 2, buffer.AsSpan(0, byteCount));
            var entries = new List<IfdEntry>(count);
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> slot = buffer.AsSpan(i * 12, 12);
                ushort type = ReadUInt16(slot[2..]);
                int size = TiffTypes.Size(type);
                if (size == 0)
                {
                    continue;
                }

                uint valueCount = ReadUInt32(slot[4..]);
                long byteLength = (long)valueCount * size;
                long valueOffset = byteLength <= 4
                    ? offset + 2 + (i * 12L) + 8
                    : Origin + ReadUInt32(slot[8..]);
                if (valueOffset < 0 || valueOffset + byteLength > length)
                {
                    continue;
                }

                entries.Add(new IfdEntry(ReadUInt16(slot), type, valueCount, valueOffset));
            }

            return new Ifd([.. entries]);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Reads one integer value (BYTE, SHORT, LONG, IFD or their signed forms) of an entry.</summary>
    public bool TryReadInteger(in IfdEntry entry, int index, out long value)
    {
        value = 0;
        int size = TiffTypes.Size(entry.Type);
        if ((uint)index >= entry.Count || entry.Type is TiffTypes.Ascii or TiffTypes.Rational or TiffTypes.SignedRational or TiffTypes.Undefined or TiffTypes.Float or TiffTypes.Double)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[4];
        Source.ReadExactly(entry.ValueOffset + (index * (long)size), bytes[..size]);
        value = entry.Type switch
        {
            TiffTypes.Byte => bytes[0],
            TiffTypes.SignedByte => (sbyte)bytes[0],
            TiffTypes.Short => ReadUInt16(bytes),
            TiffTypes.SignedShort => (short)ReadUInt16(bytes),
            TiffTypes.SignedLong => (int)ReadUInt32(bytes),
            _ => ReadUInt32(bytes),
        };
        return true;
    }

    /// <summary>Reads one RATIONAL value as a number; false when the entry is not a rational or the denominator is zero.</summary>
    public bool TryReadRational(in IfdEntry entry, int index, out double value)
    {
        value = 0;
        if ((uint)index >= entry.Count || entry.Type is not (TiffTypes.Rational or TiffTypes.SignedRational))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[8];
        Source.ReadExactly(entry.ValueOffset + (index * 8L), bytes);
        double numerator, denominator;
        if (entry.Type == TiffTypes.Rational)
        {
            numerator = ReadUInt32(bytes);
            denominator = ReadUInt32(bytes[4..]);
        }
        else
        {
            numerator = (int)ReadUInt32(bytes);
            denominator = (int)ReadUInt32(bytes[4..]);
        }

        if (denominator == 0)
        {
            return false;
        }

        value = numerator / denominator;
        return true;
    }

    /// <summary>Reads an ASCII value (at most 256 characters, up to the first NUL), or null when the entry is not text.</summary>
    public string? ReadText(in IfdEntry entry)
    {
        if (entry.Type != TiffTypes.Ascii)
        {
            return null;
        }

        int length = (int)Math.Min(entry.Count, MaxTextLength);
        Span<byte> bytes = stackalloc byte[MaxTextLength];
        bytes = bytes[..length];
        Source.ReadExactly(entry.ValueOffset, bytes);
        int end = bytes.IndexOf((byte)0);
        if (end >= 0)
        {
            bytes = bytes[..end];
        }

        return Encoding.ASCII.GetString(bytes).Trim();
    }

    /// <summary>Reads the bytes of a value into <paramref name="destination"/>; false when it does not fit.</summary>
    public bool TryReadBytes(in IfdEntry entry, Span<byte> destination)
    {
        if (entry.ByteLength > destination.Length)
        {
            return false;
        }

        Source.ReadExactly(entry.ValueOffset, destination[..(int)entry.ByteLength]);
        return true;
    }

    public ushort ReadUInt16(ReadOnlySpan<byte> bytes) =>
        LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    public uint ReadUInt32(ReadOnlySpan<byte> bytes) =>
        LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);

    /// <summary>Reads the 8-byte TIFF header at <paramref name="position"/> and returns a reader and the offset of the first directory.</summary>
    public static bool TryReadHeader(ByteSource source, long position, out TiffReader reader, out long firstDirectory)
    {
        reader = null!;
        firstDirectory = 0;
        if (position < 0 || position + 8 > source.Length)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[8];
        source.ReadExactly(position, header);
        bool little = header[0] == (byte)'I' && header[1] == (byte)'I';
        bool big = header[0] == (byte)'M' && header[1] == (byte)'M';
        if (!little && !big)
        {
            return false;
        }

        reader = new TiffReader(source, position, little);
        if (reader.ReadUInt16(header[2..]) != 42)
        {
            return false;
        }

        firstDirectory = position + reader.ReadUInt32(header[4..]);
        return true;
    }
}
