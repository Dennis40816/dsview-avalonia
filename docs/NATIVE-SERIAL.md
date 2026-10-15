<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Native SPI and UART development adapter

`NativeSerialSource.DecodeAsync` runs an external native worker against finite
raw samples. It returns source identity, instance, physical roles, settings and
typed packets. `SerialDecodeResult.ToCapture()` replaces only that instance's
records and preserves raw signals and source description. It does not apply a UI
result. The current App Decoders panel still runs I²C only.

The worker and runtime are separately staged development artifacts. There is no
released serial runtime bundle or hardware-acquisition claim. See the
[intake contract](PROTOCOL-INTAKE.md) for selected modules and UART limitations.

## Configuration

`native-serial.json` uses exact, case-sensitive fields:

| Field | Value |
| --- | --- |
| `Worker` | Absolute executable path |
| `WorkerSha256` | Reviewed executable SHA-256 |
| `DecoderDirectory` | Absolute decoder directory |
| `DecoderFiles` | Relative module paths mapped to reviewed SHA-256 values |
| `PythonHome` | Existing absolute Python runtime directory |
| `RuntimeDirectories` | Existing absolute DLL/runtime search directories |

The module inventory contains exactly `1-spi/pd.py`, `1-spi/__init__.py`,
`1-uart/pd.py`, `1-uart/__init__.py`, `common/__init__.py`,
`common/srdhelper/__init__.py` and `common/srdhelper/mod.py`.
Unknown, duplicate and missing configuration fields are rejected. Worker and
module hashes are checked before execution. This is not a complete attestation
of the Python/native runtime or a release license review. An analysis file cannot
supply executable configuration.

## Execution limits

- Input: 10–64,000,000 continuous samples without gaps. Every selected physical
  channel must exist exactly once. UART needs at least two samples per bit.
- SPI transport planes: CLK, MISO, MOSI, CS. Optional roles stay unmapped; unused
  transport storage does not become a fabricated physical signal. UART uses one
  selected data plane. Packing and execution run off the UI thread.
- Maximum packed input: 32 MB for four planes. Each worker job owns its temporary
  input. Execution uses no shell or visible console. Python user-site imports and
  bytecode writes are disabled; inherited `PYTHONPATH` is removed.
- Stdout: 4,000,000 characters. Stderr: 65,536 characters. Job deadline: 45 seconds.
  Cancellation or overflow terminates the child tree and waits up to five seconds
  for confirmed exit. Unconfirmed termination retains input and reports its path.
  Partial stdout is never published.
- The result belongs to the original capture reference. UI integration must guard
  source replacement, pending edits and cancelled generations before applying it.

## Evidence

The envelope must match protocol, sample rate/count, complete settings and channel
mask. Event sequence is separate from annotated time: TRANSFER and FRAME summaries
legitimately refer back to earlier starts.

SPI BITS/DATA must agree with each other, raw clock edges and raw data levels.
CS changes must match the physical source; TRANSFER must match accumulated words
and its release boundary. Incomplete words remain absent. An accelerated clock
may make an estimated word end overlap the next word start. Preserve the native
estimate; it is not a raw-edge timing measurement.

UART DATA must agree with native bits and sampled raw levels. Start, parity, stop
and frame status must agree. Noninteger samples per bit can make adjacent
annotations overlap by one sample. The inverted Python path can encode bits as
booleans; only bit positions accept that representation. Upstream parity-error
placeholder numbers are not presented as measured expected/actual values. An
invalid-start FRAME can carry a stale value; the adapter discards it. Break keeps
its own interval, separately from the preceding frame. EOF stays incomplete.

## Checks and remaining work

Opt-in checks use `--native-serial <configuration>` and
`--serial-fixtures <saved-evidence-directory>`. They replay saved native output
against raw planes and run fresh sparse-channel 9-bit SPI/UART stimuli, optional
SPI roles, repeated/independent instances, invalid UART start and accelerated SPI
clock. Analysis save/reopen preserves direction, values, roles and selection.

Separate process checks use a named-pipe handshake to confirm a live child before
cancellation. They check cancellation, stdout/stderr overflow, failed exit and
startup failure, including owned-input cleanup and unrelated-file preservation.
The 45-second timeout and OS termination failure were not injected.

App protocol settings, native controls, real capture throughput, runtime packaging
and owner usability remain unverified. Synthetic and programmatic checks do not
count as owner acceptance.
