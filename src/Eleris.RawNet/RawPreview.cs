namespace Eleris.RawNet;

/// <summary>A JPEG preview stored in a raw file. Get them from <see cref="RawFile.Previews"/>.</summary>
public sealed class RawPreview
{
    internal RawPreview(int index, long offset, int byteCount)
    {
        Index = index;
        Offset = offset;
        ByteCount = byteCount;
    }

    /// <summary>The place of the preview in <see cref="RawFile.Previews"/>, which lists them from the largest to the smallest.</summary>
    public int Index { get; }

    /// <summary>The size in bytes of the JPEG stream (at most 64 MiB).</summary>
    public int ByteCount { get; }

    internal long Offset { get; }
}
