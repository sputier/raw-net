# Changelog

All notable changes are listed here. The project follows [Semantic Versioning](https://semver.org); before 1.0 the API may
change between minor versions.

## Unreleased

## 0.1.0

The first release.

### Added

- Repository foundations: licence, documentation, code of conduct, security policy, issue and pull request templates,
  continuous integration, analyzers (warnings are errors), central package management.
- The `RawException` hierarchy: `UnsupportedFormatException`, `UnsupportedCameraException`,
  `UnsupportedCompressionException` and `CorruptRawFileException`.
- Test infrastructure: a builder of small synthetic NEF files, and a benchmarks project (not packaged).
- Reading the metadata of a NEF without decoding any pixel: `RawFile.Open` (from a path, a seekable stream or memory) and
  `RawFile.ReadMetadata`, giving a `RawMetadata` (camera, serial number, lens, exposure, ISO, capture date and clock offset,
  shutter count, image size, active area, colour filter pattern, bit depth, compression), the media type `image/x-nikon-nef`
  (`RawMediaTypes.NikonNef`), and typed errors for a file that is not a NEF, comes from another maker, or is damaged or
  truncated. A damaged file fails with a `RawException` and nothing else (checked by truncating and corrupting files).
- The embedded JPEG previews: `RawFile.Previews` (largest first), `ReadPreviewJpeg()` for the largest, `ReadPreviewJpeg(preview)`
  and a variant that fills a buffer of the caller without allocating. They are found in the sub-directories and in the Nikon
  MakerNote; one that does not lie inside the file is left out, and data that does not start with a JPEG start-of-image marker is reported as corrupt.
- Decoding of an uncompressed raw image (12 or 14 bits per sample, packed most significant bit first, each row on a byte
  boundary): `RawFile.Decode()` gives a `RawImage` (the mosaic of `ushort`, its size, active area, colour filter pattern and bit
  depth), and `Decode(Span<ushort>)` fills a buffer of the caller. The Nikon compression is reported with an
  `UnsupportedCompressionException` unless it is the lossless variant (see below). The packing follows the TIFF rules; it is checked with synthetic files
  and has not yet been checked on a camera file.
- Decoding of the lossless Nikon compression (TIFF compression 34713, 12 and 14 bits), through the same `RawFile.Decode`. The
  format facts it relies on (the prefix codes, the bit order, the difference and prediction rules, the compression record of
  the MakerNote) are written in the documentation of `NikonLosslessDecoder`; the code is written from them. Checked on 570 real
  files of a Nikon Z 6 (every row decoded, every sample in range, the stream ending at the end of its strip), by an
  encoder-decoder round trip on synthetic files, and with damaged files. Decoding a 24-megapixel image into a reused buffer
  takes about 0.26 s and allocates nothing but a few bytes. The lossy variant is reported with an `UnsupportedCompressionException`.
- The black level recorded by the camera: `RawMetadata.BlackLevel` and `RawImage.BlackLevel`, a `RawBlackLevel` with the value of
  each of the four photosites of the colour filter pattern (in the order of the pattern, which for a red, green, green, blue
  pattern is red, green, green, blue) and `At(column, row)`. It is null when the file holds none or one that does not fit the
  samples, and it is only read for a red, green, green, blue pattern and 14 bit samples (the layout checked on camera files). It is not subtracted from the samples. On the 570 real files of a Nikon Z 6 the four values are present and equal.
- The white balance of the shot: `RawMetadata.WhiteBalance` and `RawImage.WhiteBalance`, a `RawWhiteBalance` with the
  multipliers of the red, two green and blue photosites and where they were read (`RawWhiteBalanceSource`), taken from the plain
  record of the Nikon MakerNote. It is null when the file holds none or one that is not plausible (non-positive, tiny or very large
  multipliers). It is not applied to the samples. The record that Nikon cameras scramble with the serial number is not read yet.
- The release mechanics: a `Release` workflow, started by a version tag, that checks the tag (on `main`, CI passed, equal to the
  version of the project, with a section in this changelog), tests, packs and publishes to nuget.org by trusted publishing
  (no key stored), then creates the GitHub release; the one-time setup is described in `RELEASING.md`.
