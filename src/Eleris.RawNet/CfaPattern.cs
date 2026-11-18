namespace Eleris.RawNet;

/// <summary>
/// The 2 x 2 colour filter array of the sensor, read in row order from the top-left pixel of the raw image: the first two
/// letters are the first row, the last two the second row.
/// </summary>
public enum CfaPattern
{
    /// <summary>The file does not describe a pattern the library knows.</summary>
    Unknown = 0,

    /// <summary>Red, green / green, blue.</summary>
    Rggb,

    /// <summary>Green, red / blue, green.</summary>
    Grbg,

    /// <summary>Green, blue / red, green.</summary>
    Gbrg,

    /// <summary>Blue, green / green, red.</summary>
    Bggr,
}
