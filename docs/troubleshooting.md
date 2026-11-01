# Troubleshooting

[← Back to README](../README.md) · [User guide](user-guide.md)

## App not found after moving or uninstalling it

GROUT checks saved locations when the window regains focus and every minute while running. **App not found** means that saved location no longer exists; it does not prove why it disappeared.

Choose **Find installation** to select a Windows registration or browse to the new EXE/folder, or **Stop tracking** to remove its library entry. Update, uninstall and version editing are disabled until the location is resolved. The last known version is preserved.

## The version cannot be detected

Select the exact EXE or a supported JSON manifest in **App settings**. A folder with multiple plausible EXEs is intentionally ambiguous; GROUT does not recursively search it or guess which binary is the app.

Some EXEs lack usable version metadata or differ from the release tag. Enter a version manually or mark the latest as installed. GROUT records a release it successfully saved even if the portable EXE omits matching metadata.

In manual mode, **Check version** or **Recheck version** reads once. Enable **Read installed version automatically** in App settings to resume continuous reading.

Common differences such as `1.0.36.0` and `hub-1.0.36` are normalized, with original strings visible. Preview/channel differences remain significant. Arbitrary tags may require manual tracking.

## Update is disabled

Check the visible status and App settings. Common reasons include:

- The app is current, or the release would be a recognizable numeric downgrade.
- The installed version is unknown/unverified, or its location is missing.
- The release check failed or the source is in a rate-limit/access pause.
- No unambiguous Windows asset matches the saved download choice.

If an asset was renamed, select the correct download again. **Show all downloads** can expose unusual filenames. A successful version read does not prove all files are current, including files omitted from a release archive.

## No usable release or download is shown

Enable **Include preview releases** for preview-only projects. Confirm the project uses a [supported public host](supported-sources.md), has a published release and supplies an app asset rather than only source-code archives.

GitLab preview detection uses tag naming. Private app repositories and self-hosted servers are unsupported. Downloads requiring sign-in or returning HTML are rejected.

## An installer finished but setup still needs a location

Some installers are unregistered, use unrelated names, spawn longer-lived processes or install under another user's profile. Choose **Find installation** to retry, or browse to the installed EXE/folder. Installer exit alone is not proof of success.

Several plausible registrations require an explicit choice. During an update, GROUT does not silently redirect a configured app to a different folder.

## Replacement or extraction failed

Close the app being updated and check write access. GROUT does not forcibly close third-party apps or silently elevate file replacement. Windows can lock running EXEs or protected folders.

GROUT attempts rollback if file application fails. Completed operations do not keep recovery backups. If rollback could not finish, retain the recovery files named in the error and report the failure before deleting them. An incomplete self-update can leave a workspace under `%LOCALAPPDATA%\ReleaseDock\self-update`.

Download cancellation leaves destination files unchanged. Once application begins, it completes or attempts rollback rather than cancelling midway through writes.

## ZIP files or settings were overwritten

ZIP installs and updates apply every archive file directly, including matching settings/save files. There is no conflict-review screen. Files not present in the archive remain unchanged, and the archive folder structure is preserved. Temporary rollback is internal and completed updates do not retain backups.

## Notifications or startup do not behave as expected

- Closing the window keeps GROUT in the tray; **Exit** stops checks.
- **Quiet mode**, Windows notification settings and Do Not Disturb can suppress alerts.
- Each release tag is announced once; quiet discoveries do not create a later backlog.
- **Start GROUT with Windows** is optional and starts hidden at sign-in. After moving/renaming the EXE, toggle it off and on to save its new path.

## GROUT does not update itself

Use **Check GROUT updates**, beside **Settings** at the bottom right, to retry. Only newer stable releases with the expected Windows x64 EXE and checksum file are offered. Previews and older versions are not installed.

Version 0.4.x needs a manual upgrade to the latest release. Keep the EXE in a permanent writable folder; protected locations may require manual replacement. Self-updates require no GitHub CLI or authentication.

## Storage seems larger than expected

Configuration is in `%LOCALAPPDATA%\ReleaseDock`, retaining GROUT's original folder name. Pending/manual downloads remain until installation is resolved. Completed downloads, ZIP staging and completed rollback copies are cleaned automatically; locked files may wait for a later pass. Interrupted recovery is kept for repair.

From 0.6.0, GROUT uses Windows-provided .NET Framework and does not create a bundled-runtime extraction cache. An older 0.5.x build may have left one. See [storage and cleanup](user-guide.md#storage-and-cleanup) for the inventory.

## Report a problem

Open a [GitHub issue](https://github.com/sirkuttin/GROUT/issues) with the GROUT version, project/release URL, selected asset/update method and reproduction steps. Remove tokens and personal paths from screenshots and reports.

For vulnerabilities, follow [SECURITY.md](../SECURITY.md) rather than posting exploit details publicly. [Diagnostic commands](development.md#diagnostics) can help collect read-only reports.
