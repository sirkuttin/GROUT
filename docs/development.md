# Development

[← Back to README](../README.md) · [Publishing releases](releasing.md)

## Requirements

- Windows
- .NET 10 SDK (build tools and the release-script runner; not an end-user prerequisite)
- Git

The project folder and internal namespace retain the older `ReleaseDock` name. The built executable is `GROUT.exe`.

## Build

From the repository root:

```powershell
dotnet build ReleaseDock/ReleaseDock.csproj -c Release
```

Build a portable Windows x64 single EXE:

```powershell
dotnet build ReleaseDock/ReleaseDock.csproj -c Release `
  -p:PublishProfile=Portable-win-x64 -o outputs/portable
```

GROUT targets .NET Framework 4.8 and uses the assemblies supplied by Windows. The SDK restores the reference assemblies automatically for compilation; no developer pack or runtime DLLs are shipped. The build profile suppresses debug/config output and targets x64. The release script requires exactly one EXE under 1,000,000 bytes and runs it without a config file or adjacent DLLs.

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
GROUT.exe --preview-zip-selection PNG_PATH
GROUT.exe --preview-nexus PNG_PATH
GROUT.exe --preview-missing PNG_PATH
GROUT.exe --preview-setup PNG_PATH
GROUT.exe --preview-app-settings PNG_PATH
GROUT.exe --preview-setup-first PNG_PATH
GROUT.exe --preview-install PNG_PATH
GROUT.exe --preview-portable PNG_PATH
```

Documentation [screenshots](images/) come from these previews. Build the current source first, then render the library, add-app, app-settings, portable and missing-app states. Preview commands use neutral sample paths and render the current app UI without loading your library. Inspect every PNG before replacing a public screenshot.

## Local files and notices

`work/`, `outputs/`, `review/`, and build `bin/`/`obj/` folders are ignored by Git. GitHub releases are the published artifact archive; source ZIPs and old builds need not accumulate locally.

The release script cleans its successful temporary EXE/test report after copying verified outputs. Failed builds stay under `work/` for diagnosis.

GROUT is [MIT licensed](../LICENSE). The [runtime prerequisite notice](../licenses/WINDOWS-FRAMEWORK.txt) is embedded with the GROUT license and included in release artifacts. No .NET runtime is redistributed. Earlier 0.5.x releases bundled .NET 10 and included its redistribution notices.

## App icon

The SVG, PNG and multi-size ICO live in `ReleaseDock/Assets/`. The ICO is embedded in the executable, app windows and tray. Regenerate on Windows from the checkout root with the repository's script runner:

```powershell
dotnet tool run dotnet-script -- scripts/create-icon.csx -- --help
dotnet tool run dotnet-script -- scripts/create-icon.csx
```

The generator uses pinned `System.Drawing.Common` tooling and emits 16, 24, 32, 48, 64, 128 and 256 pixel ICO entries. `--output FOLDER` changes the destination; unknown flags fail with usage.

## .NET Framework compatibility

The app uses Windows Framework assemblies only. FrameworkJson maps the existing JSON format through the built-in parser, preserving ISO dates, 64-bit identifiers, records and ignored runtime-only fields. FrameworkSupport supplies process waiting, argument quoting, SHA-256 and atomic file replacement. FolderDialogs uses the Windows Common Item Dialog. ArchiveChecksums reads central-directory CRCs because Framework's ZIP entry API does not expose them. Portable EXE header validation remains enabled.

The packaged suite includes old-format data and updater plans, strict JSON types, Unicode/date roundtrips, native folder-dialog configuration, ZIP corruption/path checks, portable EXE checks, and isolated updater restart/rollback scenarios.

Website source diagnostic (reads the page, never installs or modifies the library):

```powershell
GROUT.exe --check-website https://framedropvr.com/ https://framedropvr.com/downloads/FrameDrop-Setup-1.0.37.exe website-report.txt
```
