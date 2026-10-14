<!-- SPDX-FileCopyrightText: 2026 Dennis40816 -->
<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
# Saved analysis

File → Save analysis stores a portable `.dsview2` document. Open analysis restores
its raw evidence and committed working state without running a decoder. This is
DSView2's versioned JSON format, not an importer for DSView `.dsl`, Saleae or
other vendor formats.

The header identifies a saved analysis. Capture source details retain the
original description and explicitly distinguish saved decoded results from a
new decoder run. Existing examples are generated signals; saving them does not
make them hardware captures. The format does not attest decoder binaries or
hardware accuracy. No executable, runtime path or decoder command is loaded
from the file.

## What is kept

- Sample rate/count, physical IDs, names, initial levels, exact transitions and
  missing intervals. Dense traces are stored, not regenerated from a demo name.
- Packet bytes, interpreted fields, diagnostics, source spans, I2C completion,
  nullable ACK state and explicit annotations. An annotation may extend beyond
  EOF; it does not extend raw coverage.
- Group definitions and active scope, including saved members not present in
  this capture. One Group remains active even when it has no captured signals.
- Waveform colors, time range, edge navigation channel and vertical source
  anchor. The anchor is clamped to the new window's visible extent.
- A/B channel, exact sample and Edge/Position kind. Local pulse width, cycle and
  frequency are recalculated from the stored transitions.
- Selected packet/diagnostic or ordered protocol element, detail visibility,
  Waveforms/Packets and search.

Pending picks, drags and invalid numeric drafts must be finished or cancelled
before saving. Hover/probe, transient hints, navigation history and window
geometry are not saved. Named notes and multiple stored measurements remain
future work.

File-local Groups/colors do not overwrite personal defaults. Returning to an
example restores those defaults. The app does not automatically reopen the
previous analysis at startup; use Open analysis explicitly.

## Failure and concurrency

Read, validation and serialization run on a worker. During opening, analysis
editing is disabled while File → Cancel file operation remains available. A
cancelled or invalid file leaves the current workbench intact. Complete incoming
state is prepared before it is applied.

Saving freezes a snapshot of the working state. Editing can continue while it
is written. If later edits differ from the saved snapshot, they remain unsaved;
the app does not silently mark them saved. Numeric input with focus is committed
before keyboard Save; invalid input blocks saving.

Writes use a unique temporary file beside the destination and replace the
destination only after complete serialization and flushing. Cancellation and
failure before replacement preserve the old file. This is not an autosave,
crash-recovery or power-loss durability guarantee. The title asterisk and
Save / Discard / Cancel prompt protect deliberate unsaved work.

## Current format limits

Version 2 accepts at most 128 MiB, 32 physical signals, 2,000,000 stored
transitions, 100,000 packets, 10,000 missing intervals and 1,024 saved Groups.
Additional bounds apply to field/diagnostic text and collections. Unsupported
versions and unknown properties are rejected. All persisted properties must be
present, including nullable properties, except the optional presentation-only
protocol selection, so missing coverage/initial levels or
completion state cannot silently become defaults. Derived issue graphs and
formatted values are excluded from serialization.

Version 1 files remain readable without a selected protocol element. Saving
upgrades them to version 2; previous development builds reject version 2.

These bounded offline documents are a starting point for analysis persistence.
Long recordings need a separate streaming storage design; increasing JSON limits
alone would not establish a production capture format.
