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
