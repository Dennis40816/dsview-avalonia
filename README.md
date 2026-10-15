<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# dsview-avalonia

An independent project to develop an Avalonia logic analyzer frontend using
DSView's acquisition and decoding libraries.

[繁體中文](README.zh-TW.md)

## Status

A runnable native Avalonia **offline workbench** is available for development.
The default examples use bundled synthetic waveforms and precomputed protocol
records. An optional development I²C source runs an external native decoder on
its generated raw samples. Self-contained `.dsview2` analysis files preserve
samples, decoded snapshots and the working view. An optional Windows development
reader imports a bounded subset of DSView logic-v3 `.dsl` / `.demo` files as raw
signals. Hardware acquisition and general capture-format support remain open.
**License clearance is incomplete; no binary releases yet.**

The workbench supports one signal Group at a time, all-channel navigation,
custom waveform colors, Hex packet details, local high/low pulse width and
complete-cycle period/frequency, and cross-channel A/B edge or position timing.
Analysis version 5 stores decoder instances, physical roles and available SPI/UART
options, with lossless words up to 128 bits and separate SPI directions. Versions
1–4 remain readable; absent historical settings remain unknown. These saved-data
capabilities do not yet enable native SPI/UART decoding in the App. A development
[native serial adapter](docs/NATIVE-SERIAL.md) can run finite SPI/UART jobs
programmatically; its settings workflow still needs App integration.
With the optional native runtime configured, **Decoders**
in the sidebar lets you select SCL/SDA and run or cancel background I²C decoding
of the current capture. See [native I²C setup and limits](docs/NATIVE-I2C.md).
The time ruler stays visible while scrolling channels. A local frequency is not
a claim of a stable capture-wide clock.

Select a decoded byte to connect its Hex value, annotated interval and fields
in the existing inspector. ACK and START/STOP stay separate, with semantic colors
and text labels; NACK is amber and diagnostic evidence is rose. Locate selected
frames that evidence while preserving A/B; Previous returns to the earlier
view. See [protocol selection](docs/PROTOCOL-SELECTION.md).

Activity compares every captured channel over the full recording and current
time view. Capture issues lists diagnostics by their reported decoder layer;
Locate issue frames its evidence while retaining timing markers. The overview
shows observed raw transitions in the current Group, including control signals.

Groups, activity and kept results are in the left sidebar; File, capture source
and Settings stay in the header. Settings previews and saves interface/data text
size, waveform thickness and wheel direction, with Apply/Cancel. See
[display settings](docs/DISPLAY-SETTINGS.md) for behavior and pending native QA.

The product scope includes existing DSView capabilities. The first hardware demo
target is tentatively DSLogic Plus, not yet connected or verified. See the
[source-backed parity inventory](docs/DSVIEW-PARITY.md) for current gaps, library
reuse and delivery gates; source availability is not integrated device support.

Development boundaries and test intent are documented in [engineering](docs/ENGINEERING.md).

## Run the offline workbench

Use the .NET SDK pinned by `global.json` (10.0.303, with latest-patch roll-forward):

```sh
dotnet restore DSView2.slnx --locked-mode
dotnet build DSView2.slnx --no-restore
dotnet run --project src/DSView.Desktop --no-build
```

Focused numerical and state checks:

```sh
dotnet run --project tests/DSView.Checks --no-build
```

Use **File → Save analysis** (Ctrl+S) to retain the capture and current analysis;
**Open analysis** (Ctrl+O) restores it. Save as uses Ctrl+Shift+S. A title asterisk
means the view or analysis has changed. Closing, reloading or opening another
source offers Save / Discard / Cancel. Finish or cancel a pending measurement
edit before saving. Failed or cancelled saves retain the previous file.

Saved files include exact raw transitions, gaps, protocol/field/diagnostic
snapshots, Groups, waveform colors, A/B, selection, search, time view and vertical
source anchor. Local timing is recomputed from saved samples. Opening does not
run a decoder or require the optional native worker. Saved-analysis Groups and
colors are local to that document; returning to an example restores personal
defaults. See [analysis files and limits](docs/ANALYSIS-FILES.md).

Open the capture source button in the header to choose **Board bring-up**
(50 MS/s), **Timing + gaps** (10 MS/s), or **Dense signals** (50 MS/s).
Timing + gaps has changed bytes and clock timing, read termination, an address
NACK and missing coverage. Dense signals has 32 raw channels and 1,048,576 stored
transitions over 120 ms, without decoded packets. Opening an example clears
analysis state while preserving Group definitions and colors. If the active
Group has no captured signals, loading chooses a compatible Group. See
[offline examples and answer key](docs/OFFLINE-EXAMPLES.md).

If a manually selected Group has no signals in the current capture, the waveform
area explains the empty state and offers **Show all signals**. Edge navigation
and new picking are disabled until signals are visible. Existing measurements
remain available; **Fit** frames the endpoint times and reveals their source rows
when both fit vertically. If the rows are too far apart, it reveals A and offers
**Show B** beside the offscreen endpoint. **Show A/B** preserves endpoint values;
**Show all signals** restores captured rows without changing the time range.

The Group editor offers signals captured by the current source and compatible
decoder tracks. Existing members absent from this capture remain listed as saved
members, so editing does not erase a reusable Group. Editing **All signals**
creates a separate Group starting with the current captured signals. Groups
change the view; selecting a decoder track does not start acquisition or decoding.
All valid saved Groups are restored on restart.

**I²C from samples** appears when a local native-worker configuration is present.
It contains only D0/SCL and D1/SDA; its packets are decoded in a background job
from the same samples used for display and timing. The worker/runtime is not
bundled. See the [development decoder contract](docs/NATIVE-I2C.md) for setup,
focused process checks and limits. This is a finite I²C path, not streaming or
general decoder support.

**File → Import DSView capture** uses a separately configured native reader.
Import runs in the background and starts with All signals and no decoded records.
Physical channel IDs and names are retained; saved device/decoder settings are
not applied. Escape cancels import; failure or cancellation keeps the previous
analysis. Save creates a separate `.dsview2` analysis, leaving the source capture
unchanged. Reopening that analysis requires neither the source file nor the
reader. See [the capture-reader contract and limits](docs/NATIVE-DSL.md).

Dense waveform regions appear as activity bands. Zoom in to resolve individual
transitions. Picking and timing use the original samples, independently of the
display's detail level. See [waveform rendering](docs/WAVEFORM-RENDERING.md) for
the implementation and optional operation-count checks.

The ruler uses ns, µs, ms or s for the current span. Fine views far from the
capture start show an explicit time origin and relative ticks. A/B entry remains
in µs as labeled, so zooming never changes the meaning of a typed value.
An empty Packets view explains whether the capture has no records, the Group
has no packets, or the search has no matches. Filtered-out selections remain
available in the inspector with their scope identified.

Start with a Group or All signals. Hover a pulse to preview timing; click to pin
it. Choose Cursor A/B, press on A and drag to B on the same or another signal.
Release commits the interval; clicking A then B also works. Choose Position for
sample-grid locations instead of edge snapping. Releasing a new pair outside a
signal or away from a valid edge restores the prior measurement. Escape or lost
pointer capture cancels the drag. The wheel does not change the view during a
drag. Escape also cancels a pending selection or numeric edit. The wheel zooms
around the pointer over the waveform or ruler, without requiring Ctrl. Shift +
wheel scrolls signal rows; the channel-label rail also retains ordinary vertical
scrolling. Dragging empty waveform space pans. Select a packet for its content;
Locate source explicitly changes the time view. Fit time shows the whole recording.

Cursor A/B focuses the waveform: Up/Down chooses a signal, Left/Right steps
through observed edges (one sample in Position mode), and Enter selects the
endpoint. Outside a pending selection, Enter pins the local pulse at the keyboard
cursor. A and B have separate ruler handles even at the same time. Numeric edits
set a Position; invalid input leaves the committed measurement intact.
Drag either cursor line or ruler handle to refine its position. Interval arrows
mark visible endpoints of the pulse preview and A/B range; an offscreen endpoint
is not replaced by an arrow at the viewport boundary. The overview distinguishes
the current time window with a lighter surface and outline. Packet-data copying
uses the copy icon beside the Hex heading, with a tooltip and accessible name.
Fit frames the selected interval and any complete cycle used for local frequency.
It preserves a Group that already contains both endpoints. Previous view
restores the earlier viewport without clearing the measurement.

Show in the Activity panel reveals a channel without changing the time range.
Explicit source/issue navigation can create a source Group if custom Groups no
longer contain the decoder's required channels.

The physical probe swatch is fixed. Click the separate short line beside a
signal name to open its appearance panel beside that channel. Presets or a
six-digit Hex color update a local waveform preview; Apply commits the color.
Default also remains a draft until Apply. Cancel, Escape or clicking outside
discards the draft. This panel changes display color, not acquisition settings.
Group/color preferences are saved
locally. Save analysis stores the capture, working measurement and kept results.

Keep result copies a complete measurement. Results opens a compact list with
names, notes and exact sources; Show on waveform restores a working copy.
Clear affects only the working pair. See [timing results](docs/TIMING-RESULTS.md).

Windows native interaction has been exercised. Other desktop platforms, the
full DPI matrix and large-recording performance still need validation. The
synthetic fixture cannot validate actual device timing or decoder correctness.
Native ns/µs/ms views, short-pulse fitting, cross-channel keyboard selection,
numeric cancellation and empty-search recovery have been exercised. Large-offset
labels, the other empty-state variants and the full keyboard/DPI matrix still
need native verification. These checks are not an independent usability trial.

Before binary distribution, the project must resolve upstream source and notice
gaps, determine the actual build and runtime dependencies, and produce a software
bill of materials (SBOM). See [third-party review status](THIRD-PARTY-NOTICES.md).

## Get the sources

```sh
git clone --recurse-submodules https://github.com/Dennis40816/dsview-avalonia.git
cd dsview-avalonia
git -C external/DSView rev-parse HEAD
```

The expected upstream revision is:

```text
2e9e2c8e726df4ef5687d39b83d4f797cc44b574
```

For an existing clone:

```sh
git submodule update --init --recursive
```

The `external/DSView` gitlink fixes the revision. Do not use
`git submodule update --remote` when reproducing this revision. GitHub's automatic
source ZIP does not include the submodule checkout; use Git for the complete
source reference.

The submodule contains the full upstream source tree, including components that
are not selected for this project's planned runtime. Its presence is not a
runtime packaging allowlist or a completed license review. No upstream files
are copied into the frontend's own source tree.

## License

Original project code, configuration, and documentation are licensed under
**GNU GPL version 3 or, at your option, any later version**
(`GPL-3.0-or-later`). See [LICENSE](LICENSE), [NOTICE](NOTICE), and the
[SPDX header rules](docs/SPDX.md).

Third-party sources retain their own licenses. The root license does not
relicense the DSView submodule or its dependencies.

This project is not affiliated with or endorsed by DreamSourceLab and is not
an official successor to DSView.
