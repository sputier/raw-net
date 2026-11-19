using Eleris.RawNet.Internal;

namespace Eleris.RawNet.Tests;

public class LosslessRealFileTests
{
    [SkippableFact]
    public void Every_real_file_decodes_to_samples_in_range_and_consumes_its_strip()
    {
        string? directory = Environment.GetEnvironmentVariable("RAWNET_SAMPLES_DIR");
        Skip.If(string.IsNullOrEmpty(directory) || !Directory.Exists(directory), "RAWNET_SAMPLES_DIR is not set to a folder.");
        int limit = int.Parse(Environment.GetEnvironmentVariable("RAWNET_SAMPLES_LIMIT") ?? "10", System.Globalization.CultureInfo.InvariantCulture);
        string[] files = [.. Directory.EnumerateFiles(directory!, "*.NEF").Order(StringComparer.Ordinal).Take(limit)];
        Skip.If(files.Length == 0, "No .NEF file in RAWNET_SAMPLES_DIR.");

        ushort[] mosaic = [];
        int decoded = 0;
        foreach (string path in files)
        {
            using RawFile raw = RawFile.Open(path);
            if (raw.Metadata.Compression != RawCompression.NikonCompressed)
            {
                continue;
            }

            RawMetadata metadata = raw.Metadata;
            int count = metadata.ImageWidth * metadata.ImageHeight;
            if (mosaic.Length < count)
            {
                mosaic = new ushort[count];
            }

            Assert.Equal(count, raw.Decode(mosaic));
            Assert.InRange(mosaic.AsSpan(0, count).ToArray().Max(), 0, (1 << metadata.BitsPerSample) - 1);

            // The stream must end where the strip ends.
            using ByteSource source = ByteSource.FromHandle(File.OpenHandle(path));
            NefStructure structure = new NefStructureReader().Read(source);
            long used = NikonLosslessDecoder.Decode(source, structure, structure.CompressionHeader!.Value, mosaic, CancellationToken.None);
            Assert.InRange(structure.RawStrips.Sum(s => s.Length) - used, 0, 8);
            decoded++;
        }

        Skip.If(decoded == 0, "No Nikon compressed file in the folder.");
    }
}
