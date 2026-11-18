using BenchmarkDotNet.Attributes;

namespace Eleris.RawNet.Benchmarks;

/// <summary>
/// Reading the largest JPEG preview of the first file of the folder named by RAWNET_SAMPLES_DIR (read only), with a new array
/// and into a buffer of the caller. Without the variable the benchmarks do nothing.
/// </summary>
[MemoryDiagnoser]
public class PreviewBenchmarks
{
    private RawFile? _file;
    private byte[] _buffer = [];

    [GlobalSetup]
    public void Setup()
    {
        string? directory = Environment.GetEnvironmentVariable("RAWNET_SAMPLES_DIR");
        string? path = !string.IsNullOrEmpty(directory) && Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.NEF").Order(StringComparer.Ordinal).FirstOrDefault()
            : null;
        if (path is not null)
        {
            _file = RawFile.Open(path);
            _buffer = new byte[_file.Previews.Count > 0 ? _file.Previews[0].ByteCount : 0];
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _file?.Dispose();

    [Benchmark]
    public byte[]? NewArray() => _file?.ReadPreviewJpeg();

    [Benchmark]
    public int IntoBuffer() => _file is null || _file.Previews.Count == 0 ? 0 : _file.ReadPreviewJpeg(_file.Previews[0], _buffer);
}
