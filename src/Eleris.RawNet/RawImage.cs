namespace Eleris.RawNet;

/// <summary>
/// The decoded raw image: the mosaic of samples the sensor recorded, one sample per photosite, before any demosaicing,
/// black-level subtraction or scaling.
/// </summary>
public sealed class RawImage
{
    internal RawImage(int width, int height, RawRectangle activeArea, CfaPattern cfaPattern, int bitsPerSample, RawBlackLevel? blackLevel, ushort[] mosaic)
    {
        Width = width;
        Height = height;
        ActiveArea = activeArea;
        CfaPattern = cfaPattern;
        BitsPerSample = bitsPerSample;
        BlackLevel = blackLevel;
        Mosaic = mosaic;
    }

    /// <summary>The width in pixels of the mosaic, margins included.</summary>
    public int Width { get; }

    /// <summary>The height in pixels of the mosaic, margins included.</summary>
    public int Height { get; }

    /// <summary>The part of the mosaic that holds the picture, as <see cref="RawMetadata.ActiveArea"/> says.</summary>
    public RawRectangle ActiveArea { get; }

    /// <summary>The colour filter pattern from the top-left sample of the mosaic.</summary>
    public CfaPattern CfaPattern { get; }

    /// <summary>The number of bits of a sample: every sample is smaller than <c>1 &lt;&lt; BitsPerSample</c>.</summary>
    public int BitsPerSample { get; }

    /// <summary>The black level the camera recorded, or null when the file holds none or the library does not read it (see <see cref="RawMetadata.BlackLevel"/>); it is not subtracted from the samples.</summary>
    public RawBlackLevel? BlackLevel { get; }

    /// <summary>The samples, row after row, <see cref="Width"/> times <see cref="Height"/> of them.</summary>
    public ReadOnlyMemory<ushort> Mosaic { get; }
}
