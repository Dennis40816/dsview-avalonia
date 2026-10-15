<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Demo acquisition mode

Demo is a required first-release acquisition source. It must work without a USB
instrument or hardware driver installation. The existing offline examples load
complete captures; they do not yet implement the acquisition lifecycle below.

The [acquisition pipeline](ACQUISITION.md), Clock, I2C + Interrupt, UART 8N1 and SPI Mode 0 sources are
implemented and behavior-tested. App controls connect scenario setup,
Start/Stop/Review, partial-result acceptance and safe publication. Protocol scenarios
produce raw signals; the explicit **Decode** action uses the shared native
decoders. Known bytes, repeated START, final NACK, exact timing, UART 8N1 bursts,
full-duplex SPI, truncation and save/reopen are verified end to end. Native UI acceptance, live waveform
preview and a distributable decoder runtime remain open. Raw Demo generation
itself does not require a hardware driver or the installed DSView demo assets.

## User workflow

Select **Demo** in the same source selector used for devices, choose a signal
scenario, configure the advertised channels/rate/duration, then start or stop.
Keep a compact Demo source label visible. Do not present a simulated DSLogic Plus
as an enumerated instrument. Do not silently switch from a failed device to Demo.
Source switching must cancel the previous operation and reject its late results.

The shared workbench provides Group selection, waveform zoom, adjacent-edge
measurement, freely positioned or edge-snapped A/B cursors, protocol setup,
packet inspection and save/reopen. Demo needs no separate analysis screen.
Group visibility remains independent of channel acquisition enablement.

## Pipeline and evidence

Demo emits bounded raw sample batches through the same capture-session contract
as hardware. Both sources use owned buffers, operation generations, truthful
sample timing, termination status and the shared capture publication boundary.
Decoders consume those raw samples through the existing adapter boundary;
acquisition-mode protocol results must not be prefilled from canned packet text.

Use deterministic public synthetic scenarios for a clock, I2C with interrupt,
SPI and UART. Add missing intervals and protocol errors as explicit scenarios.
Keep saved-example replay available as a separate workflow. Imported upstream
demo assets require their own provenance and distribution review; Demo must not
depend on a private installed DSView directory in a released build.

Demo advertises only implemented settings. Trigger behavior must be implemented
and verified before its controls are enabled. It does not claim physical voltage,
USB throughput or hardware trigger accuracy. Stress mode uses explicit channel,
sample and batch sizes and remains distinguishable from ordinary examples.

## Acceptance

- A machine without a device can select Demo, start, see samples, stop and retain
  the actual captured prefix with its termination status.
- Start/stop/restart, cancellation and source switching cannot publish stale data
  or overwrite the previous committed analysis with a failed capture.
- Independent expected timing and bytes agree with cursor measurements and
  I2C/SPI/UART results decoded from the generated samples.
- Empty, very short, partial, gapped and overflowed captures follow the same
  explicit retention policy as hardware; absent data is never filled in.
- Saving and reopening retain source provenance and sample timing.
- Bounded queue/memory behavior and rendering during acquisition are measured
  across data sizes. Demo results are reported separately from USB performance.

Implement the shared session/lifecycle boundary first, then a small Demo source
and the hardware adapter. Reuse a native demo driver only if its lifecycle,
dependencies and distribution fit this contract. No Core source is required
until a reviewed export package is available.
