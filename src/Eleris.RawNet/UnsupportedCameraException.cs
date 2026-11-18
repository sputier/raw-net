namespace Eleris.RawNet;

/// <summary>The file comes from a camera body the library does not support.</summary>
public class UnsupportedCameraException : RawException
{
    private const int MaxNameLength = 64;

    /// <summary>Creates an exception for a camera.</summary>
    /// <param name="make">The manufacturer written in the file, or null if the file has none. It is untrusted text.</param>
    /// <param name="model">The camera model written in the file, or null if the file has none. It is untrusted text.</param>
    public UnsupportedCameraException(string? make, string? model)
        : base(Describe(make, model))
    {
        Make = make;
        Model = model;
    }

    /// <summary>The manufacturer exactly as written in the file, or null. It is untrusted text.</summary>
    public string? Make { get; }

    /// <summary>The camera model exactly as written in the file, or null. It is untrusted text.</summary>
    public string? Model { get; }

    // The names come from the file: the message keeps them short and free of control characters (log injection).
    private static string Describe(string? make, string? model)
    {
        string name = Clean($"{make} {model}");
        return name.Length == 0 ? "The camera is not supported." : $"The camera '{name}' is not supported.";
    }

    private static string Clean(string text)
    {
        char[] chars = [.. text.Take(MaxNameLength).Select(c => char.IsControl(c) ? ' ' : c)];
        return new string(chars).Trim();
    }
}
