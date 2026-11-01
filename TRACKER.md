# ReleaseDock tracker

## 2026-10-30 — First working version

- User requested building a Windows alternative to Obtainium for apps installed from GitHub.
- Implementation defaults: native WPF/.NET, public stable releases, manual installed-version tracking and downloads. Working name: ReleaseDock.
- Built: persistent library, release checks on startup and hourly while open, asset chooser, download progress, size/checksum verification, release links and notes, removal, CLI help, isolated self-tests and preview rendering.
- Later candidates: tray notifications, startup checks, optional GitHub authentication, portable app replacement, and installer integration.
- No outstanding user decisions.
- Validation: Release build with zero warnings/errors; isolated tests passed for URL validation, persistence, release parsing, HTTP 404 handling, download integrity and preservation of existing files on checksum failure. Packaged executable self-tests passed. Window rendered and visually inspected. Real GitHub release endpoint confirmed accessible; interactive end-to-end installation has not been tested.

## Preview release fix — 0.1.1

- Screenshot showed `bearstonem/d2r-pcvr` failing because its releases are prereleases and the stable-only GitHub endpoint returns 404.
- Added a persistent per-app **Include preview releases** setting and immediate recheck when toggled. Stable-only remains the default. Preview mode chooses the newest publication among the 100 returned releases, excluding drafts.
- Improved stable-only 404 text to explain the preview setting and URL check.
- Added regression coverage for preview selection, draft exclusion, publication sorting, empty lists, and setting persistence. Added a read-only live repository diagnostic command.

## Optional local version reconciliation — 0.2.0

- User requested optional install-location version detection while retaining manual marking. Supplied D2R PCVR installation folder.
- Read-only inspection found `release-manifest.json` with exact tag `v0.3.30-windows-preview`; a bundled launcher installer reports an older `0.3.5`, confirming arbitrary EXE selection would be misleading.
- Added persisted install location and opt-in automatic reconciliation, folder/file pickers, manifest and EXE metadata readers, and visible detection source/errors. Checks run on release fetch, focus and a one-minute local timer. Manual marking/saving pauses automatic reconciliation.
- Conservative numeric matching allows `1.2.3.0` to match `v1.2.3`, preserving preview suffix distinctions. Failed detection retains the previous version and marks it unverified.
- Regression tests cover local updates, corrupt/missing files, manual mode, tag precedence, ambiguous folders, numeric matching, persistence and EXE resources. Read-only diagnostic confirmed the supplied D2R folder detects `v0.3.30-windows-preview`.
- Common Windows sources discussed: EXE version resources, installer registry DisplayVersion, package metadata, app manifests. Registry/package detection remains future work.

## UX review — 0.2.1

- User requested **Update** instead of **Download file**, enabled only for available updates. Implemented label, centralized eligibility rule, and handler guard; unknown/current/unverified/failed-check states cannot update. Different tags remain the existing update heuristic.
- Polling remains fixed hourly while open, without notifications. UX proposals are tracked in `review/DECISIONS.md`.

## UX decisions received — 2026-10-30

- Decision 1: **B approved** — configurable polling, daily default; manual/hourly/every six hours/daily choices; manual checks and last/next check display. Approved, not yet implemented.
- Decision 2: **A approved** — tray and Windows notifications, deduplicated by app/release; close hides to tray, Exit stops checking; quiet setting and separate opt-in Windows startup. Approved, not yet implemented.
- Decision 3: further discussion requested; no guided-installation choice approved. Decisions page now contains only this open question. Recommended explicit per-app installer handoff with independent installed-version reconciliation.

## Update routing clarified — 2026-10-30

- User wants EXE-to-EXE updates to replace the selected installed EXE where appropriate.
- For a configured installation folder, user wants the selected EXE/MSI run as an installer, or a selected ZIP extracted to the folder.
- Record this as the desired update routing, not implemented yet. Keep version-source selection distinct from update destination when the selected source is a JSON manifest.
- Open detail: staged replacement/extraction, backups, archive layout mapping and treatment of existing config/save files. Proposed implementation uses explicit per-app update method, bounded extraction and no deletion of files absent from an archive.

## Add App setup proposal — 2026-10-30

- User proposed removing the main-window add-repository textbox, renaming Track app to Add App, and opening a guided setup of roughly two screens. Asked for thoughts; setup flow is under discussion, not implemented.
- Proposed screen 1: repository validation/release discovery and already-installed versus new-install intent. Screen 2: conditional location/asset/update-method/version fields and an explicit action summary.
- Updated open decisions to cover two-screen structure and the still-unresolved replacement/extraction policy. Previous daily-polling and tray approvals remain recorded above.

## UX specialist review — 2026-10-30

- User explicitly requested sending the screen design to a UX designer. Delegated to a UX design agent; review completed and saved in `review/ux-design-review.md`.
- Reviewer recommends two screens with conditional second-screen content; existing apps identify install location before release asset selection. New installer flows discover the installed folder after installer handoff.
- Keep manual detection fallback and marking visible. No automatic EXE classification solely by extension. Keep global preferences outside Add App.
- Reviewer proposed deferring automatic ZIP application until conflict/layout policy is settled. This is advice, not an approved change in scope.

## Approved implementation — 0.3.0

- User approved making all discussed changes, including the two-screen Add App flow and configured update methods. No open decisions remain for this release.
- Removed the main-window URL field; added two-screen repository lookup and existing/new install setup, preview channel selection, duplicate handling, manual version fallback, conditional destination controls and visible update-method summaries. Installer completion uses the same second screen to establish the installed location.
- Added daily-default configurable polling (manual/hourly/six-hourly/daily), persisted schedule/cache, last/next display, staggered requests and GitHub rate-limit backoff.
- Added native Windows tray lifecycle, deduplicated update notifications, quiet mode, first-close explanation, explicit Exit and separate opt-in current-user Windows startup. Normal launches use a single-instance mutex.
- Implemented configured installer handoff, exact portable EXE replacement and reviewed ZIP extraction. ZIP mapping can remove one outer directory; per-file selections preserve likely settings/save files by default. No absent-file deletion. File updates use backups, preflight locks, transactional replacement, automatic rollback on failure and explicit recovery. Linked/escaping/ambiguous archive paths are rejected.
- Update remains unavailable for current/unknown/unverified/failed states and needs a matching configured asset. Manual marking remains available. Installation state is reconciled independently of download/installer completion.
- Added cancellation, inspected current screen renders, and expanded regression tests to feature, wizard and native tray integration checks. Final packaged results are recorded with the delivered build.
- Final validation: self-contained Windows x64 publish succeeded with zero warnings/errors. Packaged executable passed the existing suite plus **48 feature assertions and 17 wizard/tray assertions**, including actual native tray create/close-to-hide/reopen/exit lifecycle. Main/setup/install/file-review screens rendered and visually inspected. Read-only live and installed-folder D2R diagnostics both returned `v0.3.30-windows-preview`. No third-party installers were executed and no tracked installation/startup setting was changed during validation.

## Version naming fix — 0.3.1

- User screenshot showed MVRH installed EXE version `1.0.36.0` compared against GitHub tag `hub-1.0.36`, falsely showing Update available.
- Numeric matching now recognizes alphabetic project prefixes, a leading v, trailing zero components and build metadata. Original version/tag strings remain visible. Preview/channel suffixes and ambiguous multi-version tags are not collapsed.
- Added regression cases for the reported mismatch, differing versions, build metadata, prerelease separation and ambiguous tags, including status, button eligibility and notification behavior.

## Portable installation UX repair — 0.3.2

- User reported a simple portable EXE installation becoming a confusing existing-install form with a nonfunctional Add App action. Explicitly requested UX review; delegated review and follow-up review to a UX design agent.
- Root cause: every normal return from UpdateRunner, including cancelled file review and declined installer launch, set installAttempted and switched to the existing-install completion form. Portable destination also expected an exact EXE while accepting a confusing folder field.
- Split new-install setup from existing tracking and installer completion. New EXEs explicitly choose portable save versus installer launch. Portable save uses folder plus filename, displays the exact final path, and pins validation/actions at the bottom. No detection/manual-version/ZIP picker fields appear in this route.
- Introduced typed update outcomes. Deferred operations stay retryable; portable file application directly completes the dialog and adds tracking. New EXE creation skips unnecessary overwrite review; existing targets still require review/backups.
- Recorded applied portable releases with visible provenance when EXE metadata is absent/different; only matching metadata enables automatic reconciliation. This occurs after actual file application, never just download.
- Added 30 portable-flow assertions covering actual mocked-download → filesystem save → dialog completion → persisted library, invalid folder retry, metadata absence, cancelled overwrite review/retry, declined installer, and intent/repository state isolation. Existing regression suite plus 56 feature and 17 wizard/tray assertions also pass. No downloaded third-party program was executed.
- UX follow-up inspected the revised renders and code, found no blocking portable-flow issue, and suggested one conditional installer/manual-download copy adjustment, which was applied.

## Existing App settings review — 2026-10-30

- User reported the confusing I'll update it myself option, duplicate existing-install/destination fields, and requested one well-named directory textbox after adding an app. Explicitly asked to send this to the UX designer.
- Delegated to the same UX design agent; review completed in `review/app-settings/REVIEW.md`. No implementation changes in this review turn.
- Recommendation: one App folder directory field, filename-only App file for portable EXEs, computed read-only target preview, clearer method labels (including Download only), version status summary and Advanced source exceptions without another directory textbox.
- Reviewer also identified JSON-clone status reset behind Manual version tracking beside enabled automatic reconciliation. Existing conflicting paths must be resolved explicitly per app; ordinary existing EXE paths can supply folder + filename automatically.

## Single-folder settings and comprehensive UX audit — 0.3.3

- User directed implementation after the existing-settings review, then requested a detailed review of every journey and explicit external benchmarking.
- Implemented App folder + filename-only App file, computed target preview, Download only naming, advanced source exceptions, accurate tracking status, one-time checks, stale-source state, unvalidated Back drafts and explicit legacy path conflict resolution. Save settings is covered by a real modal click test.
- Full UX audit identified three P1 findings: restore/version provenance mismatch, conflicting advanced-manifest EXE targets and silent numeric downgrades. All were fixed with regression coverage.
- Addressed additional findings: actionable missing-download states, disabled-action explanation, busy controls, chunk-cancellable ZIP staging, partial payload warnings, installed-folder access, global-preference draft/rollback, monitor/DPI bounds and core keyboard/accessibility labels.
- External comparison used official Obtainium, UniGetUI, PortableApps, Scoop and Microsoft WinGet documentation plus published official screenshot pixels. No competitor was installed/operated. Report distinguishes documented behavior, observed illustrations and our design inferences. One-folder design is our chosen simplification, not a claimed universal industry pattern.
- Detailed findings, sources and remaining follow-ups: `review/comprehensive-ux/REPORT.md`. Remaining work includes cancellable checking / stopping installer waits, corrupt-data recovery, tracking-only vs package downloading, live accessibility/scaling checks, typed operation phases, notification/storage refinements and friendlier remembered ZIP layout mapping. No new user decision is needed for this release.
- Final self-contained Windows x64 publish: zero warnings/errors. Packaged executable passed the original regression suite plus **66 feature, 17 wizard/tray, 30 portable installation and 29 App settings assertions**. Actual single-folder settings and main renders inspected. Delivered detailed source-linked review and 0.3.3 ZIP; runtime OS notification/installer/accessibility limitations remain explicit.

## Codeberg then GitLab — 0.4.0

- User requested implementation of the first two source additions only: Codeberg, followed by GitLab. Scope is public Codeberg.org and GitLab.com; no generic direct-download/HTML or private/self-hosted source features added.
- Implemented and tested Codeberg first, including API routing, stable/preview selection, draft exclusion, public asset transfer and source-aware persistence. A live read of codeberg.org/forgejo/forgejo returned v17.0.1.
- Added GitLab project routing with URL-encoded nested group paths, public release lists, future/upcoming exclusion, documented preview-tag heuristics, release link parsing, friendly-label filenames, direct asset URLs and unknown-size transfer. A live read of gitlab.com/inkscape/inkscape returned INKSCAPE_1_4 (its API release provides a source tarball, correctly not offered as a Windows app).
- Replaced GitHub-only transport with a shared release client, retaining old GitHub library keys. Provider-specific API backoff persists without blocking other hosts. Project/release links and UI labels use the detected source; portable saved-release provenance names its provider.
- Added HTTPS redirect checks and HTML/login response rejection for structured release asset downloads. Hash and size checks remain conditional on available source/HTTP metadata.
- Caught an overbroad source-text replacement before build and restored affected files from the last tested source snapshot, then reapplied exact edits. Regression checks validate the restored source plus new adapters.
- Added source adapter and wizard integration tests for routing, previews, subgroup encoding, source identity, size handling, public links, redirects, login pages, duplicates and rate-limit isolation. Final packaged results recorded at delivery.
- Final packaged build: zero warnings/errors; original suite plus 66 feature, 17 wizard/tray, 30 portable-installation, 29 App settings, 31 source and 11 source-wizard assertions passed. Packaged live metadata checks succeeded for GitHub, Codeberg and GitLab; updated first setup screen rendered and inspected. Source review: `review/sources/REVIEW.md`. Distribution uses one final `ReleaseDock-0.4.0` folder.

## Automatic installer completion — 0.4.1

- User asked to automate locating an app after installer execution, then clarified that a clear match must automatically add the app and close setup, not merely prefill the final form.
- Added read-only before/after current-user/machine installed-app registration snapshots across both registry views, conservative name/asset matching, changed-entry preference, duplicate collapse and existing-directory validation. Icon and uninstall metadata are parsed only; uninstall commands are never executed.
- A clear installed match with a usable app file/version source closes setup directly. Older numeric versions and incomplete registrations cannot falsely auto-complete. Ambiguous candidates get explicit choices; missing/delayed entries get Find installation and manual fallback.
- File version/manifest reading is preferred. Registry DisplayVersion is an explicitly labeled one-time fallback when file metadata is unavailable. Known update destinations are not silently redirected. Full registry snapshots remain transient and are discarded after library addition.
- Added install-discovery unit and real modal interaction tests with a simulated installer that registers a fake app. No third-party installer or Windows registry mutation is used by tests. The modal test verifies installer completion → detected folder/version → automatic dialog close → persisted library entry, without the final directory screen.

## 2026-10-31 — Windows download filtering, 0.4.2

- Fixed ZIP extension matching exposing Linux and macOS downloads in Windows installation and app settings. Default filtering now checks platform and architecture filename tokens, preserves unlabelled candidates, and ranks native architecture first.
- Added a per-app Show all downloads override, used consistently by future release matching. Existing incompatible selections require explicit override.
- Validation: self-contained Release build succeeded; packaged full regression suite passed, including 19 compatibility assertions for Airshipper, architecture, generic names, and future update selection.


## 2026-10-31 — App names and uninstall, 0.4.3

- User approved program names as primary labels with repository beneath, matching detail heading and editable App name in settings. Suggested from EXE product metadata/description, filename, then repository name. Custom names persist.
- User specified portable ZIP uninstall removes the app folder; portable EXE removes only the file. Implemented confirmed Recycle Bin removal, protected/common shared folder checks, and distinct Stop tracking. Registered installs use a unique exact-folder Windows uninstall registration, MSI /I converted to /X, with Installed apps fallback. Tracking remains until explicitly removed.
- Validation: packaged full regression suite passed, including 14 naming/uninstall checks and 31 settings checks. Main window rendered and visually inspected. Tests did not execute real uninstallers or remove real installed apps; recycle/uninstaller interaction requires manual testing.


## 2026-10-31 — GROUT rename, 0.4.4

- User selected GROUT (Git Release Observer & Update Tool) as the working name. Renamed visible branding, executable, product metadata, tray, settings, prompts and CLI help. Kept internal namespace, legacy data directory, startup registry value and single-instance mutex compatible with ReleaseDock.
- Packaged regression suite passed. Existing data is read in place; no library migration or user-data mutation was performed during testing.


## 2026-10-31 — Dotted title, 0.4.5
- User requested G.R.O.U.T. for the in-app title. Updated window title, header and assembly title; executable remains GROUT.exe. Publish succeeded.


## 2026-10-31 — Temporary update storage, 0.4.6
- User explicitly declined retaining the latest recovery backup. Completed updates now discard rollback backups as well as downloads and ZIP staging. Temporary copies protect in-progress rollback only; interrupted recovery is preserved. Pending/manual installation payloads remain available. Startup/idle persistence sweeps obsolete legacy data; locked files retry on later sweeps.
- Packaged regression suite passed, including 7 cleanup assertions covering completed/pending downloads, staging, completed/interrupted recovery and path containment.


## 2026-10-31 — Single EXE portable packaging, 0.4.7
- User requested eliminating adjacent DLL requirements. Added a repeatable win-x64 publish profile bundling native libraries and embedded debug symbols with compression. Output contains just GROUT.exe (~72 MiB), compared with ~165 MB plus DLLs previously. Native runtime components extract to the .NET runtime cache at launch.
- User raised download size: app-only framework-dependent comparison is 508565 bytes; runtime bundling accounts for the size difference. Installer/runtime prerequisite approach recommended for a future primary distribution; not implemented yet.
- Full packaged regression suite passed from an isolated folder containing only the EXE.


## 2026-10-31 — Missing installations, 0.4.8
- User observed external Gram uninstall was only exposed after manual recheck. Added presence detection during activation and each one-minute background tick for all located apps, including manually tracked versions.
- Prominent amber App not found state with Find installation / Stop tracking, historical version label, stale-success removal and disabled update/uninstall/version editing. Finder offers Windows installation matches and EXE/folder selection. Missing and unreadable metadata remain distinct; recovered locations clear the warning.
- Full single-EXE regression suite passed, including 10 new presence and UI assertions. Missing state rendered and visually inspected. Original private release remains unchanged.


## 2026-10-31 — Repeatable C# releases
- User requested publishing 0.4.8 and a release script, explicitly selecting .csx. Added scripts/release.csx, pinned local dotnet-script tool manifest and versioned release notes. Script supports help and strict flags, isolated single-EXE build/testing, checksums, clean-main/remote/version validation, push, draft upload, digest/size verification and publication without changing repository visibility. Help and unknown-flag failure validated; 0.4.8 is being published through this script.


## 2026-10-31 — App subfolders for new ZIP installations, 0.4.9
- User requested an app-named subfolder under the chosen ZIP install parent. New-install ZIP setup now labels the parent clearly, previews the computed child destination and uses it for extraction and future updates. Back navigation preserves the parent without appending another nested child. Existing app settings remain unchanged.
- Folder names sanitize invalid Windows characters and reserved names. Packaged full regression suite passed via release.csx in local-only mode, including 6 ZIP destination checks and actual extraction into the created subfolder.


## 2026-10-31 — Public repository and license
- User authorized changing sirkuttin/GROUT to public. Inspected tracked file inventory/history for credential patterns and app-library data; no credential patterns or tracked personal app data found. Historical README includes a sample local Windows username path. Added clear release/download and early-release instructions, then changed visibility and verified public status.
- Decision: user selected A, MIT license. Added LICENSE naming Kirk Sutton as copyright holder. Self-updater remains in progress and is not included in the existing public 0.4.8 release.


## 2026-10-31 — Public self-updater, 0.5.0
- Original task: use GROUT's own GitHub releases to update itself. Added independent daily/at-startup checks, manual Check GROUT updates, and confirmed Update and restart. Exact stable EXE and checksum assets are required; older/pre-release versions are excluded. Public API access needs no credentials now that repository visibility is public.
- Helper verifies the staged payload and parent process identity, checks write access before GROUT exits, waits for exit, atomically replaces the EXE and relaunches it. Temporary rollback protects replacement/immediate launch failures; successful restart cleans payloads. Failure details remain for repair. User asked to close the running instance: implemented for GROUT itself; external third-party apps are not forcibly closed.
- Public preparation: MIT license added per user decision; formatted license, embedded own/runtime notices, license viewer and release licenses attachment, public download guidance, SECURITY.md and enabled private vulnerability reporting. Prior release0.4.8 remains latest until a new release is explicitly published.
- Packaged test suite includes metadata/version selection, checksum/corruption and replacement/rollback assertions plus a real helper handoff/restart smoke test against isolated executable copies.


- Final verification: packaged suite passed with 22 self-update assertions, including real restart. Anonymous public check reported no newer stable release than development0.5.0; public repository visibility and enabled private vulnerability reporting verified. License/notices embedded and release-script licensing attachment generated. No 0.5.0 GitHub release was published in this task.


## 2026-10-31 — Local artifact cleanup and 0.5.0 publication
- User requested cleanup now that source is on GitHub. Old packages/source ZIPs, scratch builds and build intermediates totaled roughly4.8GB. Permanent deletion was blocked by automatic approval; moved54 verified untracked workspace items to Recycle Bin instead. Kept current0.5.0 verified outputs, review notes/screenshots and the small NuGet-config backup under ignored review/local-maintenance.
- Release script now removes only the temporary EXE/report created by a successful invocation and its empty directory, without recursive deletion. Failed builds remain for diagnosis. Local full suite passed and cleanup verified.
- User asked why0.5.0 was not published; proceeding with publication through the release script.


- Publication initially encountered the user's running local0.5.0 EXE locking the artifact path. Updated the release script to select a separate commit-named build subfolder when the existing output is locked, preserving the running app.


## 2026-10-31 — README and documentation refresh
- User explicitly requested a documentation agent to improve the long README. Agent rewrote the front page from3251 to407 words and moved details into docs/user-guide.md, supported-sources.md, troubleshooting.md, development.md and releasing.md.
- Added three current0.5.0 UI screenshots using sample data, about115KB total. Corrected stale backup, AppData, UI-label and manual-versus-automatic version-reading instructions. Reviewed screenshots and all guides;34 local file/image/anchor links passed validation and git diff --check passed. Documentation-only change; no new app release needed.


## 2026-11-01 — 0.5.1: stable executable, icon and red-team fixes

- User rejected stale versioned filenames. Primary download is now GROUT.exe; an identical versioned compatibility asset keeps0.5.0 updaters functional. New updater prefers the stable asset and migrates known versioned filenames, then repairs matching user startup/Desktop/StartMenu/UserPinned shortcut references after healthy startup. Conflicting GROUT.exe destinations stop before closing the old app. Custom names stay unchanged.
- User explicitly requested a red-team review and0.5.1 publication. Three reviewers confirmed12 actionable issues across self-update, release tagging, cleanup/uninstall and download/archive handling. All confirmed findings were fixed with regressions; detailed reports remain under ignored review/red-team/. Normal/failed startup and filename migration use real isolated executable copies in tests. No personal apps, startup entries or desktop shortcuts were modified by testing.
- Self-updates now retain rollback until a post-window-load health acknowledgment, then commit before repointing references. Cleanup protects tracked installation/version paths; uninstall requires app identity and rejects junction ancestors; MSI attached-quote commands normalize correctly. ZIP extraction validates bytes+CRC and rejects concurrent new destination overwrites; downloads stop known-size overruns; invalid/non-PE portable EXEs are rejected before replacement.
- Added original vector G icon, multi-size ICO/PNG, repeatable C# icon generator, executable/window/tray integration and README branding. Generator help/generation checked; release build has no warnings/errors.
- Full single-EXE packaged suite passed:48 self-update,6 ZIP folder,10 missing-installation,12 cleanup,26 identity/uninstall,19 compatibility,80 feature,17 wizard/tray,30 portable,31 settings,31 source,11 source-wizard and19 discovery assertions, plus original regression suite. Release script separately validates tag identities and both primary/compatibility output locks, and removes redundant local compatibility copies after verified publication.

## 2026-11-01 — 0.5.2: naming correction and editable repository URLs

- User reported coder/coder labeled Multiverse VR Hub after a ZIP install. Read-only inspection confirmed Coder's actual EXE metadata and install/version were correct but a wrong CustomName override had been saved. Their running UI already showed Coder when inspected, so no user data was changed.
- Fixed suggested names becoming persistent overrides on save; automatic suggestions refresh with location edits, explicit custom names remain intact, and Use detected name clears old overrides. Folder suggestions require corroborated file/metadata identity instead of selecting a lone unrelated EXE in a shared parent. Fresh-install switching clears stale folder/file/source fields.
- User requested the full source URL in the selected-app view and editable in App settings, including provider moves. Added clickable full URL and verified source edits, duplicate checks, and clearing old-source release/asset/cache state while preserving install paths/version. Failed or cancelled edits leave original entry unchanged. Reselect new-source download after migration.
- Packaged full suite passed, including41 settings assertions and16 source-wizard assertions with real modal save and duplicate/provider-move coverage. Updated sample-data screenshots and user guide. Publishing0.5.2 via the release script.

## 2026-11-01 — 0.5.3: uninstall completion and missing-app confirmation

- User reported Coder remained tracked after GROUT uninstalled its ZIP folder. Reviewed code: old flow explicitly kept entries and required Stop tracking, a UX outcome not covered by the earlier security-focused review. Changed successful portable removals to remove and persist the actual visible library item immediately.
- Added persisted uninstall-pending state, Windows registration/known-EXE confirmation, checks on activation/background tick, and Keep tracking for cancellation/unverified outcomes. Registry-access errors are not treated as confirmation. Save failures restore the pending entry for retry. Uninstalling does not permit update/version/settings operations.
- User flagged missing-app Stop tracking text. Added missing/present/pending-specific prompts and removed misleading backup claims. Missing entries only describe removing the missing library entry.
- Packaged suite passed with42 identity/uninstall assertions, including actual MainWindow list removal and persisted library removal, restart/persistence/cancel/failure guards and prompt wording. No real apps were uninstalled in testing. Older orphaned entries remain available for deliberate Stop tracking; externally removed apps retain Find installation/Stop tracking.

## 2026-11-01 — 0.5.4: direct extraction and overwrite, internal rollback only

- User explicitly rejected file-conflict review: files must be overwritten directly, and temporary rollback must be internal with no user-facing screen. Removed operational ZIP and portable-EXE overwrite reviews for both fresh installs and updates. ZIP plans apply every archive file, including existing settings/save files; absent files remain unchanged.
- Removed the review dialog/preview command and backup/apply button wording. Updated setup/settings text and documentation to describe direct extraction/overwrite. Archive validation, safe paths, atomic writes and transient rollback on failures remain internal. No completed rollback copies are retained.
- Packaged suite passed:17 ZIP-folder assertions cover new, conflicting first install, existing update, settings overwrite, absent-file preservation and scratch cleanup with review hooks that fail if invoked. Portable overwrite regression completes with no review and no retained backup. No real user app files changed during tests.

## 2026-11-01 — Documentation and screenshot refresh

- User requested a documentation agent to audit the documentation and stale screenshots after the footer/layout changes. Audited README, all six docs guides and SECURITY.md against current code; historical release notes remain historical.
- Updated current interface caption, direct portable/ZIP overwrite explanations, silent temporary rollback, bottom-right self-update controls, removed polling timestamp claims and obsolete --preview-files diagnostic, and refreshed publishing examples using an explicitly illustrative next version.
- Regenerated library, Add App and App settings previews; added portable EXE and missing-installation images to the user guide. Main-window previews include compact footer and narrower side margins. Previews use isolated sample data; portable setup now uses neutral C:\Apps rather than the current user's profile, and App settings uses a consistent DesktopApp sample.
- Release build passed without warnings/errors; all five preview commands completed successfully, screenshots were visually inspected, 37 local documentation file/image references resolved, and git diff --check passed. No release published or user library changed.

## 0.5.5 release
- Approved compact main-window footer and margins; removed polling timestamp line without changing scheduling.
- Refreshed documentation/screenshots and preview sample data. Full packaged regression suite and documentation links verified.


## Footer message ownership — user direction, 2026-11-01
- Footer is a fallback for useful messages without a better UI home. Update-check feedback belongs by its controls; app-specific feedback belongs with its app. Revised review/DECISIONS.md around routing before wording; implementation choices remain open.

## 0.5.6 release
- Header owns check progress/counts; app entries own failed-check warnings. Same-row footer, no generic added confirmation, inline link failures, missing-folder disablement and download-only file access.
- Packaged tests cover sidebar warning state and stable footer layout. Broader footer routing remains planned, not implemented.

## 0.5.7 release
- Right-aligned sidebar statuses; removed routine success messages and suppressed persisted legacy success text. Empty selected-app message area collapses; real issues remain visible.
- Full packaged regression suite includes success suppression and issue preservation checks.

## 0.5.8 release
- GROUT check and update buttons are mutually exclusive. Available updates show Update GROUT; restart behavior is unchanged.
- Updated current documentation and screenshots; full packaged suite required for publication.
