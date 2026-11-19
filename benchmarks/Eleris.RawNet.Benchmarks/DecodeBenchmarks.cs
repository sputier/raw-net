using BenchmarkDotNet.Attributes;

namespace Eleris.RawNet.Benchmarks;

/// <summary>
/// Decoding the raw image of the first file of the folder named by RAWNET_SAMPLES_DIR (read only) into a buffer that is reused.
/// Without the variable the benchmarks do nothing.
/// </summary>
[MemoryDiagnoser]
public class DecodeBenchmarks
{
    private RawFile? _file;
    private ushort[] _buffer = [];

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
            _buffer = new ushort[_file.Metadata.ImageWidth * _file.Metadata.ImageHeight];
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _file?.Dispose();

    [Benchmark]
    public int IntoBuffer() => _file is null ? 0 : _file.Decode(_buffer);
}
