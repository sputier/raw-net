namespace Eleris.RawNet;

/// <summary>
/// The base of every error the library reports for the content of a file it cannot read: a damaged, truncated, unsupported or
/// hostile file fails with a <see cref="RawException"/>. A wrong call fails with an <see cref="ArgumentException"/>, and the
/// errors of the underlying file or stream (<see cref="IOException"/>, cancellation...) propagate unchanged.
/// </summary>
public class RawException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public RawException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public RawException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
