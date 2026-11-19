namespace Eleris.RawNet;

/// <summary>
/// What a raw file says about the shot and about its raw image, read without decoding any pixel. A value the file does not
/// hold, or holds in a form the library cannot read, is null.
/// </summary>
public sealed record RawMetadata
{
    // The serial number identifies a body: ToString, which logs and debuggers call, leaves it out.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"Make = {Make}, Model = {Model}, Iso = {Iso}, ExposureTimeSeconds = {ExposureTimeSeconds}, ");
        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"ImageWidth = {ImageWidth}, ImageHeight = {ImageHeight}, CfaPattern = {CfaPattern}, BitsPerSample = {BitsPerSample}, ");
        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"Compression = {Compression}");
        return true;
    }

    /// <summary>The media type of the file, see <see cref="RawMediaTypes"/>.</summary>
    public required string MediaType { get; init; }

    /// <summary>The manufacturer as written in the file, for example "NIKON CORPORATION".</summary>
    public required string Make { get; init; }

    /// <summary>The camera model as written in the file, for example "NIKON Z 6".</summary>
    public required string Model { get; init; }

    /// <summary>The serial number of the camera body, or null. It identifies the body: it is not part of <see cref="object.ToString"/>.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>The firmware or software version written by the camera, or null.</summary>
    public string? Software { get; init; }

    /// <summary>The model of the lens, or null.</summary>
    public string? LensModel { get; init; }

    /// <summary>The focal length in millimetres, or null.</summary>
    public double? FocalLengthMillimetres { get; init; }

    /// <summary>The aperture as an f-number (1.8 for f/1.8), or null.</summary>
    public double? FNumber { get; init; }

    /// <summary>The exposure time in seconds, or null.</summary>
    public double? ExposureTimeSeconds { get; init; }

    /// <summary>The ISO speed, or null.</summary>
    public int? Iso { get; init; }

    /// <summary>
    /// The date and time of the shot as the camera clock showed them (its <see cref="DateTime.Kind"/> is
    /// <see cref="DateTimeKind.Unspecified"/>), or null. Use <see cref="CaptureUtcOffset"/> to place it on the UTC timeline.
    /// </summary>
    public DateTime? CaptureDateTime { get; init; }

    /// <summary>The offset of the camera clock from UTC when the file records it, or null when it is not known.</summary>
    public TimeSpan? CaptureUtcOffset { get; init; }

    /// <summary>The number of times the shutter had been released when the shot was taken, or null.</summary>
    public long? ShutterCount { get; init; }

    /// <summary>The width in pixels of the raw image, margins included.</summary>
    public required int ImageWidth { get; init; }

    /// <summary>The height in pixels of the raw image, margins included.</summary>
    public required int ImageHeight { get; init; }

    /// <summary>
    /// The crop area the camera recorded in the file, inside the raw image: for a Nikon Z 6 in full-frame mode, the part of the
    /// raw image that holds the picture, the rest being the margins of the sensor. When the file records none, or an area that does
    /// not fit in the image, it is the whole image.
    /// </summary>
    public required RawRectangle ActiveArea { get; init; }

    /// <summary>The black level the camera recorded for the four photosites of the colour filter pattern, or null when the file holds none, or holds one that does not fit the samples of the image. It is only read for a red, green, green, blue pattern and 14 bit samples, the layout checked on camera files: for any other the order of the four values and their scale are not established.</summary>
    public RawBlackLevel? BlackLevel { get; init; }

    /// <summary>The as-shot white balance: the multipliers the camera would apply to the samples of each channel (read from the plain record of the Nikon MakerNote, the record holds them in the order red, blue, green, green), or null when the file holds none or one that is not plausible. The order of the red and blue values of the record is the documented one; it cannot be checked on near-neutral files. The samples are not changed by it.</summary>
    public RawWhiteBalance? WhiteBalance { get; init; }

    /// <summary>The colour filter pattern from the top-left pixel of the raw image (not of the active area).</summary>
    public required CfaPattern CfaPattern { get; init; }

    /// <summary>The number of bits of a sample of the raw image.</summary>
    public required int BitsPerSample { get; init; }

    /// <summary>How the samples are stored.</summary>
    public required RawCompression Compression { get; init; }

    /// <summary>The TIFF compression code stored in the file, whether or not the library decodes it.</summary>
    public required int CompressionCode { get; init; }
}
