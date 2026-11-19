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
    private int _disposed;

    private RawFile(ByteSource source, NefStructure structure)
    {
        _source = source;
        _structure = structure;
        var previews = new RawPreview[structure.Previews.Count];
        for (int i = 0; i < previews.Length; i++)
        {
            previews[i] = new RawPreview(i, structure.Previews[i].Offset, (int)structure.Previews[i].Length);
        }

        Previews = Array.AsReadOnly(previews);
    }

    /// <summary>What the file says about the shot and about its raw image.</summary>
    public RawMetadata Metadata => _structure.Metadata;

    /// <summary>The JPEG previews the library finds in the file (in its sub-directories and in the Nikon MakerNote) that lie entirely inside it, from the largest to the smallest in bytes; empty if there is none.</summary>
    public IReadOnlyList<RawPreview> Previews { get; }

    /// <summary>Reads the largest JPEG preview of the file.</summary>
    /// <returns>The bytes of the JPEG stream, or null if the file holds no preview.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    /// <exception cref="CorruptRawFileException">The data at the place of the preview does not start with a JPEG start-of-image marker.</exception>
    public byte[]? ReadPreviewJpeg() => Previews.Count == 0 ? null : ReadPreviewJpeg(Previews[0]);

    /// <summary>Reads one JPEG preview of the file.</summary>
    /// <param name="preview">A preview of <see cref="Previews"/>.</param>
    /// <returns>The bytes of the JPEG stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="preview"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="preview"/> does not belong to this file.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    /// <exception cref="CorruptRawFileException">The data at the place of the preview does not start with a JPEG start-of-image marker.</exception>
    public byte[] ReadPreviewJpeg(RawPreview preview)
    {
        CheckPreview(preview);
        byte[] bytes = GC.AllocateUninitializedArray<byte>(preview.ByteCount);
        ReadPreview(preview, bytes);
        return bytes;
    }

    /// <summary>Reads one JPEG preview of the file into a buffer of the caller, without allocating (a preview of several megabytes would otherwise be allocated on the large object heap); size the buffer with <see cref="RawPreview.ByteCount"/>.</summary>
    /// <param name="preview">A preview of <see cref="Previews"/>.</param>
    /// <param name="destination">The buffer; it must hold at least <see cref="RawPreview.ByteCount"/> bytes.</param>
    /// <returns>The number of bytes written, which is <see cref="RawPreview.ByteCount"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="preview"/> does not belong to this file, or <paramref name="destination"/> is too small.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    /// <exception cref="CorruptRawFileException">The data at the place of the preview does not start with a JPEG start-of-image marker.</exception>
    public int ReadPreviewJpeg(RawPreview preview, Span<byte> destination)
    {
        CheckPreview(preview);
        if (destination.Length < preview.ByteCount)
        {
            throw new ArgumentException($"The buffer holds {destination.Length} bytes and the preview has {preview.ByteCount}.", nameof(destination));
        }

        ReadPreview(preview, destination[..preview.ByteCount]);
        return preview.ByteCount;
    }

    /// <summary>Decodes the raw image into a new array.</summary>
    /// <param name="cancellationToken">Cancels the decoding.</param>
    /// <returns>The mosaic and what describes it.</returns>
    /// <exception cref="UnsupportedCompressionException">The samples use a compression the library does not decode yet; only uncompressed samples are decoded for now.</exception>
    /// <exception cref="UnsupportedFormatException">The samples have a size the library does not decode (12 and 14 bits are).</exception>
    /// <exception cref="CorruptRawFileException">The strips of the raw image are too short for the image.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    /// <exception cref="OperationCanceledException">The decoding was cancelled.</exception>
    /// <remarks>The array of a full-frame image is tens of megabytes; to decode many images, use <see cref="Decode(Span{ushort}, CancellationToken)"/> with a buffer you reuse.</remarks>
    public RawImage Decode(CancellationToken cancellationToken = default)
    {
        PrepareDecode();
        RawMetadata metadata = Metadata;
        ushort[] mosaic = GC.AllocateUninitializedArray<ushort>(checked(metadata.ImageWidth * metadata.ImageHeight));
        UncompressedDecoder.Decode(_source, _structure, mosaic, cancellationToken);
        return new RawImage(metadata.ImageWidth, metadata.ImageHeight, metadata.ActiveArea, metadata.CfaPattern, metadata.BitsPerSample, mosaic);
    }

    /// <summary>Decodes the raw image into a buffer of the caller, row after row, without allocating the mosaic.</summary>
    /// <param name="destination">The buffer; it must hold at least <see cref="RawMetadata.ImageWidth"/> times <see cref="RawMetadata.ImageHeight"/> samples.</param>
    /// <param name="cancellationToken">Cancels the decoding.</param>
    /// <returns>The number of samples written.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    /// <exception cref="UnsupportedCompressionException">The samples use a compression the library does not decode yet; only uncompressed samples are decoded for now.</exception>
    /// <exception cref="UnsupportedFormatException">The samples have a size the library does not decode (12 and 14 bits are).</exception>
    /// <exception cref="CorruptRawFileException">The strips of the raw image are too short for the image.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    /// <exception cref="OperationCanceledException">The decoding was cancelled.</exception>
    /// <remarks>When the decoding fails, <paramref name="destination"/> may be partly written. Disposing the file during a decoding is not supported.</remarks>
    public int Decode(Span<ushort> destination, CancellationToken cancellationToken = default)
    {
        PrepareDecode();
        RawMetadata metadata = Metadata;
        int count = checked(metadata.ImageWidth * metadata.ImageHeight);
        if (destination.Length < count)
        {
            throw new ArgumentException($"The buffer holds {destination.Length} samples and the image has {count}.", nameof(destination));
        }

        UncompressedDecoder.Decode(_source, _structure, destination[..count], cancellationToken);
        return count;
    }

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
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _source.Dispose();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    // Everything that can be refused before a byte of the image is read or allocated.
    private void PrepareDecode()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        RawMetadata metadata = Metadata;
        if (metadata.Compression != RawCompression.Uncompressed)
        {
            throw new UnsupportedCompressionException(metadata.CompressionCode, metadata.Compression == RawCompression.NikonCompressed ? "Nikon compressed" : null);
        }

        if (!UncompressedDecoder.IsSupported(metadata.BitsPerSample))
        {
            throw new UnsupportedFormatException($"Uncompressed samples of {metadata.BitsPerSample} bits are not decoded (12 and 14 bits are).");
        }

        UncompressedDecoder.CheckStrips(_structure);
    }

    private void CheckPreview(RawPreview preview)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(preview);
        if (preview.Index >= Previews.Count || !ReferenceEquals(Previews[preview.Index], preview))
        {
            throw new ArgumentException("The preview does not belong to this file.", nameof(preview));
        }
    }

    private void ReadPreview(RawPreview preview, Span<byte> destination)
    {
        try
        {
            _source.ReadExactly(preview.Offset, destination);
        }
        catch (EndOfStreamException ex)
        {
            throw new CorruptRawFileException("The file ends inside a preview", preview.Offset, ex);
        }

        if (destination[0] != 0xFF || destination[1] != 0xD8)
        {
            throw new CorruptRawFileException("The preview is not a JPEG stream", preview.Offset);
        }
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
