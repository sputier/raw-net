using Eleris.RawNet.Internal;

namespace Eleris.RawNet;

/// <summary>
/// An opened raw file. Opening reads only the structure of the file (a few small reads), never its pixels, so
/// <see cref="Metadata"/> is available at once. Dispose the file to release what it holds.
/// </summary>
/// <remarks>
/// A file opened from a path or from memory can be used by several threads. A file opened from a stream uses the position of
/// that stream and must not be used by several threads at once.
/// </remarks>
public sealed class RawFile : IDisposable, IAsyncDisposable
{
    // The seam of the structure reader, kept as the interface on purpose (a TIFF library could implement it).
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "A seam.")]
    private static readonly INefStructureReader StructureReader = new NefStructureReader();

    private readonly ByteSource _source;
    private readonly NefStructure _structure;

    private RawFile(ByteSource source, NefStructure structure)
    {
        _source = source;
        _structure = structure;
    }

    /// <summary>What the file says about the shot and about its raw image.</summary>
    public RawMetadata Metadata => _structure.Metadata;

    /// <summary>
    /// Opens a raw file from a path. The structure of the file is checked as a whole: a file cut inside its raw data fails with a
    /// <see cref="CorruptRawFileException"/> even though its metadata could be read.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The opened file.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or null.</exception>
    /// <exception cref="RawException">The file is not a raw file the library reads, or is damaged.</exception>
    /// <exception cref="IOException">The file cannot be read (the usual I/O and access exceptions of the file system propagate unchanged).</exception>
    public static RawFile Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Microsoft.Win32.SafeHandles.SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        ByteSource source;
        try
        {
            source = ByteSource.FromHandle(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        return Create(source);
    }

    /// <summary>Opens a raw file from a seekable stream.</summary>
    /// <param name="stream">A readable, seekable stream positioned anywhere; the library sets the position it needs.</param>
    /// <param name="leaveOpen">True to leave the stream open when the file is disposed.</param>
    /// <returns>The opened file.</returns>
    /// <exception cref="ArgumentException">The stream is null, cannot be read or cannot seek.</exception>
    /// <exception cref="RawException">The content is not a raw file the library reads, or is damaged.</exception>
    public static RawFile Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));
        }

        return Create(ByteSource.FromStream(stream, leaveOpen));
    }

    /// <summary>Opens a raw file held in memory. The memory must not change while the file is in use.</summary>
    /// <param name="data">The whole file.</param>
    /// <returns>The opened file.</returns>
    /// <exception cref="RawException">The content is not a raw file the library reads, or is damaged.</exception>
    public static RawFile Open(ReadOnlyMemory<byte> data) => Create(ByteSource.FromMemory(data));

    /// <summary>Reads the metadata of the file at <paramref name="path"/> and closes it.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The metadata.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or null.</exception>
    /// <exception cref="RawException">The file is not a raw file the library reads, or is damaged.</exception>
    /// <exception cref="IOException">The file cannot be read (the usual I/O and access exceptions of the file system propagate unchanged).</exception>
    public static RawMetadata ReadMetadata(string path)
    {
        using RawFile file = Open(path);
        return file.Metadata;
    }

    /// <summary>Releases the file or the stream this object holds.</summary>
    public void Dispose() => _source.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static RawFile Create(ByteSource source)
    {
        try
        {
            return new RawFile(source, StructureReader.Read(source));
        }
        catch (EndOfStreamException ex)
        {
            source.Dispose();
            throw new CorruptRawFileException("The file ends before the end of its structure", null, ex);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }
}
