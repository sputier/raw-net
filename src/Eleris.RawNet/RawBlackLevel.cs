namespace Eleris.RawNet;

/// <summary>
/// The black level the camera recorded: the value a sample takes in the dark, for each of the four photosites of the 2 x 2
/// colour filter pattern, in the order of the pattern read from its top-left photosite (row by row), which for a red, green,
/// green, blue pattern is red, green, green, blue. The values are in the units of the samples of the raw image.
/// </summary>
/// <param name="TopLeft">The level of the top-left photosite of the pattern.</param>
/// <param name="TopRight">The level of the top-right photosite.</param>
/// <param name="BottomLeft">The level of the bottom-left photosite.</param>
/// <param name="BottomRight">The level of the bottom-right photosite.</param>
public readonly record struct RawBlackLevel(int TopLeft, int TopRight, int BottomLeft, int BottomRight)
{
    /// <summary>The level of the photosite at a column and a row of the raw image, whose pattern starts at its top-left sample.</summary>
    /// <param name="column">The column of the sample.</param>
    /// <param name="row">The row of the sample.</param>
    /// <returns>The black level that applies to it.</returns>
    public int At(int column, int row) => ((column & 1) == 0, (row & 1) == 0) switch
    {
        (true, true) => TopLeft,
        (false, true) => TopRight,
        (true, false) => BottomLeft,
        _ => BottomRight,
    };
}
