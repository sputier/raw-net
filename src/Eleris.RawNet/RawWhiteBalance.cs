namespace Eleris.RawNet;

/// <summary>Where the white balance of a <see cref="RawWhiteBalance"/> comes from.</summary>
public enum RawWhiteBalanceSource
{
    /// <summary>Not known; the value of a default instance.</summary>
    Unknown = 0,

    /// <summary>The camera recorded the multipliers in plain form in the Nikon MakerNote.</summary>
    NikonMakerNoteLevels = 1,
}

/// <summary>
/// The as-shot white balance: the multipliers the camera would apply to the samples of each channel of the raw mosaic to bring
/// them to a neutral grey, as it recorded them. They are not applied to the samples, and not normalised by the library (a camera
/// usually records them relative to green, so green is near 1). The channels are listed in the order of a red, green, green,
/// blue pattern.
/// </summary>
/// <param name="Red">The multiplier of the red photosites.</param>
/// <param name="Green1">The multiplier of the first green value of the record. The two green values are equal on every camera
/// file checked, so which green photosite each one belongs to is not established.</param>
/// <param name="Green2">The multiplier of the second green value of the record.</param>
/// <param name="Blue">The multiplier of the blue photosites.</param>
/// <param name="Source">Where the values were read.</param>
public readonly record struct RawWhiteBalance(double Red, double Green1, double Green2, double Blue, RawWhiteBalanceSource Source);
