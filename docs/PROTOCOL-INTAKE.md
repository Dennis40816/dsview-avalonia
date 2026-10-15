<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# SPI and UART intake contract

Current-capture native decoding is integrated for I²C. SPI and UART tracks in the
default examples are still saved synthetic results. A local native experiment
has exercised the pinned `1-spi` and `1-uart` modules against independent raw
stimuli. Managed adapters now run that worker on finite raw samples and produce
typed results. I²C/SPI/UART configuration is connected to the App's Decoders
panel through a [shared workflow](DECODER-WORKFLOW.md); native UI acceptance is
still pending. Runtime distribution and live hardware performance remain open. See
[native serial integration](NATIVE-SERIAL.md).

## Use the existing decoder, preserve its evidence

The selected modules come from DSView revision
`2e9e2c8e726df4ef5687d39b83d4f797cc44b574`.
Their source headers identify GPL-2.0-or-later. This observation is not a release
approval or a conclusion about every runtime dependency. The worker reuses the
locally staged libsigrokdecode runtime and the previously reviewed chunk-boundary
candidate patch; the upstream checkout and the App's existing I²C worker remain
unchanged.

| Protocol | Required source roles | Optional roles | Settings carried with results |
| --- | --- | --- | --- |
| SPI | CLK and at least one of MOSI/MISO | Other data direction, CS | Mode 0–3, bit order, word size, CS polarity, role presence |
| UART | One data signal per instance | None for this module | Baud rate, data bits, parity, stop bits, bit order, inversion |

Missing roles must remain absent. Do not assign an unrelated channel or synthesize
a constant input to satisfy a legacy four-role SPI schema. The worker's plane
order is CLK, MISO, MOSI, CS; this is transport order, not physical probe order.
The App must keep the physical channel IDs selected by the user.

The experiment exercised SPI modes 0–3, both bit orders, 5/8/16/128-bit words,
active-low/high CS, each supported optional channel combination and incomplete
words. It checked both data directions, native bit sampling positions and
CS-bounded transfers. UART checks exercised parity variants, inversion,
4/8/9/16/128-bit words, both bit orders, 9600/115200/921600 baud at 24 MS/s,
parity/first-stop failure, break and truncated input. One-sample and unaligned
chunks were compared with one-shot results for the stated fixtures. This does
not prove every option combination or generic streaming support.

## Known upstream UART limitations

1. The decoder advertises several stop-bit counts, but its implementation checks
   the first stop bit only. A raw stimulus configured for two stop bits, with a
   valid first stop slot and a low second slot, still produces a valid first
   frame. Do not label this as complete two-stop-bit validation. The managed
   capability boundary must reject unsupported stop-bit validation until a
   reviewed decoder fix has independent evidence.
2. `PARITY ERROR` currently emits a placeholder pair `(0, 1)`. An even-parity
   stimulus with value `0x01`, expected parity 1 and observed parity 0, reproduces
   that incorrect pair. Retain the parity-error diagnostic and invalid-frame
   status. Do not show the placeholder as an observed expected/actual comparison.
3. DATA may precede FRAME, and EOF may arrive before completion. A DATA result
   alone cannot establish a valid frame or an observed stop bit. Break also owns
   a separate interval; it is not an ordinary successful data byte.

These limitations are explicit unresolved intake items, not passing regressions
that require the product to preserve incorrect behavior.

## Saved evidence and display preparation

Analysis version 5 adds typed SPI/UART snapshots and capture-specific instance
identities. The protocol configuration workflow remains open.
The default SPI/UART examples retain their historical byte snapshots.

- `SerialWord` stores a bit width, canonical uppercase fixed-width Hex and source
  interval. SPI words use 5–128 bits; UART uses 4–128. Padding above the declared
  width must be zero. Numeric values remain independent of wire bit order.
- `SpiTransfer` keeps simultaneous MOSI/MISO words separate. Optional data and CS
  roles remain absent. Completion distinguishes CS release, capture end, no CS
  boundary and unknown. Two waveform lanes retain independent selection indices.
- `UartFrame` distinguishes incomplete evidence from an observed stop and carries
  nullable validity. Data alone is not a complete frame. Current options admit one
  stop bit only, because the limitation above is unresolved. Rejected start bits
  have no data word. Break has its own diagnostic interval and does not replace
  the preceding invalid frame.
- Decoder sources save protocol, unique instance ID, physical roles and optional
  typed settings. Up to 32 root instances can be stored. Parent stacks are not
  implemented. Old fixed-slot records remain readable without invented options.
- Version 1–4 files can be read and saved as v5. New saved source entries include
  explicit protocol identity. Missing old options become null. Existing byte data
  is not retroactively converted into purported native word evidence.
- Group membership can retain absent decoder IDs across captures. Only the
  capture's explicit sources authorize wiring or packet evidence. Missing members
  do not create tracks or attach the demo's physical channels.
- The contextual word inspector displays at most 32 direction/value rows per
  page. Copy retains full Hex and direction; selecting a word connects it to its
  decoded interval. This is not raw-edge timing evidence.

New tests cover wide duplex values, 9-bit UART, options, migration, item selection,
bounded paging, stale source rejection, optional roles and strict JSON admission.
These are model/storage checks, not native pointer, layout or hardware acceptance.

## Adapter contract

- Produce words with explicit bit width and lossless values; do not
  truncate 9–128-bit values to a byte or 32-bit integer. Preserve transmission
  order separately from numeric byte order and Hex formatting.
- Retain MOSI and MISO as separate simultaneous data directions. Do not put one
  in `Address` or concatenate them into a misleading single byte stream. Both
  directions need selectable waveform spans and independent copy actions.
- Populate the saved instance, physical roles and options from the actual job.
  Parent stack outputs still need their own typed contract and migration.
- Preserve native DATA/BITS/CS-CHANGE/TRANSFER and UART start/data/parity/stop/
  frame/break provenance. Decoder annotation endpoints can be estimates; they
  must not replace observed raw edges for timing measurements.
- Validate the envelope, options, event types, sequence, bit/value agreement,
  sample bounds and completion before publication. Reject duplicate/unknown
  fields. Keep unknown/incomplete evidence distinct from valid zero values.
- Reuse the existing background-command lifecycle, rather than clone a separate
  window workflow for every protocol. Protocol adapters own their contracts;
  Workbench remains the single committed analysis owner.

## Interaction direction

Extend the sidebar Decoders entry. Keep the waveform as the primary surface.
Show protocol, physical roles and the common settings first: SPI mode and UART
baud/data/parity/stop. Less frequent word widths, bit order and inversion belong
in the same contextual settings panel. Do not add permanent Header controls.

After successful decode, an explicit action reveals the mapped source Group.
Changing a draft must not alter saved results. Keep previous results on failed or
cancelled jobs; label incomplete data accurately. Multiple decoder instances and
layered parsers must retain their own configuration and result identities.

## Remaining gates

The local finite-input experiment passes 40 grouped behavior cases across 117
process invocations, with the two UART limitations above recorded separately.
Its original bridge initialization crash was fixed by acquiring the Python GIL
after `srd_init` releases it; it was a bridge bug, not a protocol decoder result.
The two existing native Python deprecation warnings remain.

Managed adapters now verify settings, event sequence, sampled bits, values and
completion. Bounded worker jobs have checks for cancellation, output overflow,
failed exit and startup failure cleanup. The 45-second deadline and failure to
terminate an OS process have not been exercised by injected failure tests.

Native rendering/interaction verification, runtime packaging,
third-party interoperability goldens, throughput and hardware capture remain open.
No owner usability task is marked passed by these checks.
