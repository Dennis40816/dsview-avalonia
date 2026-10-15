<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Decoder workflow and implementation boundary

The sidebar **Decoders** panel supports a new or existing I²C, SPI or UART
instance. It uses the inspector area; the waveform remains the main workspace.
This is development integration with optional external runtimes. Compiled views
and programmatic integration checks do not establish native UI usability or
hardware capture support.

## Use

1. Open a capture, then **Decoders**. Choose **New decoder** or an existing
   instance. An existing instance keeps its protocol and identity.
2. Choose a protocol and map roles to captured physical channels. Required roles
   are not guessed. Missing saved channels remain marked unavailable. SPI needs
   CLK and at least one of MOSI/MISO; optional roles offer **None**.
3. Set SPI mode or UART baud/parity, and the data width. Advanced settings contain
   bit order, SPI chip-select polarity and UART inversion. UART currently supports
   one stop bit; the upstream second-stop validation limitation remains open.
4. **Decode** runs over the full capture, independently of the visible Group and
   zoom. **Cancel** preserves previous results. A failure also preserves them.
5. Successful decoding replaces only that instance. Other instances, raw signals,
   Group, viewport and A/B remain. **Show decoded signals** explicitly reveals
   the source. A successful empty result removes that instance's old records.

Settings are a draft until successful publication. Old files without recorded
SPI/UART options show an unknown-settings notice: defaults configure the next
job, not the already saved results. New instances receive distinct identities;
renaming/removing instances and parent decoder stacks are separate future work.

## Boundaries

- `Capture` owns physical signals, sample rate/count and sample coordinates.
  Protocol annotations may estimate their endpoints. Raw edge measurements never
  take their timing from those estimates.
- `DecoderSource` identifies a protocol instance, physical input roles and its
  typed options. It contains no native executable paths or UI controls.
- Each adapter accepts the original capture, binding and cancellation token.
  `IDecoderResult` returns that source reference, effective binding and typed
  packets. Native JSON/process details remain in infrastructure adapters.
- `DecoderDraft` owns local role/parameter editing. `DecoderViewModel` owns one
  background job, cancellation and generation. Its injectable runner is a small
  boundary for another implementation; there is no decoder-specific Window
  hierarchy or cloned UI lifecycle.
- Publication requires the same capture, generation and full configuration.
  Closing, switching files, disposal or cancellation prevents a late result from
  publishing. Pending cursor edits block replacement. Shutdown awaits completion.
  Workbench is the single committed analysis owner. Replaced selections clear;
  selections in untouched instances remain.
- Draft validation updates when settings or capture change. Ordinary waveform
  navigation does not repeatedly invalidate decoder controls. Native packing and
  execution run off the UI thread; this does not prove live throughput.

## Reuse a library or replace a decoder

Keep the native implementation first. A future C# implementation can return the
same typed result through the runner boundary. Before switching it, compare
independent raw fixtures for values, direction, physical sample positions,
completion, diagnostics and cancellation behavior. Preserve uncertainty and
incomplete frames; matching a formatted Hex string alone is insufficient.

Adding a new protocol may still require a typed evidence model and analysis-file
migration. The boundary does not imply arbitrary protocols can be enabled by
changing a name. Avoid a universal base class with unrelated protocol fields.
Parent stacks need explicit input/output contracts and provenance separately.

## Verification and remaining work

Workflow checks cover draft admission, locked edits, cancellation with late
success, capture replacement, close/reopen, disposal, shutdown, retry, pending
cursor edits, wrong source/roles/options, instance limits and viewport isolation.
Opt-in native checks exercise sparse-channel 9-bit SPI/UART through the same VM,
multiple SPI instances, replacement, saved/reopened direction and A/B. An
installed-capture I²C pipeline verifies a second independent instance as well.

`--decoder-workflow-only` limits the checks executable to this boundary. Add
`--native-serial <config>` for serial integration; add `--native-i2c <config>`,
`--native-dsl <config>` and `--decode-capture <file>` together for the import/I²C
pipeline. External runtimes and fixtures are opt-in development prerequisites.

Native DPI, large fonts, keyboard/focus behavior, panel scrolling and cancellation
feedback still need direct UI verification and owner testing. Runtime packaging,
real acquisition, long captures and concurrent acquisition/decode/render
performance remain open. Protocol-worker fixtures do not close those gates.
