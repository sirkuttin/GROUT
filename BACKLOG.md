# GROUT backlog

Updated 2026-11-09. Product direction and near-term priorities approved by the owner. See [roadmap](ROADMAP.md).

This is the detailed task source of truth; TRACKER.md records history. IDs remain stable when priorities change. Next and Later entries are candidates, not feature commitments. Implemented changes await 0.11.0 publication. Remaining live GROUT-009 validation is deferred as an explicit limitation by the owner.

Statuses: Planned, In progress, Needs evidence, Implemented/unreleased, Shipped, Blocked, Deferred, Proposed. Shipped requires a release version/link. Start one cohesive outcome at a time; keep at most two implementation items active.

## Implemented, awaiting release

### GROUT-001

**Status:** Implemented/unreleased

Damaged installation records could silently restore stale publisher versions and be deleted; preserve release truth and evidence

**Acceptance criteria**

Malformed/null/invalid/missing referenced records retain last saved release and explicit issue; ordinary save preserves unreadable referenced bytes; deliberate removal cleans records; confirmed application can replace them; null library/settings fail closed

**Dependencies and evidence**

Reliability review reproduced against shipped EXE; new isolated regressions pass

### GROUT-002

**Status:** Implemented/unreleased

Background rendering could discard manual-version drafts; keep edits until a deliberate action

**Acceptance criteria**

Dirty draft survives unrelated render/model change; Save trims/commits; Mark latest explicitly replaces draft; selecting another item resets display; no cross-item draft leakage

**Dependencies and evidence**

UX source finding plus new packaged draft regressions

### GROUT-003

**Status:** Implemented/unreleased

Filtered rows appeared beneath a collapsed arrow and summary; make visible structure truthful

**Acceptance criteria**

Search/Updates-only reveal matching descendants with open glyph, hide collapsed-only summaries, explain unavailable collapse during filtering, restore saved state when filter clears; accessible row/action names identify items

**Dependencies and evidence**

Reproduced model state; new hierarchy regressions; live assistive technology check remains GROUT-009

### GROUT-004

**Status:** Implemented/unreleased

Recheck and Find installation feedback appeared far away in footer; keep action feedback local

**Acceptance criteria**

Failed one-time read preserves version/manual mode, appears beside Recheck and survives refresh; successful retry/manual confirmation clears it; no leakage to other item; no routine success footer for read/location changes

**Dependencies and evidence**

Parent completed routing with four isolated regression cases; broader routing GROUT-008

### GROUT-019

**Status:** Implemented/unreleased

Group headers should act as folders rather than opening a group detail page. Click anywhere on the header to toggle; right-click and the named overflow button offer the same node-targeted actions. Tracked apps with children retain their app detail behavior.

**Acceptance criteria**

Header clicks on text/blank space toggle without selecting the group; dragging or releasing on a different row does not toggle; no group detail page; shared context/overflow actions target the clicked node; filtered forced-open state remains consistent. Fifteen regression assertions and the complete packaged suite passed on 2026-11-09. Actual mouse interaction was not separately exercised with computer control. Build: outputs/group-headers/GROUT.exe.

**Dependencies and evidence**

Explicit owner request and screenshot, 2026-11-09. Builds on user-managed hierarchy and existing drag/drop logic.

## Now: dependable operations
### GROUT-022

**Status:** Implemented/unreleased

Owner requested hiding search and Updates only on 2026-11-09. Their entire container is collapsed, including margin, so list and details reclaim the row. Controls are not keyboard-reachable and startup remains unfiltered; underlying filter code/tests are retained for future reconsideration. Layout regression verifies zero reserved row height. Included in 0.11.0 release candidate.

### GROUT-020

**Status:** Implemented/unreleased

Let users choose what closing the window does. Settings offers Keep running in the tray (default) or Exit GROUT. The preference persists, old configurations retain tray behavior, and active operations/open dialogs block unsafe exit. Eight isolated lifecycle/persistence assertions pass; test shutdown is injected so the test runner is not terminated.

### GROUT-021

**Status:** Implemented/unreleased

Sidebar and header polish requested 2026-11-09: first outside click dismisses menus and consumes both down/up; overflow appears only for row hover, keyboard focus or its open menu; sidebar has a draggable/keyboard resize divider with saved width; app titles span the top row with status below; header actions/counts share a right edge; main branding is G.R.O.U.T. / Filling the gaps in app updates.

Twenty-five isolated assertions cover dismissal gesture state, routed header clicks, actual visual-tree barrier hit testing, default overflow visibility/style rules, row geometry, header edges, splitter presence and width restoration/persistence. User reproduced two timing-based dismissal failures; those filters were replaced by a transparent topmost click shield that remains until the outside gesture ends. Tests verify header coverage, retention between down/up, removal after release and normal next-click behavior. Native physical click-through remains a live UI check. Latest build: outputs/menu-shield-fix/GROUT.exe. All prior fixes are included.

Approved order: GROUT-005, GROUT-006, GROUT-007, GROUT-008. GROUT-009 validates affected flows throughout this work.

### GROUT-005

**Status:** Implemented/unreleased

Implemented 2026-11-08. Check now becomes Cancel checks during a batch. Provider requests and inter-item delays accept cancellation; completed results are preserved, unattempted items remain unchanged, partial checks do not advance LastSuccess or clear retry policy. Full packaged regressions passed, including 18 cancellation assertions using simulated HTTP and hidden sample windows. No live provider or computer-control session was used. Test build: outputs/backlog-005/GROUT.exe.

A release-check batch prevents leaving while waiting on network; allow cancelling read-only checks

**Acceptance criteria**

Cancel stops new requests and cancels cancellable active request; checked items retain valid results; unattempted/cancelled items are not falsely marked failed/current; scheduler and buttons return to coherent state; cancellation is not announced as successful full check

**Dependencies and evidence**

Existing long-standing review; preserve source backoffs; specify explicit operation phases with GROUT-006

### GROUT-006

**Status:** Implemented/unreleased

Implemented 2026-11-08. Stop waiting detaches from the installer without cancellation/termination, preserves the package and unconfirmed state across restart, and cannot create a release receipt on a late exit. Available in updates and first-install setup. Twelve regression assertions cover simulated completion/failure, persistence/cleanup and a real hidden benign process remaining alive after detachment; no third-party installer was run.

An external installer can keep GROUT waiting indefinitely; let user stop the wait safely

**Acceptance criteria**

Stop waiting returns control without terminating installer; wording says installer continues; no successful-release receipt without completion evidence; pending state/payload remain usable for later reconciliation; failed/cancelled/late completion cases covered

**Dependencies and evidence**

Reliability inspection, not live installer reproduction; requires operation-state design and isolated process tests

### GROUT-007

**Status:** Implemented/unreleased

Implemented 2026-11-08. Startup recovery identifies the exact file, explains that files are unchanged, offers Open data folder / Retry loading / Exit, and restores normal startup only after complete validation. Saving and update/tray initialization are paused on invalid data. Twenty isolated assertions cover malformed/null library, invalid source/cycle, settings errors, unchanged receipts/configuration, retry after correction and action routing. Import/replacement automation remains the separate GROUT-018 candidate.

Damaged library/settings currently leave an inaccessible technical startup experience; provide a safe recovery route

**Acceptance criteria**

Screen identifies affected file and problem, can open data folder and retry after correction; optional validated import/replacement never silently overwrites originals; absent files differ from damaged files; no empty-library save on error; receipt errors remain per-item

**Dependencies and evidence**

GROUT-001 fail-closed foundation; recovery copy policy concerns configuration only, never retained app payload backups

### GROUT-008

**Status:** Implemented/unreleased

Implemented 2026-11-08. App operation progress/errors stay in app details; opening folder/download failures use local action feedback; recovery/uninstall failures stay with the app; GROUT self-update feedback stays beside its own button. Routine uninstall/settings success footer text removed. Shared save failures remain global. Seven new routing assertions and the updated visible-uninstall regression pass; transient progress is not persisted. Self-update text is width-limited to protect footer layout.

Some remaining app/action messages still use distant footer; complete feedback ownership

**Acceptance criteria**

Inventory remaining emitters; app operation errors attach to selected item/action; persistence failures stay global; routine success messages suppressed; messages clear/retry predictably; footer no longer shifts layout

**Dependencies and evidence**

GROUT-004 is subset complete; inspect remaining folder/download/uninstall paths before changing behavior

### GROUT-009

**Status:** Deferred (remaining live validation)

Owner chose on 2026-11-09 to keep unchecked Windows cases documented while preparing the release. Computer control stays off. Automated/implemented portions remain in the release candidate; this deferral does not claim the live checklist passed.

Implemented Left/Right branch navigation with parent/first-visible-child selection, focus-following logic, filter-aware state, descriptive row/state names, explicit choice/organization/completion field labels and nonredundant tab stops. Thirty-two automated assertions pass for navigation handlers, labels and essential-control bounds at simulated 100/125/150/200% scale on a 1920x1040 work area. Actual keyboard, assistive-technology and display/multiple-monitor validation remain unverified. No computer-control session was used. The remaining procedure is in [Windows validation](docs/windows-validation.md); this task is not marked complete from model/layout tests alone. Latest build: outputs/backlog-009/GROUT.exe.

Accessibility/scaling behavior is not established by model tests; make core paths usable and document limits

**Acceptance criteria**

Verify keyboard selection/search/expand/settings/version edit/recovery with visible focus and no traps; screen reader identifies items/state/actions; 100/125/150/200% scaling has no clipped essential controls; record actual vs simulated evidence and any failures

**Dependencies and evidence**

GROUT-003 naming is first step only; user-approved targeted Windows interaction is needed, default automated work remains without visible UI

## Next: candidates to validate and refine

Plan GROUT-016 alongside Now; execution needs an agreed isolated environment. Use findings to refine the order of the other candidates.

### GROUT-010

**Status:** Proposed

Notification click only opens existing view, possibly hiding announced updates; take user to relevant work

**Acceptance criteria**

Single-item notification reveals/selects item and necessary ancestors; multiple-item notification opens Updates; conflicting search cleared or explained; GROUT self-update points to its own control; no false selection when item was removed

**Dependencies and evidence**

UX source inspection; test routing with injectable action, then explicitly targeted Windows notification check

### GROUT-011

**Status:** Proposed

Confirmed need: copy selected nested ZIP contents into an exact existing destination; proposed improvement: make that mapping immediately understandable

**Acceptance criteria**

User sees selected archive folder/file and exact resulting destination examples; folder contents vs extra nesting is unambiguous; a read-only summary explains future updates; remembered selection resolves uniquely or stops before writes; no conflict-review screen or repeated per-update selection; validate with Reimagined task

**Dependencies and evidence**

Existing selector already shipped; competitive study GROUT-016 tests whether presentation saves effort; Scoop partial extraction already documented, so visual convenience rather than exclusive capability is the hypothesis

### GROUT-012

**Status:** Needs evidence

Some installers still require locating the result manually; improve detection only where observed

**Acceptance criteria**

Gather installer cases with source/installer type and observed destination evidence; unique confident match completes automatically; ambiguous match asks; no unrelated install claimed; receipt requires completion evidence

**Dependencies and evidence**

Installer discovery already exists; evaluate residual cases, do not backlog already shipped automation as new work; GROUT-006

### GROUT-016

**Status:** Needs evidence

Assumption: visually configuring direct releases into existing custom paths is easier than the nearest alternatives; test before investing in differentiation claims

**Acceptance criteria**

Run identical registered-installer, portable-EXE replacement and nested-ZIP-folder-content tasks in GROUT and Updainium; compare Scoop using available package/recipe and a separate authoring case. Use isolated machine/VM or agreed sandbox; record versions, steps, setup/recipe time separately from repeated-update time, prompts, exact resulting file tree, persisted version, failure/retry behavior and limitations. Do not score an untested/unsupported case as impossible. Publish results with raw observations, no winner predetermined

**Dependencies and evidence**

Source comparison only so far; GROUT-005/006/007 underpin fair failure/recovery checks; no real managed apps or user's machine configuration changed merely for benchmarking

### GROUT-017

**Status:** Proposed

Confirmed need: track what GROUT actually applied despite stale publisher metadata; proposed improvement: make evidence understandable

**Acceptance criteria**

Selected item distinguishes applied release, detected file/manifest version and manual confirmation in plain language when relevant; explain a mismatch and next action without exposing internal receipt jargon; never equate saved receipt with complete-file verification; missing/unreadable evidence preserves truth and appropriate warning; normal current state stays quiet

**Dependencies and evidence**

GROUT-001 and existing applied-release records; Obtainium also documents source/installed mismatches, so perfect detection is not a promise

### GROUT-018

**Status:** Proposed

Proposed improvement: move or recover a growing library through validated export/import

**Acceptance criteria**

Export includes versioned schema, source links, user names/hierarchy/order, update mappings and appropriate settings; excludes API secrets, pending payloads, caches and runtime-only state. Import previews counts/conflicts, validates IDs/parents/source/path/mapping/record references and handles older supported formats; cancellation/malformed input leaves existing data unchanged; user deliberately chooses merge/replace; no installations or app-file writes occur; another machine's missing paths are marked unresolved and imported records do not prove files are installed. Include necessary applied-release evidence or clearly label what cannot transfer; roundtrip tests preserve supported fields

**Dependencies and evidence**

GROUT-007 shares validation/recovery foundation; Updainium/Obtainium configuration transfer is documented, not a unique GROUT feature; configuration transfer is distinct from retained installation backups

## Later: gated or demand-led

### GROUT-013

**Status:** Blocked

Nexus integration cannot be publicly enabled yet; reach readiness before activation

**Acceptance criteria**

Registration/policy prerequisites satisfied; account/download paths and failure recovery verified; public credentials/storage behavior documented; free/Premium limitations clear

**Dependencies and evidence**

External registration dependency; no date promise

### GROUT-014

**Status:** Deferred

D2RLoader search exists but catalog UI was deliberately hidden; reconsider only if it helps users

**Acceptance criteria**

Owner chooses to reopen discovery; design fits Add/parent/destination flows without new workspace confusion; catalog errors/paging/search tested; direct URL behavior remains

**Dependencies and evidence**

Explicit owner decision; keep hidden until then

### GROUT-015

**Status:** Needs evidence

Current website matching excludes some real download patterns; expand only from concrete demand

**Acceptance criteria**

Collect unsupported page+download examples; classify static/latest/JS/auth cases; accept a narrowly testable pattern or explain unsupported state; retain safe URL/download validation and visible failure

**Dependencies and evidence**

Existing static versioned links already work; no general web scraping engine commitment

## Updating this backlog

- Record the trigger, user consequence and reproduction evidence before implementing a bug fix.
- Define acceptance criteria before coding. Prefer headless isolated regressions; distinguish them from actual Windows interaction and record unverified limits.
- Update status after verification; move to Shipped only when publication completes and add its release link.
- Keep completed IDs for traceability. Review priorities after each release and remove superseded assumptions.
- Do not create a second independently maintained task definition in GitHub issues. If issues become the detailed source of truth, replace these entries with links and summaries.
