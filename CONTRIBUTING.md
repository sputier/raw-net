# Contributing to Eleris.RawNet

Thank you for your interest. Bug reports, small fixes, documentation, test files and benchmarks are all welcome. For a new
feature, a new camera or a large change, please open an issue first so that the design can be discussed before you write code.

By taking part you agree to follow the [code of conduct](CODE_OF_CONDUCT.md). To report a security problem, do not open an
issue: follow [SECURITY.md](SECURITY.md).

## Reporting a bug

Open an issue with the *Bug report* form. The most useful report has:

- the version of the library, the .NET version and the operating system;
- the camera model and the settings that matter (bit depth, compression);
- the smallest code that shows the problem;
- the file that triggers it, or a way to produce it. Do not attach a file you are not allowed to share, and remember that a
  raw file holds personal data (GPS position, serial number, date, embedded preview): crop or strip it first, or describe it.

A file that makes the library throw anything other than a `RawException` (or an `ArgumentException` for a wrong call),
allocate far more memory than its size justifies, or run for a long time compared with its size is a security problem: see
[SECURITY.md](SECURITY.md).

## Building and testing

You need the .NET SDK pinned by [`global.json`](global.json).

```shell
dotnet test Eleris.RawNet.slnx -c Release
dotnet format Eleris.RawNet.slnx --verify-no-changes
```

Tests read synthetic files built by `tests/Eleris.RawNet.Tests/Synthetic`. Tests on real camera files are optional: they use
the folder named by `RAWNET_SAMPLES_DIR`, read it only, and are reported as skipped, never as passed, when it is not set.
Never commit a camera file: use the synthetic builder, or a file you made yourself that holds no personal data and that you
are free to publish under the MIT licence.

## Making a change

1. Fork the repository and create a branch from `main`.
2. Make the change, with tests. A change is finished when automated tests that exercise it pass; a bug fix starts with a test
   that fails without the fix.
3. Make sure that the build has no warning (analyzers are on and warnings are errors) and that every public member has its
   XML documentation (the build fails otherwise).
4. Update the documentation the change touches and the [changelog](CHANGELOG.md) when the change is visible to users.
5. If the change is on a hot path (decoders, readers), run the benchmarks of `benchmarks/` before and after and give the
   figures in the pull request. A performance change is kept only if the gain is measured. Code that reads a file also has an
   allocation budget, checked by a test.
6. Open a pull request against `main` and fill in the template. The CI must be green before a merge.

### Conventions

- The format is set by [`.editorconfig`](.editorconfig); files use LF line endings.
- The library targets net10.0 only, has no dependency, no native code, and stays compatible with trimming and NativeAOT.
- The format knowledge comes from public documentation and from observation of files. Do not copy or translate code from
  other raw decoders, whatever their licence: describe what you observed and what the documentation says instead.
- Code that reads a file must stay bounded on hostile input: every allocation and loop is limited by the size of the file or
  by an explicit option, and failures are `RawException`. Add a test, or a fuzzing case, when you fix a robustness problem.
- Commit messages: a short imperative summary line (`Read the Nikon MakerNote`), then the reason and the measurements if any.
  Reference the issue (`Closes #123`).

## Licence

By contributing you agree that your contribution is licensed under the [MIT licence](LICENSE) of the project.
