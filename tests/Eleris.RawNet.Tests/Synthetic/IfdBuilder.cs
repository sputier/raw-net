using System.Buffers.Binary;
using System.Text;

namespace Eleris.RawNet.Tests.Synthetic;

/// <summary>Builds one little-endian TIFF directory, with the values that do not fit in the entry stored right after it.</summary>
internal sealed class IfdBuilder(bool bigEndian = false)
{
    private const int ByteType = 1, AsciiType = 2, ShortType = 3, LongType = 4, RationalType = 5, UndefinedType = 7;

    private readonly List<Entry> _entries = [];

    private sealed record Entry(int Tag, int Type, int Count, Func<byte[]> Value)
    {
        public int ByteLength => Count * TypeSize(Type);
    }

    /// <summary>The size of the directory with its out-of-line values; it does not depend on the values.</summary>
    public int Size => 2 + (12 * _entries.Count) + 4 + _entries.Sum(e => e.ByteLength > 4 ? Even(e.ByteLength) : 0);

    public IfdBuilder Bytes(int tag, params byte[] values) => Add(tag, ByteType, values.Length, () => values);

    public IfdBuilder Undefined(int tag, byte[] values) => Add(tag, UndefinedType, values.Length, () => values);

    public IfdBuilder Text(int tag, string value)
    {
        byte[] bytes = [.. Encoding.ASCII.GetBytes(value), 0];
        return Add(tag, AsciiType, bytes.Length, () => bytes);
    }

    public IfdBuilder Shorts(int tag, params ushort[] values) => Add(tag, ShortType, values.Length, () =>
    {
        byte[] bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            Put16(bytes.AsSpan(i * 2), values[i]);
        }

        return bytes;
    });

    public IfdBuilder Longs(int tag, params uint[] values) => LazyLongs(tag, values.Length, () => values);

    /// <summary>LONG values known only once the file is laid out (offsets): the count is fixed, the values are read late.</summary>
    public IfdBuilder LazyLongs(int tag, int count, Func<uint[]> values) => Add(tag, LongType, count, () =>
    {
        uint[] v = values();
        byte[] bytes = new byte[v.Length * 4];
        for (int i = 0; i < v.Length; i++)
        {
            Put32(bytes.AsSpan(i * 4), v[i]);
        }

        return bytes;
    });

    public IfdBuilder Rationals(int tag, params (uint Numerator, uint Denominator)[] values) => Add(tag, RationalType, values.Length, () =>
    {
        byte[] bytes = new byte[values.Length * 8];
        for (int i = 0; i < values.Length; i++)
        {
            Put32(bytes.AsSpan(i * 8), values[i].Numerator);
            Put32(bytes.AsSpan((i * 8) + 4), values[i].Denominator);
        }

        return bytes;
    });

    /// <summary>Writes the directory as if it started at <paramref name="offset"/> of the space its offsets refer to.</summary>
    public byte[] Serialize(long offset)
    {
        byte[] buffer = new byte[Size];
        Put16(buffer, (ushort)_entries.Count);
        int dataPosition = 2 + (12 * _entries.Count) + 4;
        int index = 0;
        foreach (Entry entry in _entries.OrderBy(e => e.Tag))
        {
            Span<byte> slot = buffer.AsSpan(2 + (12 * index++), 12);
            Put16(slot, (ushort)entry.Tag);
            Put16(slot[2..], (ushort)entry.Type);
            Put32(slot[4..], (uint)entry.Count);
            byte[] value = entry.Value();
            if (value.Length != entry.ByteLength)
            {
                throw new InvalidOperationException($"Tag {entry.Tag}: {value.Length} bytes instead of {entry.ByteLength}.");
            }

            if (value.Length <= 4)
            {
                value.CopyTo(slot[8..]);
            }
            else
            {
                Put32(slot[8..], (uint)(offset + dataPosition));
                value.CopyTo(buffer.AsSpan(dataPosition));
                dataPosition += Even(value.Length);
            }
        }

        return buffer;
    }

    private IfdBuilder Add(int tag, int type, int count, Func<byte[]> value)
    {
        if (_entries.Exists(e => e.Tag == tag))
        {
            throw new InvalidOperationException($"Tag {tag} is already in the directory.");
        }

        _entries.Add(new Entry(tag, type, count, value));
        return this;
    }

    private void Put16(Span<byte> destination, ushort value)
    {
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        }
    }

    private void Put32(Span<byte> destination, uint value)
    {
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        }
    }

    private static int Even(int length) => (length + 1) & ~1;

    private static int TypeSize(int type) => type switch
    {
        ByteType or AsciiType or UndefinedType => 1,
        ShortType => 2,
        LongType => 4,
        RationalType => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
