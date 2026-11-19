namespace Eleris.RawNet.Internal;

/// <summary>The strips of the raw image read as one continuous run of bytes.</summary>
internal sealed class StripStream(ByteSource source, IReadOnlyList<ByteRange> strips)
{
    private int _strip;
    private long _used;

    /// <summary>Reads up to <c>destination.Length</c> bytes; returns 0 only at the end of the last strip.</summary>
    public int Read(Span<byte> destination)
    {
        while (_strip < strips.Count)
        {
            ByteRange range = strips[_strip];
            long left = range.Length - _used;
            if (left == 0)
            {
                _strip++;
                _used = 0;
                continue;
            }

            int count = (int)Math.Min(left, destination.Length);
            source.ReadExactly(range.Offset + _used, destination[..count]);
            _used += count;
            return count;
        }

        return 0;
    }

    /// <summary>Fills <paramref name="destination"/> or throws <see cref="EndOfStreamException"/>.</summary>
    public void ReadExactly(Span<byte> destination)
    {
        while (destination.Length > 0)
        {
            int read = Read(destination);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            destination = destination[read..];
        }
    }
}
