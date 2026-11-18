using System.Globalization;

namespace Eleris.RawNet;

/// <summary>The file is damaged, truncated or inconsistent.</summary>
public class CorruptRawFileException : RawException
{
    /// <summary>Creates an exception for a damaged file.</summary>
    /// <param name="message">What is wrong with the file, without a final period.</param>
    public CorruptRawFileException(string message)
        : this(message, null, null)
    {
    }

    /// <summary>Creates an exception for a damaged file, with the position of the problem.</summary>
    /// <param name="message">What is wrong with the file, without a final period.</param>
    /// <param name="position">The position in the file where the problem was found, or null if it has none.</param>
    public CorruptRawFileException(string message, long? position)
        : this(message, position, null)
    {
    }

    /// <summary>Creates an exception for a damaged file, with the position of the problem and its cause.</summary>
    /// <param name="message">What is wrong with the file, without a final period.</param>
    /// <param name="position">The position in the file where the problem was found, or null if it has none.</param>
    /// <param name="innerException">The cause, for example the end of the stream.</param>
    public CorruptRawFileException(string message, long? position, Exception? innerException)
        : base(Format(message, position), innerException)
    {
        Position = position;
    }

    /// <summary>The position in the file where the problem was found, or null.</summary>
    public long? Position { get; }

    private static string Format(string message, long? position) =>
        position is null ? message : string.Create(CultureInfo.InvariantCulture, $"{message} (at byte {position})");
}
