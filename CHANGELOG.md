# Changelog

All notable changes are listed here. The project follows [Semantic Versioning](https://semver.org); before 1.0 the API may
change between minor versions.

## Unreleased

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
