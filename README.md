# Eleris.RawNet

A pure managed .NET library to read the raw files of digital cameras. The first supported format is the **Nikon NEF** of the
Z 6: metadata without decoding any pixel, the embedded JPEG preview, and the raw sensor mosaic with what is needed to process
it (colour filter pattern, active area, black and white levels, as-shot white balance). The sister library of
[Eleris.FitsNet](https://github.com/sputier/fits-net) and [Eleris.TiffNet](https://github.com/sputier/tiff-net).

**Status: pre-release, under construction.** Nothing is published yet and the API is not stable before 1.0. The roadmap is in
the [issues](https://github.com/sputier/raw-net/issues).

Targets: net10.0, no dependency, no native code, compatible with trimming and NativeAOT.

## Goals

- **Fast metadata**: capture date and time, exposure, ISO, camera, lens, image size and colour filter pattern, read without
  decoding the pixels.
- **The raw mosaic as integers**, unscaled, with the active area, the black and white levels and the white balance as shot.
- **The embedded preview**, cheaply.
- **Typed errors**: an unsupported camera, an unsupported compression or a damaged file fails with a `RawException`, never
  with an unrelated exception, and a hostile file cannot exhaust the memory of the application.
- **Speed**: allocation budgets and timings are tested, and measured with BenchmarkDotNet (in `benchmarks/`, never part of the
  package).

## Usage

```csharp
using Eleris.RawNet;

// Only the structure of the file is read, a few small reads: no pixel is decoded.
using RawFile file = RawFile.Open("DSC_0001.NEF");
RawMetadata m = file.Metadata;
Console.WriteLine($"{m.Model}, ISO {m.Iso}, {m.ExposureTimeSeconds} s, {m.ImageWidth} x {m.ImageHeight}, {m.CfaPattern}");
```

`RawFile.ReadMetadata(path)` does the same and closes the file. A file that is not a NEF, comes from another maker or is damaged
fails with a `RawException` (`UnsupportedFormatException`, `UnsupportedCameraException`, `CorruptRawFileException`...), and with
nothing else; an unreadable file fails with the usual `IOException`.

The embedded JPEG previews are read the same way, without decoding anything:

```csharp
byte[]? jpeg = file.ReadPreviewJpeg();                      // the largest preview, null if the file has none
if (file.Previews.Count > 0)
{
    byte[] buffer = new byte[file.Previews[0].ByteCount];     // or into your own buffer, without allocating
    int written = file.ReadPreviewJpeg(file.Previews[0], buffer);
}
```

The raw image is decoded to its mosaic of 16-bit samples, unscaled (uncompressed, and the lossless Nikon compression of 12 and 14 bit images):

```csharp
RawImage image = file.Decode();                              // image.Mosaic, Width, Height, ActiveArea, CfaPattern
ushort[] mine = new ushort[m.ImageWidth * m.ImageHeight];
file.Decode(mine);                                           // or into a buffer you reuse
```

What is implemented, and what is next, is tracked in the [issues](https://github.com/sputier/raw-net/issues): the metadata, the
previews and the mosaic work today; the levels and the white balance come next.

## Build and test

You need the .NET SDK pinned by [`global.json`](global.json).

```shell
dotnet build Eleris.RawNet.slnx -c Release
dotnet test Eleris.RawNet.slnx -c Release
dotnet format Eleris.RawNet.slnx --verify-no-changes     # the style of .editorconfig, checked in CI
```

The tests build their own small synthetic NEF files (`tests/Eleris.RawNet.Tests/Synthetic`): no camera file is stored in the
repository. Tests that read real files are optional and read-only; they run only when the environment variable
`RAWNET_SAMPLES_DIR` names a folder of files, and are reported as skipped otherwise.

Benchmarks:

```shell
dotnet run -c Release --project benchmarks/Eleris.RawNet.Benchmarks -- --filter "*"
```

## Contributing, security, licence

Bug reports and pull requests are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md) and the
[code of conduct](CODE_OF_CONDUCT.md). To report a vulnerability (a file that crashes the library, exhausts its memory or
blocks it), follow [SECURITY.md](SECURITY.md) and do not open a public issue. Eleris.RawNet is released under the
[MIT licence](LICENSE). The format knowledge comes from public documentation of TIFF, Exif and the Nikon format, and from
observation of files; no code of other raw decoders is copied.
