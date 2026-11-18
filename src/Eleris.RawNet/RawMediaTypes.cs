namespace Eleris.RawNet;

/// <summary>The media types of the formats the library reads. A NEF is TIFF based but is not an <c>image/tiff</c>.</summary>
public static class RawMediaTypes
{
    /// <summary>The media type of a Nikon NEF file.</summary>
    public const string NikonNef = "image/x-nikon-nef";
}
