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
- Capture-specific decoder roles and physical channel IDs. These describe
  wiring, not new observations; declared inputs may be absent from partial raw
  data. A track is displayed only when its source signals and records exist.
- Group definitions and active scope, including saved members not present in
  this capture. One Group remains active even when it has no captured signals.
- Waveform colors, time range, edge navigation channel and vertical source
  anchor. The anchor is clamped to the new window's visible extent.
- A/B channel, exact sample and Edge/Position kind. Local pulse width, cycle and
  frequency are recalculated from the stored transitions.
- Named timing results with exact A/B sources, local-pulse identity and notes.
  Each retained result is independent of the current working pair.
- Selected packet/diagnostic or ordered protocol element, detail visibility,
  Waveforms/Packets and search.

Pending picks, drags and invalid numeric drafts must be finished or cancelled
before saving. Hover/probe, transient hints, navigation history and window
geometry and removal undo are not saved. See [kept timing results](TIMING-RESULTS.md).

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

Version 4 accepts at most 128 MiB, 32 physical signals, 2,000,000 stored
transitions, 100,000 packets, 10,000 missing intervals, 1,024 saved Groups and
256 timing results (80-character name, 2,048-character note).
Additional bounds apply to field/diagnostic text and collections. Unsupported
versions and unknown properties are rejected. All persisted properties must be
present, including nullable properties, except the optional presentation-only
protocol selection and legacy timing/source metadata described below, so missing coverage/initial levels or
completion state cannot silently become defaults. Derived issue graphs and
formatted values are excluded from serialization.

Versions 1 through 3 remain readable. Version 1 has no selected protocol element;
both older versions may omit timing results, which migrate to an empty list.
Version 3 requires that list, even when empty. Versions 1–3 use their original
decoder wiring: I²C D0/SCL+D1/SDA, SPI D2/CLK+D3/MOSI+D4/MISO+D5/CS, and UART
slots D6 and D7. They cannot carry the new source metadata. Saving upgrades them
to version 4 and writes those roles explicitly without re-decoding packets.

Version 4 requires `capture.decoderSources`, even when empty. Each source has an
`id` and complete `inputs` with `role` and `channel`. Duplicate/unknown fields
inside these source/input objects, missing roles, duplicate roles/channels,
unknown source IDs and IDs outside D0–D31 are rejected. A packet must refer to
a declared source. This version retains four existing slots (`i2c`, `spi`, `ua`,
`ub`); extra decoder instances and application stacks remain open work.

Groups retain their raw membership when wiring changes. A decoder whose sources
are outside a Group stays outside that Group's view. Locate creates or reuses a
Group containing the actual mapped sources. Personal Group restoration does not
inject the example's default channels. All signals includes available decoded
tracks along with raw signals.

In-memory legacy captures can omit sources; the writer materializes the legacy
defaults. A version 4 file with absent or null sources is rejected on read.
Previous development builds reject version 4. Save as a new file
when an older build must continue reading the original.

These bounded offline documents are a starting point for analysis persistence.
Long recordings need a separate streaming storage design; increasing JSON limits
alone would not establish a production capture format.
