# GROUT roadmap

Updated 2026-11-09. Direction approved by the product owner; no delivery dates are promised.

## Purpose and audience

Keep useful Windows apps and mods from independent release sources organized and current, especially when their publishers do not provide an updater.

Build first for people with workflows like the owner's: direct release links, portable files, selected ZIP contents and custom destinations. Broad Windows software maintenance and full game-mod management are not the current focus.

Our working promise is **upstream releases into the right existing files and folders, without writing package recipes**. Competitor research shows substantial overlap; a hands-on comparison must establish any usability advantage. This audience choice is a focus, not evidence of market demand.

## Now: dependable operations

The next milestone makes core operations safe to leave, retry and recover from.

| Order | Outcome | Backlog |
| --- | --- | --- |
| Release preparation | Preserve corruption evidence, version drafts and truthful filtered-tree states; keep version feedback local. Shipped in 0.11.0. | GROUT-001–004 |
| 1 | Cancel release checks without inventing failures or successful completion. Shipped in 0.11.0. | GROUT-005 |
| 2 | Stop waiting for an external installer without killing it or claiming success. Shipped in 0.11.0. | GROUT-006 |
| 3 | Recover from damaged configuration with clear file details and a safe retry route. Shipped in 0.11.0. | GROUT-007 |
| 4 | Finish placing app/action feedback near its owner; keep shared save failures global. Shipped in 0.11.0. | GROUT-008 |
| Throughout | Validate keyboard access, understandable item/state names and scaling; document what was actually tested. | GROUT-009 |

Done when each item's acceptance criteria pass, interrupted/failed operations preserve truthful state, and remaining Windows validation limits are explicit. This roadmap does not authorize publishing a release.

[Release 0.11.0](https://github.com/sirkuttin/GROUT/releases/tag/v0.11.0) includes implemented Now work and owner-requested sidebar/menu/close behavior polish (GROUT-019–022). Search and Updates only are hidden. The owner chose to defer remaining live Windows validation as an explicit limitation; computer control stays off. Published 2026-11-09.

## Next: prove and simplify custom installation

These are candidates, not committed features. Plan the comparison alongside Now; run it in an agreed isolated environment.

| Candidate | Outcome | Backlog |
| --- | --- | --- |
| Same-task comparison | Compare installer, portable EXE and nested ZIP workflows with Updainium and Scoop; separate setup/recipe effort from repeat updates. | GROUT-016 |
| Clearer ZIP mapping | Show exactly which archive contents go to which destination, without accidental extra nesting. | GROUT-011 |
| Clearer version evidence | Explain applied, detected and manually confirmed versions only when useful. | GROUT-017 |
| Validated export/import | Move or recover library configuration without secrets, installation writes or false installed-state claims. | GROUT-018 |
| Notification navigation | Open the relevant item or update list when a notification is clicked. | GROUT-010 |
| Targeted installer discovery | Improve only the residual cases that evidence shows existing discovery misses. | GROUT-012 |

Use task observations and user feedback to refine this order; do not assume competitors lack a feature merely because documentation did not establish it.

## Later: selective expansion

- Nexus: blocked on registration and integration readiness (GROUT-013).
- D2RLoader browsing: stays hidden until deliberately reconsidered (GROUT-014).
- Additional website patterns: require concrete unsupported examples and a maintainable scope (GROUT-015).

## Boundaries

- One portable EXE under 1,000,000 bytes, using Windows Framework; keep persistent data small and clean completed temporary files.
- One user-managed library for apps and mods. Reorganizing entries never moves installed files.
- Explicit update destinations, direct payload overwrites, silent transient rollback and no retained installation backups or conflict-review screens.
- Record success only with installation evidence. Preserve last known versions when evidence cannot be read.
- Quiet normal states; actionable failures close to their controls.
- No automatic move into load-order management, dependency resolution, cloud accounts or private/self-hosted sources without a separate demonstrated need.

## How we maintain the plan

[BACKLOG.md](BACKLOG.md) owns detailed tasks, acceptance criteria, dependencies and status. [TRACKER.md](TRACKER.md) records decisions and history. Review priorities after each release; distinguish implemented/unreleased from shipped and proposals from commitments.

Success means representative add/update/restart and failure/retry flows remain understandable and truthful, while the size and cleanup constraints hold. Use isolated tests and direct feedback first; telemetry is not a prerequisite.
