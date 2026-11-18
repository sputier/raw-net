using BenchmarkDotNet.Attributes;
using Eleris.RawNet.Tests.Synthetic;

namespace Eleris.RawNet.Benchmarks;

/// <summary>
/// Reading the metadata of a file: from memory (a synthetic file) and, when the environment variable RAWNET_SAMPLES_DIR names a
/// folder of NEF files, from the first file of that folder (read only).
/// </summary>
[MemoryDiagnoser]
public class MetadataBenchmarks
{
    private byte[] _synthetic = [];
    private string? _realFile;

    [GlobalSetup]
    public void Setup()
    {
        _synthetic = SyntheticNef.Build();
        string? directory = Environment.GetEnvironmentVariable("RAWNET_SAMPLES_DIR");
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            _realFile = Directory.EnumerateFiles(directory, "*.NEF").Order(StringComparer.Ordinal).FirstOrDefault();
        }
    }

    [Benchmark]
    public RawMetadata SyntheticInMemory()
    {
        using RawFile file = RawFile.Open(_synthetic);
        return file.Metadata;
    }

    [Benchmark]
    public RawMetadata? RealFileFromPath() => _realFile is null ? null : RawFile.ReadMetadata(_realFile);
}
