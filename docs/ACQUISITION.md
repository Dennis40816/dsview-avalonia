<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Capture acquisition lifecycle

`AcquisitionSession` now provides a background acquisition pipeline and a paced
raw Clock, I2C + Interrupt, UART and SPI Demo sources. The App wires them through an independent acquisition
view model, setup pane and Start/Stop/Review action. Native UI interaction and
layout acceptance are still pending. The native USB preparation component is not
yet an acquisition source. Existing offline example loading remains separate.

## Source contract

An `IAcquisitionSource` provides an immutable `AcquisitionPlan`: source kind/name,
sample rate, finite sample limit and ordered physical channel IDs/names. Hardware
adapters must construct it from verified applied configuration. Validation of a
plan's shape is not hardware readback or capability validation.

After configuration, the source calls `Running` once. It delivers packed digital
samples, with each uint bit corresponding to its physical channel ID. Disabled
channels must be zero. The first sample is position zero. The writer requires
continuous positions, finite limits and valid batch lengths. Source callbacks
call `TryWrite` before releasing borrowed native buffers; it copies the samples.

The writer never waits for consumer queue capacity. Saturation returns false,
records Overflow and seals ingress. A source must stop when the writer refuses
data. A cooperative Demo producer can await capacity before writing. This is not
a guarantee of lock-free or allocation-free native callback execution.

The source task must return only after all its callbacks and transfers have
ended. A data-end packet alone cannot fulfill that contract. Native worker
adapters will need bounded stop/exit handling; the managed session cannot forcibly
terminate a producer that ignores cancellation and refuses to return.

## Ownership and completion

One session owns one producer, queue and consumer. Repeated `RunAsync` calls return
the same task. `Progress` is a polled value snapshot; it never exposes mutable
waveform lists or raises per-sample UI notifications. Each session has a unique
operation ID. The App owner matches that ID and its operation generation before
publishing results, and awaits cancellation before file source replacement.

| Operation or outcome | Behavior |
| --- | --- |
| Stop | Signal the producer, wait for its teardown, drain admitted batches and retain the actual prefix. |
| Cancel / disposal during acquisition | Close ingress, signal abort, wait for teardown and discard the capture. A late successful source return cannot publish it. |
| Completed | Require confirmed Running and the exact configured sample count. Premature completion becomes DataLoss. |
| Disconnected, Overflow, DataLoss or Failed | Keep the valid prefix with its reason. Compatible partial results require explicit acceptance rather than automatic publication. |
| Empty or fewer than ten samples | Never pad. Empty input has no capture; 1–9 samples remain available in the result but are incompatible with the current workbench/file minimum. |

AcceptedSamples counts data admitted to the bounded queue. RetainedSamples counts
the usable stored prefix, which can be smaller when transition storage overflows.
Cancelled results retain zero samples and have no raw capture. Result captures
contain readonly signal/edge collections, no prefilled packets and no guessed
default decoders. Source/termination/count information is included in their
source description. Structured acquisition metadata persistence is still pending.

## Bounds and loss policy

Defaults are four queued batches of 4096 uint samples (64 KiB queued payload),
plus producer/consumer buffers and at most 2,000,000 transitions. Each plan is
limited to 32 physical channels and 64,000,000 samples. Transition conversion
visits changed channel bits rather than scanning every channel at each sample.
Final snapshots temporarily coexist with the transition lists; the session then
releases its lists. These bounds are not measured USB throughput or rendering FPS.

This first pipeline retains continuous prefixes. A missing or overlapping batch
ends the prefix; it never joins data across an unknown interval. Retaining resumed
segments with explicit level evidence/gaps remains future work. The same applies
to continuous/repeated acquisition, trigger metadata, disk-backed storage and
incremental rendering/decoding.

## Demo scenarios and checks

`DemoClockSource` generates raw clock samples for a chosen physical channel,
sample rate/limit and integer half-period. It uses the same writer/session as
future hardware adapters. Delivery is paced by batch duration and cancellation
interrupts pending timers. Sub-millisecond delays may complete immediately under
.NET timer resolution; sample positions and frequency always derive from the
sample grid, never elapsed wall time. All scenarios share this small sample pump.

`DemoI2cSource` generates SCL, SDA and active-low INT_N on three configurable
physical channels. Each repeated transaction writes register 0x10 at address
0x2A, issues a repeated START, reads 0x5A and 0xC3, then ends with NACK and STOP.
The interrupt begins before START and ends after STOP. A quiet interval separates
transactions. The App attaches the explicitly configured SCL/SDA roles, but no
packets. **Decode I2C** sends the acquired signals through the existing decoder;
it requires a configured native I2C runtime. This is an explicit action after
publication, not continuous decoding.

`DemoUartSource` emits an idle-high TX signal on a configurable physical channel.
Each burst contains 0x55, 0xA5, 0x00 and 0xFF as consecutive 8N1 frames, LSB first.
Eight idle bit slots precede a burst and sixteen follow it. Baud rate must fit
at least four whole samples per bit; for 115200 baud, 1152000 samples/s is one
valid choice. Odd counts such as nine samples per bit also work. This restriction
belongs to this deterministic generator, not to imported UART capture analysis.
The App configures the known TX role and baud, and **Decode UART** runs the shared
native decoder. The button follows the current capture even when the next
capture's draft changes.

`DemoSpiSource` generates four physical signals: CLK, MOSI, MISO and active-low CS.
The fixed format is mode 0, 8 bits, MSB first. Each transfer sends 0x9F, 0x00 and
0xA5 while receiving 0x00, 0xEF and 0x17. CS asserts before the first clock and
releases after the last clock; a quiet interval separates transfers. Data changes
on low CLK. The App configures all four explicit roles and the known format, then
**Decode SPI** uses the existing native adapter. These are public synthetic words,
not an emulation of a particular device or a hardware identification result.

The standalone checks depend only on Domain/Infrastructure, without Avalonia or
native hardware runtimes:

```powershell
dotnet build tests/DSView.Acquisition.Checks/DSView.Acquisition.Checks.csproj --artifacts-path <output>
dotnet <output>/bin/DSView.Acquisition.Checks/debug/DSView.Acquisition.Checks.dll
```

They cover buffer reuse before any consumer runs, batch boundary transitions,
D0/D31 among 32 channels, queue and transition limits, malformed input, empty and
short data, marked partial outcomes, waiting for teardown, late results after
abort, and the Demo sources with manually advanced timers. Timing assertions
use independent sample positions; tests do not sleep for an expected duration.
These are managed acquisition behavior checks, not App interaction or hardware
acceptance. Additional checks in `DSView.Checks --acquisition-workflow-only` exercise
the App owner, including source replacement, partial acceptance, pending edits and
save/reopen. `--demo-workflow-only --native-i2c <configuration>` additionally
checks actual I2C decoding, independent expected bytes/timestamps, cross-channel
cursors, remapped pins/rate and a capture ending inside a byte. Acquisition may
complete its requested sample count while its last protocol transaction remains
incomplete; no trailing byte, NACK or STOP is invented. Add
`--native-serial <configuration>` to that command to check UART acquisition,
native decoding, exact frame starts, changed baud/pins, save/reopen and capture
ending before the stop-bit center. It also checks full-duplex SPI words, exact CS
intervals, CS-to-clock measurement, independent MISO selection and truncated words.
Seeing a data word is not proof of a complete UART frame or SPI transfer. Native UI interaction,
structured saved metadata and live display performance still require evidence.

## App workflow

The header's **Demo** button opens Capture setup. It offers Clock, I2C + Interrupt,
UART 8N1 and SPI Mode 0 scenarios with physical channels, sample rate and duration.
Clock/I2C/SPI show clock frequency; UART shows baud rate. Each scenario shows only
its own channel roles; hidden roles do not participate in validation. The
defaults request one second at 1 MS/s with a 10 kHz clock. Invalid or fractional
sample timing cannot start. Switching scenarios preserves the entered timing.
I2C requires three distinct channels and four whole sample intervals per cycle.
No USB device is presented as ready by this screen.

**Start** changes to **Stop** during acquisition. The existing waveform remains
available for inspection. Completion validates a new analysis off-thread before
publication; unsaved analysis uses the existing save/discard/cancel question.
Current source identity, operation generation and pending edits are checked again
after asynchronous preparation. A changed view during preparation keeps the new
capture pending instead of overwriting that change.

Stopped or incomplete data opens the pane with **Use partial capture** and **Keep
current analysis**. A retained result remains reachable through **Review** if the
pane is closed. **Cancel acquisition** discards new samples and waits for producer
cleanup. Closing the window with an active or pending acquisition asks before
discarding it. File picker/save/replacement prompts inhibit automatic publication.
Starting another acquisition is also inhibited while the shared decoder runs.

The setup pane shares the existing right-side context area with decoder settings
and packet details. It adds no permanent measurement row. A small header progress
bar polls retained samples at 100 ms intervals; it does not yet render incoming
waveforms. Applying a capture shows All signals so a selected high-numbered Demo
channel is visible. Existing Group preferences remain available.

For native acceptance, check D0 and D31, Start/Stop/Review, numeric errors, short
stops, unsaved-analysis decisions, file dialogs, close/cancel, large fonts and
minimum window width. Verify cursor measurements against the selected clock and
save/reopen the resulting analysis. Compiled bindings and view-model checks do
not substitute for those actions in the real App.
