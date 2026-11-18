using Microsoft.Win32.SafeHandles;

namespace Eleris.RawNet.Internal;

/// <summary>Random access to the bytes of a file, wherever they are: a handle, a seekable stream or memory.</summary>
internal abstract class ByteSource : IDisposable
{
    /// <summary>The length of the file in bytes.</summary>
    public abstract long Length { get; }

    /// <summary>Reads exactly <c>destination.Length</c> bytes at <paramref name="offset"/>; the range must lie inside the file.</summary>
    public abstract void ReadExactly(long offset, Span<byte> destination);

    public virtual void Dispose()
    {
    }

    public static ByteSource FromMemory(ReadOnlyMemory<byte> data) => new MemorySource(data);

    public static ByteSource FromHandle(SafeFileHandle handle) => new HandleSource(handle);

    public static ByteSource FromStream(Stream stream, bool leaveOpen) => new StreamSource(stream, leaveOpen);

    private sealed class MemorySource(ReadOnlyMemory<byte> data) : ByteSource
    {
        public override long Length => data.Length;

        public override void ReadExactly(long offset, Span<byte> destination) =>
            data.Span.Slice(checked((int)offset), destination.Length).CopyTo(destination);
    }

    private sealed class HandleSource : ByteSource
    {
        private readonly SafeFileHandle _handle;

        public HandleSource(SafeFileHandle handle)
        {
            _handle = handle;
            Length = RandomAccess.GetLength(handle);
        }

        public override long Length { get; }

        public override void ReadExactly(long offset, Span<byte> destination)
        {
            while (destination.Length > 0)
            {
                int read = RandomAccess.Read(_handle, destination, offset);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                destination = destination[read..];
                offset += read;
            }
        }

        public override void Dispose()
        {
            _handle.Dispose();
            base.Dispose();
        }
    }

    private sealed class StreamSource : ByteSource
    {
        private readonly Stream _stream;
        private readonly bool _leaveOpen;

        public StreamSource(Stream stream, bool leaveOpen)
        {
            _stream = stream;
            _leaveOpen = leaveOpen;
            Length = stream.Length;
        }

        public override long Length { get; }

        public override void ReadExactly(long offset, Span<byte> destination)
        {
            _stream.Position = offset;
            _stream.ReadExactly(destination);
        }

        public override void Dispose()
        {
            if (!_leaveOpen)
            {
                _stream.Dispose();
            }

            base.Dispose();
        }
    }
}
