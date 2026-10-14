<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Offline examples

Use the capture source button in the header to open an offline example.
Selection in the dialog does not change the current capture. **Open example**
replaces it and clears measurements, pending edits, packet selection, search and
navigation history. Group definitions and waveform colors remain. Reload
reloads the successfully opened example. These captures are synthetic; their
annotations are precomputed and do not establish raw-decoder or hardware accuracy.

| Example | Sampling | Purpose |
| --- | --- | --- |
| Board bring-up | 50 MS/s, 50,000 samples, 1 ms | Multiple protocols, packet fields and layered diagnostics |
| Timing + gaps | 10 MS/s, 80,000 samples, 8 ms | Changed clock timing and bytes, normal read NACK, address NACK and missing coverage |
| Dense signals | 50 MS/s, 6,000,000 samples, 120 ms | 32 raw channels with 32,768 transitions each; dense rendering and source-based timing; no decoded packets |

The header marks missing data regardless of zoom. Capture source details gives
the exact missing interval and a **Show interval** action. That explicit
navigation preserves measurements. Hatched regions have no samples. A dashed
level after the gap remains unknown until an observed transition establishes
it. A position within missing coverage is a time coordinate, not an observed
edge; its endpoint label identifies missing samples.

## Fixture representation

The embedded JSON files declare `sampleRate` in samples/second and `sampleCount`.
All JSON time values, including `gaps`, are in microseconds. The loader converts
them to integer sample indices using the declared rate. Gaps are half-open.
Channel `events` identify transitions, not arbitrary first-resumed level
snapshots. Events inside a gap are omitted. The first explicit transition after
a gap is retained even if its resulting level matches the last observed level
before the gap. Such a match does not establish the intervening waveform.

I2C `wire.cells` provides data and acknowledge intervals for the address and
each data byte. Sampling rate does not determine protocol bit duration. Without
these intervals the UI displays packet-level data and does not invent detailed
byte/ACK bounds. Direction, ACK state and diagnostic layer have separate fields.

Generate Timing + gaps from this repository root:

```sh
python tools/generate-timing-example.py
```

The generator writes only `timing-gaps.json`. Focused checks compare its stored
SCL sampling edges and SDA levels with the declared wire bytes, ACKs and STOP.
This is fixture consistency verification, not a raw protocol decoder.

## Dense signal generation

`DemoCaptureSource` generates Dense signals on the background source-loading
path. It allocates an independent transition array for each physical channel.
No JSON fixture or raw decoder supplies this example. All initial levels are Low;
there are no missing intervals or packet records.

For channel `c` from 0 through 31, `step = 64 + (c % 16) * 4` samples and
`offset = (c / 16) * 1,000,000 + c * 127` samples, using integer division.
Transition `i` from 0 through 32,767 occurs at `offset + (i + 1) * step`;
even `i` changes to High and odd `i` changes to Low. Generation checks cancellation
between bounded batches. The last level continues to the recording's end.

Select All signals and Fit time to see the full recording. A dense activity band
retains short pulses even when their entering and exiting levels match. Zoom in
for exact edges. The [rendering checks](WAVEFORM-RENDERING.md) count source access;
they do not establish an interactive frame rate.

## Developer answer key

Use these values to verify the implementation; do not read answers to a
participant during an independent usability task.

- D0 at 1012 microseconds: Low width 5 microseconds, period 10 microseconds,
  frequency 100 kHz. Pulse samples 10100–10150; cycle samples 10100–10200.
- D0 falling sample 10100 to D8 falling sample 17000: +690 microseconds.
  Reversed endpoints give -690 microseconds. An arbitrary interval has no
  signal-frequency result. Position 1012.3 microseconds is sample 10123.
- `alt-read-1`: 1000–1650 microseconds, 7-bit address 0x51, Read; wire address
  byte A3. Data `A5 21 02 78 56 00`; little-endian payload 0x5678. The synthetic
  DemoFrame definition uses XOR over the preceding five data bytes: expected A8,
  received 00. Diagnostic evidence is 1550–1630 microseconds.
- The last read byte ends with NACK, which terminates the read; it is not an
  additional I2C failure. The separate `alt-address-nack` transaction is Write,
  wire address A2, no data bytes, and address NACK evidence at 5090–5100
  microseconds. This distinction follows the acknowledge rules in
  [NXP UM10204](https://cache.nxp.com/docs/en/user-guide/UM10204.pdf).
- Missing samples are `[40000,45000)`, or 4–4.5 ms. D8's missing transition at
  sample 40500 is excluded. Its observed transitions are 9000 High, 17000 Low,
  18000 High and 47000 High. The recovery edge at 47000 remains visible without
  a fabricated Low interval before it.
- D8's level is unknown from 4 to 4.7 ms, then known High. D9 has no observed
  post-gap transition and remains unknown until the capture ends at 8 ms.
- Dense signals D0: each complete High/Low pulse is 64 samples or 1.28 us;
  a complete cycle is 128 samples or 2.56 us, giving 390.625 kHz.
- Dense signals D31: each complete High/Low pulse is 124 samples or 2.48 us;
  a complete cycle is 248 samples or 4.96 us, giving approximately 201.613 kHz.
  For example, the Low pulse spans samples 1,499,937–1,500,061, and its full
  cycle spans samples 1,499,937–1,500,185. The final incomplete pulse does not
  establish another complete cycle.

Passing these checks does not constitute independent usability acceptance.
Device capture, real decoding, import, session persistence, end-to-end performance
on long recordings, other desktop platforms and the full DPI matrix remain
separate validation.
