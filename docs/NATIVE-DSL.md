<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Optional native DSView capture reader

This Windows development adapter imports raw digital samples through a separate
native reader. The repository contains the managed adapter and synthetic checks;
the reader build and runtime are not yet packaged here. Default builds can open
saved `.dsview2` analyses without it. This is not general DSView compatibility or
hardware acquisition support.

## Local setup and behavior

Place `native-dsl.json` beside the built desktop executable. Configure reviewed
absolute paths and the actual executable hash:

| Field | Meaning |
| --- | --- |
| `Worker` | Executable implementing the version-1 contract below |
| `WorkerSha256` | SHA-256 of that executable, 64 hexadecimal digits |
| `StagingDirectory` | Writable local-drive Windows directory; ASCII path at most 180 characters |
| `RuntimeDirectories` | Existing absolute directories for the worker's dependent DLLs |

The local worker uses `libsigrok4DSL` and MiniZip from upstream revision
`2e9e2c8e726df4ef5687d39b83d4f797cc44b574`. A stock DSView executable or arbitrary
DLL is not a substitute. The hash check covers the selected executable, not all
dependent DLLs. Child-process PATH changes do not change the machine's PATH.
Keep machine-specific configuration, captures and binaries out of the repository.

Use **File → Import DSView capture**. A missing configuration produces an
unavailable message. A successful import shows All signals, the whole recording,
original physical IDs and names, and no packet records. Groups and colors are
copied from personal defaults into the new analysis. Raw captures display no
decoder rows, even if a reusable Group includes a decoder configuration.

The first stored sample is time zero. Stored trigger position and raw signed
Unix-millisecond trigger time are retained in source details; they do not move
the ruler origin. Saved device settings and decoder configurations are not
applied. Channel names do not assign decoder roles: D0 may be SDA, not SCL.

Escape cancels an in-progress import. Preparation must succeed before the old
analysis is replaced. Failed or cancelled imports retain the prior capture,
measurement and source identity. Save writes a separate `.dsview2` analysis;
Save rejects the active imported source path as its destination. The analysis contains raw data
and provenance and can reopen without the native reader or original archive.

## Supported development subset

- Version-3 logic captures, 1–32 physical channels, IDs 0–31.
- 1–256 blocks, 64–64,000,000 samples, with the native reader currently requiring
  a multiple of 64 samples. Integer SI sample-rate strings up to 1 GHz.
- Source archive and expanded cross-data payload each at most 128 MiB.
- At most 2,000,000 transitions across all channels after managed conversion.
- Nonblank channel names without control characters, at most 255 UTF-8 bytes
  and 128 Unicode code points in native preflight; the managed boundary further
  limits names to 128 UTF-16 code units. Some supplementary-character names can
  therefore be explicitly rejected by the managed boundary.

Native preflight checks declared block/channel coverage, duplicate keys/entries,
CRC and exact lengths. Older versions, analog/DSO modes, partial-word native
captures and fractional rate strings are outside this subset; rejecting them
does not establish that they are invalid DSView files. Decoder selection,
selected-range compatibility and newly exported real-device captures still
need broader interoperability evidence.

## Execution and binary contract

The managed job copies the selected file into a unique directory and computes
its SHA-256. The original is opened without write/delete sharing during copying.
The private ASCII-named copy remains locked against writes/deletion across
preflight, native reading and conversion. Unicode original paths are supported.
The worker receives two arguments: the private input path and a fresh output
path. It creates the output exclusively; it must not overwrite an existing file.

Stdout is one UTF-8 JSON object, emitted only after successful native completion
and closing the full output. A nonzero exit is failure. Every field is required,
including nullable trigger fields; duplicate, unknown and missing fields fail:

```json
{
  "version": 1,
  "layout": "cross64-le-lsb",
  "complete": true,
  "sampleRate": 25000000,
  "sampleCount": 128,
  "payloadBytes": 32,
  "triggerPosition": 0,
  "triggerTimeUnixMs": null,
  "channels": [{ "id": 1, "name": "SCL" }, { "id": 12, "name": "SDA" }]
}
```

Channels have unique ascending physical IDs. For each group of 64 samples,
the payload contains one little-endian 64-bit word per channel, in that order;
bit 0 is the earliest sample. It is not a byte per sample or one complete plane
per channel. The exact payload size is `ceil(sampleCount / 64) * channels * 8`.
The managed adapter also handles a partial final word and ignores its padding,
although the current native preflight does not accept that input subset yet.

Initial levels precede transitions. Conversion preserves actual changes at
sample 64 boundaries and creates no edge at sample 0 or EOF. Timing uses the
same resulting samples as waveform rendering. No decoded records are invented.

Stdout/stderr are bounded at 32,768/65,536 characters and read concurrently.
The native process has a 45-second deadline. Cancellation terminates the process
tree and observes exit before removing staging. Unconfirmed termination retains
staging and includes its path in the diagnostic exception; the desktop currently
shows a generic error for that teardown failure. Conversion is also cancellable, including constant
signals without transitions. Copy and conversion are outside the native-process
deadline. This finite transport is not a streaming acquisition design.

## Verification and remaining work

```sh
dotnet run --project tests/DSView.Checks --no-build -- --native-dsl /absolute/path/native-dsl.json
```

Without that argument the suite checks managed layout/timing, metadata bounds,
transition limits, cancellation and analysis round trips. With it, it also checks
an actual reader using a synthetic Unicode-named archive with reordered sparse
physical IDs, rejects changed worker identity and inconsistent data, cancels an
observed new worker, confirms exit/cleanup and imports again.

These checks do not prove native UI operation, owner usability, hardware timing,
large-capture throughput, all DSView formats or distribution readiness. A
portable worker build, actual dependency inventory, matching distribution
sources and unresolved upstream notices remain required before binary release.
