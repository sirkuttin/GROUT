# Publishing releases

[← Back to README](../README.md) · [Development](development.md)

The C# script [`scripts/release.csx`](../scripts/release.csx) builds, tests and optionally publishes a Windows x64 release. It uses the repository-pinned `dotnet-script` local tool.

## Setup

Run from the repository root with Windows, .NET 10 SDK and Git installed. Publishing also needs an authenticated GitHub CLI with repository access.

```powershell
dotnet tool restore
dotnet tool run dotnet-script -- scripts/release.csx -- --help
```

With `dotnet-script` installed globally, you can instead work from the `scripts` folder:

```powershell
dotnet script .\release.csx -- --help
```

## Build and test without publishing

```powershell
dotnet tool run dotnet-script -- scripts/release.csx
```

Without `--publish`, nothing is pushed or published. The script builds the project version, runs packaged regression tests and writes five assets under `outputs/releases/<version>/`:

- `GROUT.exe` — the normal Windows x64 download
- `GROUT-<version>-win-x64.exe` — identical compatibility copy for older updaters
- `GROUT-<version>-SHA256SUMS.txt`
- `GROUT-<version>-TEST-RESULTS.txt`
- `GROUT-<version>-LICENSES.txt`

If a running local release EXE locks the output, a separate `build-*` subfolder is used; the script does not stop that instance. Successful builds remove their temporary EXE/test report and empty working directory. Failed builds remain available for troubleshooting.

## Publish a new version

1. Update `<Version>` in [`ReleaseDock/ReleaseDock.csproj`](../ReleaseDock/ReleaseDock.csproj).
2. Add notes under `releases/`, for example `releases/v0.6.0.md` for a new 0.6.0 release (substitute the next version you intend to publish).
3. Commit the code and notes on `main`. The checkout must be clean and origin must match the target repository.
4. Publish:

```powershell
dotnet tool run dotnet-script -- scripts/release.csx -- `
  --notes releases/v0.6.0.md --publish
```

This example assumes the project has already been changed to 0.6.0. Optional `--version` must match the project; it does not edit it.

The script builds in a new directory, enforces single-EXE output below 1,000,000 bytes and runs packaged tests without adjacent DLLs or a config file. It pushes `main`, creates a draft release targeting the tested commit, uploads all five assets and verifies their SHA-256 digests and sizes. Only then does it publish as latest. Repository visibility is unchanged.

Existing releases are never overwritten, including drafts. If uploading or verification fails, inspect the draft on GitHub before retrying. Resolve or remove that incomplete draft deliberately; rerunning does not replace it automatically.

## Flags

| Flag | Effect |
| --- | --- |
| `--version X.Y.Z` | Validate the version; defaults to the project version |
| `--notes FILE` | Markdown notes; required to publish |
| `--repo OWNER/REPO` | Defaults to `sirkuttin/GROUT`; must match origin |
| `--publish` | Push and publish after successful checks |
| `-h`, `--help` | Show help |

Unknown, duplicate or incomplete flags fail with usage. `--notes` paths resolve from where you invoked the command. From `scripts`, use `../releases/v0.6.0.md`.

## Push source changes without releasing

Documentation or script-only changes can be committed and pushed normally:

```powershell
git add README.md docs/
git commit -m "Improve documentation"
git push
```

This does not create a release. Use `--publish` only when you intend to release a new version.

Releases use the stable download filename GROUT.exe. Both EXE asset names share the same SHA-256; the compatibility asset lets 0.5.0 self-updaters reach newer releases. The script also validates that existing local/remote tags and the published tag resolve to the tested commit. Successful publication removes the redundant local compatibility EXE; GitHub retains it.
