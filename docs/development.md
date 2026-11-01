# Development

[← Back to README](../README.md) · [Publishing releases](releasing.md)

## Requirements

- Windows
- .NET 10 SDK
- Git

The project folder and internal namespace retain the older `ReleaseDock` name. The built executable is `GROUT.exe`.

## Build

From the repository root:

```powershell
dotnet build ReleaseDock/ReleaseDock.csproj -c Release
```

Publish a self-contained Windows x64 single EXE:

```powershell
dotnet publish ReleaseDock/ReleaseDock.csproj -c Release `
  -p:PublishProfile=Portable-win-x64 -o outputs/portable
```

The profile bundles native libraries and embedded debug symbols, with compression. The .NET desktop runtime accounts for most of the EXE's size; native runtime components extract into the .NET runtime cache at launch.

For a build that also runs packaged tests and generates checksums/license notices, use the [release script in local-only mode](releasing.md#build-and-test-without-publishing).

## Run packaged tests

```powershell
New-Item -ItemType Directory -Force work | Out-Null
Start-Process .\outputs\portable\GROUT.exe `
  -ArgumentList '--self-test', "$PWD\work\test-results.txt" -Wait
Get-Content .\work\test-results.txt
```

The suite uses isolated libraries, temporary files and mocked HTTP. It exercises a transient native tray lifecycle and a real self-update stop/replace/restart handoff against isolated executable copies. It does not run third-party installers, replace the user's running GROUT EXE, load their app library or change startup registration.

Real third-party installer/uninstaller execution, Windows notification delivery, startup behavior and live accessibility/multiple-DPI behavior still need manual testing.

## Diagnostics

These commands write reports without changing the tracked-app library. Use absolute report paths when invoking them from another working directory.

| Command | Purpose |
| --- | --- |
| `GROUT.exe --help` | Usage; unknown flags are rejected |
| `GROUT.exe --tray` | Start the normal app hidden in the tray |
| `GROUT.exe --check-self-update REPORT_PATH` | Check GROUT's public stable release endpoint without installing |
| `GROUT.exe --check-repo PROJECT_LINK_OR_OWNER/REPO REPORT_PATH` | Diagnose public releases, including previews |
| `GROUT.exe --detect-installed LOCATION REPORT_PATH` | Read a version from a folder, EXE or manifest |
| `GROUT.exe --find-installation PROJECT_LINK REPORT_PATH` | List matching Windows installed-app registrations |

## UI previews

These render sample data rather than the user's library:

```text
GROUT.exe --preview PNG_PATH
GROUT.exe --preview-missing PNG_PATH
GROUT.exe --preview-setup PNG_PATH
GROUT.exe --preview-app-settings PNG_PATH
GROUT.exe --preview-setup-first PNG_PATH
GROUT.exe --preview-install PNG_PATH
GROUT.exe --preview-portable PNG_PATH
GROUT.exe --preview-files PNG_PATH
```

Documentation [screenshots](images/) come from these previews. Use sample paths and avoid personal library details in public screenshots.

## Local files and notices

`work/`, `outputs/`, `review/`, and build `bin/`/`obj/` folders are ignored by Git. GitHub releases are the published artifact archive; source ZIPs and old builds need not accumulate locally.

The release script cleans its successful temporary EXE/test report after copying verified outputs. Failed builds stay under `work/` for diagnosis.

GROUT is [MIT licensed](../LICENSE). Runtime files in [licenses/](../licenses/) are embedded in the EXE and included with release artifacts. They came from the .NET 10.0.14 runtime packs; review them when updating the bundled runtime.
