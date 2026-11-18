## What and why

<!-- The problem, and the change. Reference the issue: "Closes #123". -->

## Checklist

- [ ] Automated tests cover the change and pass (`dotnet test Eleris.RawNet.slnx -c Release`).
- [ ] No build warning; every public member is documented; `dotnet format Eleris.RawNet.slnx --verify-no-changes` is clean.
- [ ] Documentation updated: `README.md`, `CHANGELOG.md` when the change is visible to users.
- [ ] Code that reads a file stays bounded on hostile input (`RawException` only, allocations limited by the size of the file or an option).
- [ ] No camera file, no personal data and no code taken from another raw decoder is in the change.
- [ ] A change to a hot path comes with before and after benchmark figures and an allocation budget below.

## Measurements

<!-- Benchmark figures, or "not applicable". -->

## Not verified

<!-- Platforms, cases or configurations you could not test. -->
