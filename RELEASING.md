# Publishing the NuGet package

The package is `Eleris.RawNet`. Its version is `<Version>` in `src/Eleris.RawNet/Eleris.RawNet.csproj`, the only place to
change it (`0.1.0`, or `0.2.0-preview.1` for a prerelease: nuget.org lists a version with a suffix as a prerelease).

## One-time setup

The package is published with [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing): no API
key is stored on GitHub. The `publish` job exchanges the OIDC token of GitHub Actions for a temporary key (valid one hour).

1. On [nuget.org](https://www.nuget.org), click your user name > *Trusted Publishing* and add a policy (the values are not
   case sensitive):
   - **Package owner**: you, or the organization that owns the packages;
   - **Repository owner**: `sputier`, **Repository**: `raw-net`;
   - **Workflow file**: `release.yml` (the file name only);
   - **Environment**: `nuget`;
   - **Scopes**: publishing new packages and new versions, with the glob pattern `Eleris.RawNet*`.

   A policy for a private repository is active for 7 days until a first publication gives nuget.org the identifiers of the
   repository; this repository is public, so the first publication makes it permanent.
2. On GitHub, the environment `nuget` (*Settings > Environments*) exists and has the maintainer as **required reviewer**
   (every publication waits for the approval, whoever pushed the tag). Add the secret `NUGET_USER` to it: your nuget.org
   **profile name**, not your e-mail address. It is the only value the workflow needs; it is not a credential.
3. The ruleset *Protect version tags* (*Settings > Rules > Rulesets*) restricts the creation, the update and the deletion of
   the tags `v*` to the repository administrators, so that only they can start a publication.

## Releasing a version

1. Change `<Version>` in `src/Eleris.RawNet/Eleris.RawNet.csproj`, move the entries of `CHANGELOG.md` from "Unreleased" to
   the new version, and commit.
2. Check the package locally (package validation runs during the pack; the CI checks that the package holds the library, its
   documentation file and the readme, and has no dependency):

   ```shell
   dotnet pack src/Eleris.RawNet -c Release -o artifacts
   unzip -l artifacts/Eleris.RawNet.*.nupkg
   ```

3. Tag and push. The tag is `v` followed by the version:

   ```shell
   git tag v0.1.0
   git push origin v0.1.0
   ```

Push the tag once the CI of the commit on `main` has passed. The `Release` workflow then checks that the tag is on `main`,
that the CI passed on it, that it matches the version and that the changelog has its section; runs the tests; packs in a job of its own and checks the content of the package; waits for
the approval of the `nuget` environment; gets a temporary API key from nuget.org (trusted publishing), pushes the `.nupkg` and
its symbols (`.snupkg`) and creates the GitHub release with the notes of the changelog.

A published version cannot be replaced. It can only be *unlisted* on nuget.org, so a mistake costs a new version number.

## API compatibility

`<PackageValidationBaselineVersion>` in the project is the last published version: package validation compares each new
package with it and fails the pack on an API break (for example removing a public member). After publishing a version, set it to that
version in the next change; for a deliberate break before 1.0, raise the version number accordingly and set the baseline once
the new version is published.
