using System.Buffers.Binary;
using System.Text;

namespace Eleris.RawNet.Tests.Synthetic;

/// <summary>One directory entry as stored in a little-endian TIFF.</summary>
internal readonly record struct TinyEntry(int Tag, int Type, int Count, long ValueOffset);

/// <summary>
/// A few lines to read back the files of <see cref="SyntheticNef"/>, independent of the library, so that the builder is checked
/// by something other than itself.
/// </summary>
internal static class TinyTiff
{
    public static List<TinyEntry> ReadIfd(byte[] file, long offset, long origin = 0)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan((int)offset));
        List<TinyEntry> entries = [];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> slot = file.AsSpan((int)offset + 2 + (12 * i), 12);
            int type = BinaryPrimitives.ReadUInt16LittleEndian(slot[2..]);
            int n = (int)BinaryPrimitives.ReadUInt32LittleEndian(slot[4..]);
            long valueOffset = n * TypeSize(type) <= 4
                ? offset + 2 + (12 * i) + 8
                : origin + BinaryPrimitives.ReadUInt32LittleEndian(slot[8..]);
            entries.Add(new TinyEntry(BinaryPrimitives.ReadUInt16LittleEndian(slot), type, n, valueOffset));
        }

        return entries;
    }

    public static TinyEntry Find(List<TinyEntry> entries, int tag) => entries.Single(e => e.Tag == tag);

    public static ushort[] Shorts(byte[] file, TinyEntry e) =>
        [.. Enumerable.Range(0, e.Count).Select(i => BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan((int)e.ValueOffset + (i * 2))))];

    public static uint[] Longs(byte[] file, TinyEntry e) =>
        [.. Enumerable.Range(0, e.Count).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)e.ValueOffset + (i * 4))))];

    public static string Text(byte[] file, TinyEntry e) => Encoding.ASCII.GetString(file, (int)e.ValueOffset, e.Count - 1);

    public static (uint, uint)[] Rationals(byte[] file, TinyEntry e) =>
        [.. Enumerable.Range(0, e.Count).Select(i => (
            BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)e.ValueOffset + (i * 8))),
            BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)e.ValueOffset + (i * 8) + 4))))];

    public static int TypeSize(int type) => type switch
    {
        1 or 2 or 7 => 1,
        3 => 2,
        4 => 4,
        5 => 8,
        _ => throw new NotSupportedException($"type {type}"),
    };
}
