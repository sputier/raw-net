namespace Eleris.RawNet;

/// <summary>The file is not in a format the library reads.</summary>
public class UnsupportedFormatException : RawException
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public UnsupportedFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public UnsupportedFormatException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
