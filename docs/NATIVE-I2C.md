<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Optional native I²C development source

This adapter proves a finite source-to-decoder path in the offline workbench.
It is not a packaged decoder backend. The repository contains the managed
adapter and synthetic input; it does not yet contain a reproducible native
worker build or its runtime. Default builds remain usable without this option.

## Local setup

Use a reviewed worker implementing the contract below. The development worker
uses `libsigrokdecode4DSL` and `1-i2c` from upstream revision
`2e9e2c8e726df4ef5687d39b83d4f797cc44b574`, with a local runtime candidate patch.
It is not interchangeable with a stock sigrok CLI or an arbitrary DSView DLL.
A portable worker build, dependency manifest and matching distribution sources
remain required before this can become a generally available product feature.

Place `native-i2c.json` beside the built desktop executable. Use reviewed
absolute paths and actual SHA-256 values for these fields:

| Field | Meaning |
| --- | --- |
| `Worker` / `WorkerSha256` | Worker executable path and SHA-256 |
| `DecoderDirectory` | Directory containing only the selected `1-i2c` module |
| `DecoderSha256` | SHA-256 of `1-i2c/pd.py` |
| `InitializerSha256` | SHA-256 of `1-i2c/__init__.py` |
| `PythonHome` | Matching embedded Python installation |
| `RuntimeDirectories` | Array of directories for the worker's dependent DLLs |

Configuration existence adds **I²C from samples** to the source chooser.
Opening validates configuration and the three selected file hashes. These
checks detect changes to those files, not changes to every dependent module.
The runtime must be locally trusted. Child-process environment settings do not
modify global PATH. Do not commit machine-specific configuration or binaries.

Run focused checks against the same configuration:

```sh
dotnet run --project tests/DSView.Checks --no-build -- --native-i2c /absolute/path/native-i2c.json
```

Without this argument the checks cover the managed contract and skip native
process checks. With it they also decode actual samples, mutate SDA, truncate
before ACK, validate selected identities, cancel an observed running worker,
and decode again. They do not exercise native UI layout or establish usability.

## Execution and ownership

`DecodeAsync` runs validation, packing, worker I/O and adaptation in a background
job. Each invocation uses a fresh process and a unique temporary input file.
The desktop source-generation check prevents stale loads replacing a newer
capture. Failed or cancelled loads retain the previous capture.

Input must be a finite, continuous capture of 10 through 64,000,000 samples.
D0 is SCL and D1 is SDA. Both must have ordered, alternating transitions; the
initial level precedes the first transition. Missing-data intervals are rejected.
Other captured channels remain in the capture but do not enter this decoder.

The worker receives arguments in this order:

1. Decoder directory.
2. Packed input file.
3. Sample count.
4. Sample rate in samples per second.
5. Chunk size, equal to the full sample count for this adapter.

The packed file contains two consecutive bitplanes, SCL then SDA. Each plane
has `ceil(sampleCount / 8)` bytes with least-significant-bit-first samples.
It is generated from the same signal arrays used for display and measurement.
The fixture has no packet records to copy into the result.

The parent reads stdout and stderr concurrently, with limits of 4,000,000 and
65,536 characters respectively, and a 45-second execution deadline. Cancellation
terminates the process tree and observes both readers. Input is removed only
after confirmed exit. A teardown failure reports retained input instead of
deleting it under a possibly running worker; cleanup errors preserve the original
failure. This finite development transport is not intended for large streaming
datasets.

## Structured output

The worker copies callback data into owned records and writes one JSON document
after native session teardown. The envelope contains `schema: 1`,
`decoder: "1:i2c"`, `addressFormat: "shifted"`, the requested `sampleRate` and
`sampleCount`, `completion: "eof"`, `openTransaction`, and an `events` array.
At most 200,000 events are accepted. A nonzero process exit is a failed load.

Each event has sequential `sequence`, `kind`, `start`, `end`, `spanEvidence`,
nullable `value`, and `bits` fields. Sample positions are integers.

- `START`, `START REPEAT`, `STOP`: zero-width observed points, `value: null`,
  empty `bits`, `spanEvidence: "point"`.
- `BITS`: eight `[value, start, end]` triples in transmission order, immediately
  followed by its byte event. Bit values are 0/1 and observed starts are inside
  the capture. Contiguous annotated bit spans must match the byte span.
- `ADDRESS READ`, `ADDRESS WRITE`, `DATA READ`, `DATA WRITE`: a value matching
  the preceding bits, empty `bits`, `spanEvidence: "decoder-annotation"`.
  Addresses are shifted seven-bit values; the wire R/W bit must match direction.
- `ACK`, `NACK`: annotation for the pending byte, `value: null`, empty `bits`.

The adapter checks the event sequence and byte/bit consistency; it does not
reimplement raw SCL/SDA decoding in C#. Annotation ends can extrapolate beyond
the final sampled bit and even beyond EOF. They are not precise physical bit
boundaries. Waveform timing continues to use observed raw edges.

Repeated START ends one addressed segment and opens the next. EOF preserves
an incomplete prefix, unknown address/direction or unknown ACK where applicable.
No STOP or ACK is fabricated. The timeline labels observed points S / Sr / P.
Address NACK and write-data NACK produce factual I²C diagnostics; a normal final
read NACK does not. No DemoFrame or private application-layer fields are inferred.

## Scope still open

This option does not provide hardware acquisition, capture-file import,
gap-aware decoding, configurable channel roles, other decoders, application
stacking. Capture import and saved analysis are separate workbench paths; neither
automatically runs this decoder. The candidate native runtime
patch has I²C-specific evidence; generic `wait`/`skip` and streaming behavior
remain unverified. Official DSView output comparison and real-capture golden
tests are still needed. Native UI operation, cancellation feedback and the
owner's uncoached task trial remain distinct from these programmatic checks.
