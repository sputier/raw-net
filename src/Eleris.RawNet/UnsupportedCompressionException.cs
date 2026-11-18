using System.Globalization;

namespace Eleris.RawNet;

/// <summary>The pixels of the file use a compression the library does not decode.</summary>
public class UnsupportedCompressionException : RawException
{
    /// <summary>Creates an exception for a compression.</summary>
    /// <param name="code">The compression code stored in the file.</param>
    /// <param name="name">A readable name of the compression, or null if it has none.</param>
    public UnsupportedCompressionException(int code, string? name)
        : base(Format(code, name))
    {
        Code = code;
        Name = name;
    }

    /// <summary>The compression code stored in the file.</summary>
    public int Code { get; }

    /// <summary>A readable name of the compression, or null.</summary>
    public string? Name { get; }

    private static string Format(int code, string? name) => name is null
        ? string.Create(CultureInfo.InvariantCulture, $"The compression {code} is not supported.")
        : string.Create(CultureInfo.InvariantCulture, $"The compression {code} ({name}) is not supported.");
}
