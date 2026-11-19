namespace Eleris.RawNet;

/// <summary>How the pixels of the raw image are stored in the file.</summary>
public enum RawCompression
{
    /// <summary>A compression the library does not know; see <see cref="RawMetadata.CompressionCode"/>.</summary>
    Unknown = 0,

    /// <summary>The samples are stored as they are.</summary>
    Uncompressed = 1,

    /// <summary>The compression of Nikon NEF files (TIFF compression code 34713).</summary>
    NikonCompressed = 34713,
}
