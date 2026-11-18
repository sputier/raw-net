namespace Eleris.RawNet.Internal;

/// <summary>Where a piece of the raw image is stored in the file.</summary>
/// <param name="Offset">The position of its first byte.</param>
/// <param name="Length">The number of bytes.</param>
internal readonly record struct RawStrip(long Offset, long Length);

/// <summary>What the structure of a NEF file says: the metadata, and where the raw image is stored.</summary>
/// <param name="Metadata">The metadata of the file.</param>
/// <param name="RawStrips">The strips of the raw image, in order.</param>
internal sealed record NefStructure(RawMetadata Metadata, IReadOnlyList<RawStrip> RawStrips);

/// <summary>
/// Reads the structure of a NEF file. It is a seam: the implementation of the library reads the TIFF directories itself, and
/// another one could rely on a TIFF library without any change to the rest.
/// </summary>
internal interface INefStructureReader
{
    /// <summary>Reads the structure, or fails with a <see cref="RawException"/>.</summary>
    NefStructure Read(ByteSource source);
}
