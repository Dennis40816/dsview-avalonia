<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Implementation and test boundaries

New features should introduce an identifiable owner for their state, rather than
add another independent workflow to the main window. UI wiring, editable state,
sample-domain operations and filesystem/device work have different lifetimes.
Keep those responsibilities separate. Moving members to another partial file
does not create a new boundary.

## Appearance example

| Owner | Responsibility |
| --- | --- |
| `AppearanceSettingsViewModel` | Saved preferences, optional editing draft, commands and reported save failure |
| `SettingsView` | Compiled bindings and presentation; no save or JSON logic |
| `AppearanceResources` | UI-thread projection into font resources |
| `AppearancePreview` | Draw a noninteractive preview from immutable preferences |
| `DisplayPreferences` | One default definition, validation and strict document admission |
| `LocalAppearanceStore` | Fixed local-data path, bounded read, staged replacement and typed IO results |
| `WorkbenchShell` | Sidebar layout mode and navigation controls; no acquisition state |

The view model derives open/saving status from the draft and async command. Its
controls bind to that owner; the view does not read controls back to build a
second settings snapshot. Cancel restores the loaded preferences. Save adopts
the immutable draft only after storage succeeds. Repeated input cannot edit or
close a pending save. New settings commands use the existing public
CommunityToolkit.Mvvm dependency.

The filesystem adapter is the only new appearance component that reads the
environment or files. Its root resolver is injectable internally so checks can
use an isolated workspace. Invalid or unavailable roots fail closed. No capture
file, decoder or user-entered path chooses the appearance destination.

## Checks that protect behavior

`DecoderDraft` owns role/parameter editing. `DecoderViewModel` owns the shared
I²C/SPI/UART workflow: commands, source identity, cancellation and generation. Its injected worker
delegate is an asynchronous IO boundary, not a second capture owner. Workbench
alone commits validated results and preserves measurements. The window only
opens the panel and cancels/drains work at file and shutdown boundaries. New
decode controls use compiled bindings and share the existing inspector area.
Controlled completions test stale output, cancel, retry, disposal, pending edits
and shutdown without sleeps. Optional real worker checks cover imported data
through decode and persistence, separately from native UI acceptance.

Use independent fixtures and public/internal application contracts. Do not
compare implementation source, assert private members, or copy the algorithm
under test into its expected result. Cases should report one behavior and have
independent state. A controllable completion signal tests in-flight saving;
fixed sleeps and elapsed-time assertions are unnecessary here.

The appearance checks cover external document rejection, draft cancellation,
defaults, successful publication, failure recovery and in-flight edit guards.
Storage checks additionally use real files in the shared `TestWorkspace` fixture
to verify a fresh store can reload saved preferences, malformed originals stay
unchanged, invalid writes preserve earlier bytes, and failed replacement cleans
its staged file. They never open the user's settings directory.

The reload check caught a computed validation property being serialized as an
unknown field. Excluding that property fixed the actual failure; the expected
document was not relaxed to accommodate it.

For performance regressions, prefer same-run workload scaling, calibrated
comparisons or allocation/work counts. Report real latency distributions on
identified hardware separately. A timeout that prevents a hung check is not a
performance assertion. Synthetic rendering is not evidence of real acquisition.

## Existing work and scope limits

The preexisting main window, Workbench and check runner still contain large
methods and mixed responsibilities. New appearance code does not add a
MainWindow partial or a new save lifecycle there. Existing UI controls moved to
the shell owner, but this is not a complete MVVM migration or repository-wide
conformance claim. Next extractions should own a whole workflow, beginning with
capture lifecycle. Decoder configuration now uses the shared workflow. Keep their behavioral evidence
before changing storage or cancellation rules.

The current check runner remains a console runner. The shared fixture is local
to that test project; older ad-hoc fixture code has not yet been migrated. A
repository-wide structural ratchet, analyzer policy and headless UI fixture are
still work to adopt. Native layout, focus and pointer routing need separate
verification; passing model/storage checks does not certify them.

No restricted Core source, test helper or theme is copied or referenced by this
change. Reuse of a component requires an approved, pinned distribution with
appropriate terms; following design principles does not imply package access.
