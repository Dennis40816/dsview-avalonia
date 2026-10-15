<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Workbench layout and display settings

Groups live in the left sidebar, with one active Group. Activity, kept results
and capture issues share that context area. File, capture source and Settings
remain in the global header. Waveforms/Packets and time navigation belong to
the analysis area. Previous view and Fit use named icons; edge navigation opens
a local popover. Zoom is primarily wheel-at-pointer, with Shift+wheel for rows.

Below 1280 DIPs the Group sidebar starts collapsed and can open as a 240-DIP
drawer over the work area. Selecting a Group or Close Groups dismisses it.
The minimum window width remains 1024 DIPs. The sidebar must not compress the
time axis when opened in this compact form. The inspector stays contextual.

Settings is a full work-area page, separate from the current analysis. It offers
interface text size (12–16), data text size (12–18), waveform thickness (1–3),
and reverse wheel zoom. Changes preview immediately, including a sample trace.
Apply persists them; Cancel, Back or Escape restores the previous preferences.
Restore defaults changes the draft until Apply. While a save is pending,
editing and cancellation are disabled. Other analysis controls are
disabled while this page is open. Display settings do not alter sample timing,
physical probe colors, saved analysis content or acquisition enablement.

Preferences are stored in the user's local application data directory under
`DSView2/appearance.json`, separate from workbench Groups/colors. Missing files
use defaults. Invalid/unreadable preferences are not rewritten during load;
Settings reports the fallback. Writes validate first and replace the destination
from a temporary file in the same directory. Failed writes keep the prior saved
preferences and leave the draft available for Retry or Cancel.

## Validation boundary

The revised candidate has a successful solution build and 24 focused
view-model, document-admission and isolated filesystem checks. Native visual and interaction acceptance is still required. In
particular, verify 1024/1380/wide layouts at 100/125/150/200% display scaling,
maximum text sizes, Group drawer/focus recovery, Settings Apply/Cancel/reopen,
long signal names, empty states, packet inspector and repeated edge navigation.
Do not count compilation or the visual preview as a usability pass.

The larger A/B initial-placement redesign, channel editor, color-picker redesign
and hardware capture controls remain open. The current draggable cursors still
use the existing two-point initial placement and explicit edge/position modes.
