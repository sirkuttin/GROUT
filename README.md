# G.R.O.U.T.

**Git Release Observer & Update Tool** · Version 0.5.0

A native Windows app for keeping track of public app releases on **GitHub, Codeberg.org and GitLab.com**. The Windows x64 portable build is a single `GROUT.exe` with .NET included. Download it and run it; no adjacent DLLs or separate .NET installation are required. Native runtime components are automatically extracted into the .NET runtime cache at launch. Close older versions before launching this one. Your existing library is retained.

## Download

Get the Windows x64 portable EXE from [the latest GitHub release](https://github.com/sirkuttin/GROUT/releases/latest). Download it to a permanent folder and run it. Release assets include SHA-256 checksums and the packaged test report.

**Early release:** the EXE is currently unsigned. GROUT changes installed app files only after confirmation; close the app being updated first. New features on `main` may not yet be in the latest release. Report reproducible problems through [GitHub Issues](https://github.com/sirkuttin/GROUT/issues), with the GROUT version and steps, and remove tokens and personal paths from attachments.

## Add an app

**Add App** opens two screens:

1. Paste a GitHub, Codeberg.org or GitLab.com project/release link. The source is detected automatically and the project checked after a brief pause or on **Next**. GitHub also accepts `owner/repository`. Enable **Include preview releases** for preview-only projects. Choose whether the app is already installed or you want to install it.
2. For an existing app, select its folder or EXE and detect the installed version, or enter a version manually. Choose the Windows release file and confirm how future updates should work. For a new app, select its release file, update method, and a destination where applicable. After an installer runs, GROUT looks for its Windows installed-app entry and automatically adds the app and closes setup when it finds a clear completed installation. Only uncertain cases need the completion screen.

**Add App** on an existing installation only adds tracking. New installations use an action-specific button: **Save and add app** for portable EXEs, **Download and run installer** for installers, or **Extract and add app** for ZIPs. Installer/manual-download completion stays on a separate state of screen two. Cancelling setup never adds to the library; downloads or installations already performed remain on disk, with an explanation before leaving.

For a new portable EXE, explicitly choose **Save as a portable app**, select **Save in folder**, and confirm the filename and full destination. **Save and add app** saves the EXE without running it and adds it directly to the library. This page contains no existing-install location, manual version or reconciliation fields. It creates the destination folder if needed. Existing files require overwrite review and temporary rollback protection. Cancelling review or declining an installer keeps the original setup and retry action; neither counts as a successful installation.

After an exact portable EXE has actually been saved, GROUT records the source release it saved. If the EXE provides matching version metadata, automatic reconciliation is enabled. If metadata is missing or differs, the applied release is retained with a visible provenance explanation and automatic metadata comparison is paused. A successful file save is not treated as failed merely because the EXE omits version metadata. Future GROUT replacements update this recorded release.

Duplicate repositories open the existing library item. Tracking an existing app does not require a working release download or successful local version detection. New installations require a release asset.

## Update methods

| Method | Behavior |
| --- | --- |
| I'll update it myself | Download and open the file's folder. Install/copy it yourself, then reconcile or mark it installed. |
| Replace this app file | Replace the explicitly configured portable EXE. Preview the exact destination, back up the old file, then apply. Close the app first. |
| Run an installer | Download the selected EXE/MSI, then explicitly offer to run it. The installer controls its own destination. Installer exit never proves the installation succeeded. |
| Extract into this folder | Stage the ZIP, inspect its paths, choose whether to remove a single outer folder, and review individual additions/replacements. Existing settings/save files are unchecked by default using conservative filename rules. Review all file selections before applying. |

ZIP updates preserve files absent from the archive. Replaced files use temporary rollback copies during the operation. A failed application attempts to restore already-changed files; error messages identify the backup if recovery needs attention. Unsafe archive paths, Windows device names, duplicate/colliding paths, archive links and linked destination paths are rejected. ZIPs are limited to 10,000 entries and 8 GB uncompressed data. No forced app shutdown or silent privilege elevation is performed by file replacement; unwritable/locked destinations need attention before retrying.

**Restore previous files** reverts the last file-based update. It restores replaced files and removes files that update added; later changes to those files will be overwritten, as the confirmation explains. Empty directories may remain. Backups are retained in GROUT's data folder. Installer rollback is controlled by that installer, not GROUT.

The update method is suggested from the file/location combination and is visible and editable before saving. An EXE's extension is not proof it is an installer or portable app. JSON manifests can be version sources while a separate EXE/folder is the update destination.

**Update** is disabled if the installed version is unknown/unverified, the release check failed, the version matches, or no unambiguous configured asset matches. The selected asset family is remembered across version-number changes while preserving architecture/suffix distinctions. Ambiguous/renamed assets require selecting a file again in **App settings**. Other formats, including MSIX and 7z, use manual handling.

Downloads are staged in the app data folder, checked against the source's byte count and SHA-256 digest when supplied. If the API does not provide a byte count, the HTTP content length is checked when available. Size unknown is shown honestly. A cached download is reused only when its tag/name/size and supplied SHA-256 still match. Download cancellation preserves existing destination files and removes partial downloads. File application completes or rolls back once started.

## Polling, tray and notifications

In **Settings**, choose **Manual only**, **Every hour**, **Every 6 hours**, or **Every day**. Daily is the default. Checks run when due, including on launch and after wake; last-success and next-check times appear in the main window. Manual checks are available subject to source-specific backoff. Checks are staggered across apps. Rate limits/access restrictions pause only the affected host, and its pause survives restarts. Public API limits still apply.

## Supported sources in 0.4.0

- **GitHub:** public repository releases, retaining existing `owner/repository` library keys and native draft/prerelease behavior.
- **Codeberg.org:** public repository releases and assets, including preview releases when enabled. Both project and release URLs are accepted; drafts are excluded. Source identity is retained so equal owner/project names on different hosts do not collide.
- **GitLab.com:** public projects, including nested subgroups and `/-/releases` URLs. Release asset links are used; automatically generated source-code archives are excluded from app downloads. Friendly asset labels can derive a filename from the linked download. Linked HTTPS asset hosts/CDNs are supported; a resource requiring sign-in will not be saved as an EXE.

GitLab does not supply the same native prerelease flag as GitHub/Codeberg. Stable selection excludes tag names containing recognized markers such as alpha, beta, rc, preview, nightly or dev; enabling previews includes them. Future-dated/upcoming releases remain excluded. This heuristic is explained in setup and cannot recognize every publisher's naming scheme. Selection examines up to 100 releases returned by each provider's list endpoint.

Examples:

```text
https://github.com/owner/app
https://codeberg.org/owner/app/releases
https://gitlab.com/group/subgroup/app/-/releases
```

This version does not add other/self-hosted Gitea/Forgejo/GitLab servers, private repositories, credentials, standalone download URLs or website scraping. Those are separate features. Release links may point to HTTPS CDNs; redirect handling rejects HTTPS-to-HTTP downgrade, embedded credentials and obvious local/private literal addresses. Source metadata redirects stay on the same API host. The source can still publish unsuitable packages, so choose the correct release asset and update method.

Closing the window keeps GROUT in the Windows tray. Double-click the icon to reopen; right-click for Open, Check now, Settings or **Exit**. Exit stops checking. Notifications announce each newly discovered app/release tag once, with a quiet option. Releases discovered during quiet mode are recorded without later flooding notifications. Windows notification and Do Not Disturb settings govern whether alerts are displayed.

**Start GROUT with Windows** is opt-in and uses the current user's Run registry entry. Keep the executable in a permanent location before enabling it. Startup opens in the tray. Re-save the setting after moving the app. Only one normal instance of this version runs per user; diagnostic commands are separate.

## Installed versions

Local versions are checked during release checks, every minute while running (including in the tray), and when returning to the window. Folder detection checks `release-manifest.json`, then `vrmod-manifest.json`, then a single top-level EXE excluding installer/updater/crash-handler names. You can choose a specific EXE or JSON file with a top-level `release_tag` or `version` string. No recursive scan is performed. Continuous version detection reads files; post-installer discovery can use a Windows registered version as a clearly labeled one-time fallback.

## Automatic installer completion in 0.4.6

Before starting an installer, GROUT takes a read-only snapshot of Windows installed-app entries in the current-user and machine uninstall registries, including 32-bit and 64-bit views. After the installer returns, it looks for matching entries, favoring exact project/asset/app names and newly added or changed registrations. It reads InstallLocation, DisplayIcon and, as a directory-only fallback, UninstallString. It never executes uninstall commands.

**A clear new/current installation is added automatically and the setup window closes—there is no final directory screen or extra Add click.** A recognizable older version or a registration without an app file/version source does not falsely count as completed installation. Several plausible locations require an explicit choice. If no suitable registration appears, the completion screen offers **Find installation**, manual browsing and version entry. A short recheck accommodates installers that spawn another process; Find installation can retry later without rerunning the installer.

Matching is best effort. Unregistered apps, unusual names, inaccessible registry entries and installers running under another user's profile can need manual selection. A confirmed existing app's configured folder is not silently redirected during an update. Standard file version/manifest detection is preferred; if unavailable, the selected Windows entry's DisplayVersion is recorded with registry provenance and automatic file detection is paused. Raw before/after registry scans are transient and not stored in the library.

For D2R PCVR, choose `C:\Users\sirku\AppData\Local\Programs\D2R PCVR`. Its release manifest provides the exact GitHub tag. Configure **Run an installer** with its setup EXE for future updates; the version source is still the installed folder.

EXE detection reads ProductVersion, falling back to FileVersion, without executing the file. Numeric comparison normalizes a leading `v`, an alphabetic project prefix such as `hub-` or `release-`, trailing zero components and build metadata after `+`. Thus `1.0.36.0` matches `hub-1.0.36`, while the original strings remain visible. Preview/channel suffixes are never discarded for equality. Recognizable numeric cores prevent offering older releases as ordinary updates; same-core channel differences and incomparable names are described as different releases. Arbitrary naming schemes are not given a guessed total ordering.

Missing/unreadable/invalid sources preserve the last known version and show an unverified state. Detection reports its source; it is not an integrity check of all installed files. **Installed version** contains rechecking, manual entry and **Mark latest as installed**. Manual marking pauses automatic reconciliation; **Recheck version** or App settings can resume it.

## Data and migration

Library: `%LOCALAPPDATA%\GROUT\apps.json`. Preferences: `settings.json` in the same folder. Downloads, staging and backups are subfolders there. Back up this folder to retain configuration and recovery files. Caches/backups are not automatically deleted; remove unneeded ones after successful updates, retaining any backups you still want to restore.

Older library entries default to manual update handling. Open **App settings** once to configure an installer, portable replacement or extraction destination. Release metadata and notification deduplication are cached across restarts. Review files are excluded from the source/build archives.

## App settings in 0.3.3

Use the single **App folder** directory field. For portable EXEs, **App file** is a filename only; the full replacement path is derived and shown read-only. A separate version file can be chosen under Advanced settings. Legacy source/target conflicts require an explicit choice; saving does not silently redirect replacement.

The method formerly called I'll update it myself is now **Download only**. Version status reflects the selected tracking mode, a changed source is marked not checked, and Check version performs a one-time read without silently enabling automatic tracking. **Enter version manually** opens the appropriate controls. The main window separately offers **Open app folder** and **Open downloaded package folder**.

Ordinary Update blocks recognizable numeric downgrades. Partial ZIP application of non-settings payload keeps a warning even if a new manifest reports the latest version. File-restore backups retain prior version/provenance/mode; older backups without those records fall back to unknown rather than retaining an incorrect latest version. ZIP staging is cancellable between chunks. Initial windows are bounded to the monitor's working area/DPI; live accessibility and multi-scaling testing are still pending.

Known follow-ups include cancellation of check batches, stopping an external-installer wait, richer corrupt-data recovery, explicit tracking-only mode . Installer execution, Windows notification delivery and startup registration are not exercised by the automated suite.

## Build and diagnostics

Requires Windows and .NET 10 SDK:

```powershell
dotnet build ReleaseDock/ReleaseDock.csproj -c Release --source https://api.nuget.org/v3/index.json
dotnet publish ReleaseDock/ReleaseDock.csproj -c Release -p:PublishProfile=Portable-win-x64 -o outputs/GROUT-0.4.8 --source https://api.nuget.org/v3/index.json
```

```text
GROUT.exe --help
GROUT.exe --tray
GROUT.exe --self-test REPORT_PATH
GROUT.exe --preview PNG_PATH
GROUT.exe --preview-setup PNG_PATH
GROUT.exe --preview-app-settings PNG_PATH
GROUT.exe --preview-setup-first PNG_PATH
GROUT.exe --preview-install PNG_PATH
GROUT.exe --preview-portable PNG_PATH
GROUT.exe --preview-files PNG_PATH
GROUT.exe --check-repo PROJECT_LINK_OR_OWNER/REPO REPORT_PATH
GROUT.exe --detect-installed LOCATION REPORT_PATH
GROUT.exe --find-installation PROJECT_LINK REPORT_PATH
```

Unknown flags fail with usage. Preview commands use sample data. Self-tests use isolated temporary libraries/files and mocked HTTP, with a real transient native tray lifecycle test in manual/quiet mode; they never launch third-party installers or modify your tracked installations/startup settings. Live repository and local-version diagnostics write reports without changing the library.

Download choices hide clearly named non-Windows files, source/symbol archives, and incompatible architectures by default. Unlabelled archives remain candidates. Show all downloads exposes unusual filenames and other platforms when needed; the choice is saved per app.

App names appear above repository names in the library and details. App settings lets you edit the name; installed EXE metadata or filename supplies a suggestion.
Uninstall launches a uniquely matched registered Windows uninstaller. Portable EXE removal recycles just the selected EXE; portable ZIP removal recycles the entire app folder after confirmation. System/common shared folders are blocked. Stop tracking remains separate. If no registered uninstaller matches, Windows Installed apps opens. Uninstaller completion is not automatically verified.

GROUT means Git Release Observer & Update Tool. This rename retains the existing %LOCALAPPDATA%\ReleaseDock data folder and startup registration for compatibility. Close ReleaseDock before launching GROUT. If Windows startup was enabled, toggle it off/on in GROUT settings to point it at GROUT.exe in its new location.

Storage cleanup (0.4.6): completed portable operations discard their downloaded payload, ZIP staging and temporary rollback copies. Verified installer downloads are removed. Pending/manual downloads remain available. Startup and idle saves also remove obsolete downloads, staging and completed legacy backups. Interrupted recovery journals are preserved for repair; cleanup failures such as locked files are retried later.

Missing installations: GROUT checks saved app locations on activation and every minute while running, even with manual version tracking. Missing locations show an amber App not found banner and sidebar status, with Find installation and Stop tracking actions. The last recorded version is preserved as historical information; update, uninstall and version editing are disabled. Find installation offers matching Windows registrations or an EXE/folder picker. Restoring a valid location clears the missing state; unreadable version metadata remains a separate unverified state.
GROUT.exe --preview-missing PNG_PATH renders the missing-installation screen using sample data.

## Build and publish a release

The release script is C# (`scripts/release.csx`), run through the repository-pinned `dotnet-script` local tool. Run from the checkout with .NET 10 SDK, Git, and an authenticated GitHub CLI:

```powershell
dotnet tool restore
dotnet tool run dotnet-script -- scripts/release.csx -- --help
# Build, test and checksum locally without pushing or publishing:
dotnet tool run dotnet-script -- scripts/release.csx -- --version 0.4.8
# After updating the project version, writing release notes and committing all changes:
dotnet tool run dotnet-script -- scripts/release.csx -- --version 0.4.8 --notes releases/v0.4.8.md --publish
```

The script validates the project version, requires a clean main branch and matching origin before publication, builds into a new isolated directory, enforces exactly one EXE, and runs the packaged self-tests. It writes distributable files under `outputs/releases/<version>/`, pushes main, uploads a draft release targeting the tested commit, verifies SHA-256 and size for all four assets, then publishes it as latest. Repository visibility is unchanged. Existing releases are never overwritten. If upload or verification fails, inspect the draft on GitHub before retrying; the script refuses to overwrite an existing release, including a draft. All options use flags; unknown or duplicate flags fail with usage. `--version` defaults to the project version, `--repo` to `sirkuttin/GROUT`, and `--notes` is mandatory for `--publish`.

New portable ZIP installations treat the chosen folder as a parent and automatically create an app-named subfolder (for example, choosing C:\Apps for Gram produces C:\Apps\Gram). The full extraction path is shown before confirmation. Future updates use that saved app folder. Existing app locations and ZIP update destinations are unchanged.


## GROUT updates itself (0.5.0)

GROUT checks its own stable GitHub releases on startup and daily while running. This is separate from the schedule for tracked apps and can be turned off in Settings. **Check GROUT updates** performs a manual check; **Update and restart** appears when a newer stable Windows x64 EXE and its checksum file are available.

After confirmation, GROUT downloads the new EXE, verifies its SHA-256 against the exact release checksum, starts an update helper, then closes itself. The helper waits for the running instance to exit, replaces the same executable path and restarts it. Library, preferences and startup path remain intact. The helper checks that it is replacing the actual parent process's EXE and preflights write access before asking it to exit. Replacement or immediate launch errors attempt rollback; failures leave recovery details and an alert. No silent elevation is attempted: protected folders may require manual updating.

The updater uses the public `sirkuttin/GROUT` GitHub releases API and requires no credentials or GitHub CLI. It excludes previews and downgrades. The initial upgrade from 0.4.x must be downloaded manually because those builds do not contain the updater. Download-only mode is not used for GROUT's own updater.

Temporary update payloads are removed after successful restart. If a restart or repair is interrupted, its workspace may remain under `%LOCALAPPDATA%\ReleaseDock\self-update` for troubleshooting. Native .NET extraction cache behavior is unchanged.

`GROUT.exe --check-self-update REPORT_PATH` checks the public endpoint without installing anything or changing the library. Automated self-tests exercise real replacement/restart using isolated executable copies and a temporary update workspace; they never replace the user's running GROUT executable or load their app library.

## License and security

GROUT is [MIT licensed](LICENSE). The EXE embeds the GROUT license and the bundled .NET runtime's license/notices; Settings provides a viewer. Release packaging also attaches a combined licenses file. The notices in `licenses/` come from the .NET 10.0.14 runtime packs and should be reviewed when updating the bundled runtime.

See [SECURITY.md](SECURITY.md) for private vulnerability reporting. Downloads are currently unsigned; code signing remains a follow-up for wider distribution.
Successful release-script runs remove their temporary published EXE and test report from work/ after copying verified artifacts to outputs/releases/<version>/. Failed builds stay available for troubleshooting. Git is the source archive; old packaged copies need not be retained locally once published.

If a local release EXE is running, the release script writes the new artifacts to a separate build subfolder instead of overwriting or stopping that instance.

