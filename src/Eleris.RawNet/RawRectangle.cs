namespace Eleris.RawNet;

/// <summary>A rectangle of pixels of the raw image: its top-left corner and its size.</summary>
/// <param name="Left">The column of the first pixel.</param>
/// <param name="Top">The row of the first pixel.</param>
/// <param name="Width">The number of columns.</param>
/// <param name="Height">The number of rows.</param>
public readonly record struct RawRectangle(int Left, int Top, int Width, int Height)
{
    /// <summary>The column after the last pixel.</summary>
    public int Right => Left + Width;

    /// <summary>The row after the last pixel.</summary>
    public int Bottom => Top + Height;
}
