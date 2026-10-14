<!-- SPDX-FileCopyrightText: 2026 Dennis40816 -->
<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
# Kept timing results

Keep result copies the complete current measurement into this capture's analysis.
It does not move the waveform. A default name identifies the source and kind;
Results opens an on-demand list for editing names and notes. No extra marker
track or permanent panel is added. The current measurement is labelled Current.

Selecting a list item only shows its sources and note. Show on waveform explicitly
copies it into the working A/B pair and fits/reveals its sources. Editing or
clearing that working copy leaves the retained result unchanged. Packet/byte
selection remains independent. Remove affects only the selected retained result;
Undo remove restores the latest removal, including its stable identity and order.
Undo is session-only and resets when another capture replaces this one.

Kept results store exact channel/sample and Edge/Position identity, not formatted
numbers. Local pulse width and a valid complete cycle are recalculated from the
capture; the separate cycle source is shown in details. A valid pulse without
a complete cycle has no frequency. General A/B intervals retain signed B−A,
including reversed or coincident endpoints, and never imply frequency. Positions
in missing coverage remain time coordinates, not evidence of observed activity.

Finish or cancel a pending pick/drag or invalid numeric input before Keep or Show.
Metadata Save/Cancel does not change endpoint evidence. Names are labels, not
identity keys; duplicate labels are allowed. New captures clear their retained
results after the existing Save / Discard / Cancel flow accepts replacement.

Analysis version 3 stores up to 256 results, each with an 80-character name and
2,048-character note. It requires the result collection; v1/v2 omission migrates
to empty. File validation rejects invalid sources, local pulse claims, duplicate
IDs and excess bounds before applying incoming state. Asynchronous saves freeze
the record array so later edits stay unsaved. See [analysis files](ANALYSIS-FILES.md).

Standalone point annotations, annotation tracks, formulas, limits, result search
and report export remain future work. This bounded feature is not an independent
human usability acceptance or a claim about hardware measurement accuracy.
