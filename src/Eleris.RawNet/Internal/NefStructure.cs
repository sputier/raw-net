namespace Eleris.RawNet.Internal;

/// <summary>Where a piece of data (a strip of the raw image, a preview) is stored in the file.</summary>
/// <param name="Offset">The position of its first byte.</param>
/// <param name="Length">The number of bytes.</param>
internal readonly record struct ByteRange(long Offset, long Length);

/// <summary>
/// The start of the Nikon "linearization" record of the MakerNote (tag 0x96), which tells how the raw image is compressed: two
/// version bytes, then the four starting values of the predictors.
/// </summary>
internal readonly record struct NikonCompressionHeader(byte Version0, byte Version1, ushort Predictor0, ushort Predictor1, ushort Predictor2, ushort Predictor3)
{
    /// <summary>The first version byte of the lossless variant.</summary>
    public const byte LosslessVersion = 0x46;

    public bool IsLossless => Version0 == LosslessVersion;
}

/// <summary>What the structure of a NEF file says: the metadata, and where the raw image is stored.</summary>
/// <param name="Metadata">The metadata of the file.</param>
/// <param name="RawStrips">The strips of the raw image, in order.</param>
/// <param name="Previews">The JPEG previews that lie inside the file, largest first.</param>
/// <param name="CompressionHeader">The Nikon compression record of the MakerNote, or null if the file has none.</param>
internal sealed record NefStructure(RawMetadata Metadata, IReadOnlyList<ByteRange> RawStrips, IReadOnlyList<ByteRange> Previews, NikonCompressionHeader? CompressionHeader = null);

/// <summary>
/// Reads the structure of a NEF file. It is a seam: the implementation of the library reads the TIFF directories itself, and
/// another one could rely on a TIFF library without any change to the rest.
/// </summary>
internal interface INefStructureReader
{
    /// <summary>Reads the structure, or fails with a <see cref="RawException"/>.</summary>
    NefStructure Read(ByteSource source);
}
