# Windows accessibility and layout validation

This checklist completes GROUT-009. Automated regressions cover navigation handlers, accessible names and simulated window layout. The real Windows interactions below are **not yet verified**; passing model tests does not substitute for them.

Use an isolated sample library where possible. GROUT's `--tree-drag-test-host` starts an isolated sample window without loading or saving your actual library. Launch without this flag for normal use; do not use real apps for destructive validation.

## Keyboard

1. Tab through the header, search, filter, library, detail controls and footer. Confirm visible focus, sensible order, no traps, and Shift+Tab in reverse.
2. In the library use Up/Down to select; Right to expand and enter the first child; Left to return to the parent and collapse. Confirm keyboard focus follows selection.
3. Check group header toggling, keyboard access to overflow actions, row selection, first-outside-click menu dismissal and sidebar-divider resizing. Search and Updates only are temporarily hidden in the current build; filtered navigation remains covered by isolated tests for future use.
4. Open Settings and a setup dialog using the keyboard. Confirm fields/options are named and reachable, Cancel/Escape behaves correctly, and no unrequested operation starts.
5. In an isolated normal library, enter a manual version draft, move focus, and check that unrelated refreshes do not discard it. Explicit Save and Mark latest should replace it deliberately.
6. Against isolated damaged configuration, navigate Open data folder / Retry loading / Exit. Correct the file separately; retry and check focus reaches the restored library. Never damage real configuration for this test.

## Screen reader

Use Windows Narrator or another installed reader only in a deliberately started test session. Record reader/version and do not claim results for other readers.

- Confirm library rows announce the actual item name, branch expansion and relevant status rather than a class name or glyph alone.
- Confirm search, version input, repository/destination fields, option values and recovery actions have understandable names.
- Check errors can be discovered beside the action and current focus remains understandable after navigation, retry and dialog closure.
- Record missing announcements separately from visual correctness. Progress should not become a continuous stream of distracting speech.

## Display scaling

On a test machine/VM, check 100%, 125%, 150% and 200% scaling at the available screen resolutions. Do not change another person's display settings just to execute this checklist.

- Verify Check now/Cancel checks, Add, Settings, GROUT update and dialog completion actions stay reachable.
- Check long names, nested groups, long version/error text, recovery file paths and the narrowest supported window. Confirm text wraps/trims with access to full details.
- Verify scrolling reaches setup/settings/recovery actions when height is constrained.
- If multiple monitors are available, move the window between different scale factors and confirm bounds and focus remain usable.

## Record results

For each case record build, Windows version, reader (if used), resolution/scale, steps, observed result and Pass/Fail/Not tested. Keep automated simulation, actual input and reader observations separate. A failed case needs a reproducible issue; an untested case remains an explicit release limitation.
