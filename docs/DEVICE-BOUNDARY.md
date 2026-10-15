<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Device discovery, connection and acquisition boundaries

The current development backend can observe DreamSourceLab USB descriptors.
It cannot yet connect/configure a capture device or acquire live samples. The
App remains an offline workbench; descriptor observations are not capture-ready
devices. No device selector or Start control is enabled by this backend alone.

## Why discovery is separate

The following source observations refer to DSView revision
`2e9e2c8e726df4ef5687d39b83d4f797cc44b574`:

| Source | Observed behavior | Required boundary |
| --- | --- | --- |
| `libsigrok4DSL/lib_main.c:115,171,1325` | `ds_lib_init` scans hardware through driver `scan` and starts a hotplug thread. | Initialization is an active operation, not passive discovery. |
| `libsigrok4DSL/hardware/DSL/dslogic.c:312,442` | Scan can upload firmware when the configuration profile is absent. | Keep firmware/driver initialization out of automatic descriptor refresh. |
| `libsigrok4DSL/lib_main.c:339` | `ds_active_device` can fall back to another device; its returned result may be the fallback's success. | Verify actual active handle/type/driver after activation. A successful return alone cannot authorize capture. |
| `libsigrok4DSL/lib_main.c:284` | The list contains opaque handles/names and has a special empty-list branch. | Initialize count/output before calling, own/free the copy, and scope handles to one worker lifetime. Do not infer USB identity from the display name. |
| `libsigrok4DSL/lib_main.c:937,955,973` | Active-device get/set/list delegate to driver configuration. | Query live capabilities and read settings back on the confirmed device. Profile declarations are not applied configuration. |
| `libsigrok4DSL/lib_main.c:721,780` | Start schedules a collect thread; driver start and subsequent events happen later. | Distinguish accepted command, running transfer and completed capture. |
| `libsigrok4DSL/lib_main.c:840` | Stop requests session stop and can join the collecting thread. | Stop on a control thread, never from the data callback itself. |
| `libsigrok4DSL/lib_main.c:1146,1593` | Data forwarding is synchronous; some events run on separate callback threads. | Copy borrowed data before returning, serialize worker output and reject stale operation events. |
| `libsigrok4DSL/hardware/DSL/dsl.c:2161,2345` | Logic packets refer to transfer buffers; end-of-data is a distinct packet. | A stop acknowledgement is not proof of complete data. Retain final counts, packet status and termination cause. |

These are source findings, not runtime certification of driver behavior or
thread safety. Connection, readback, stop/disconnect and throughput still need
integration tests and the actual device.

## Implemented descriptor-only observation

`tools/native/usb-discovery.c` is a small Windows worker. It loads a reviewed
libusb DLL by absolute path, searching dependencies only in its directory and
the Windows system directory. Its resolved APIs initialize a libusb context,
enumerate descriptors, inspect bus/address/speed/port paths and release that
context. It does not resolve device-open, claim-interface or transfer APIs and
does not load libsigrok or firmware. It does not query device string descriptors.

Only vendor `0x2A0E`, as used by the pinned DSL drivers, is returned. An unknown
product is kept as a descriptor observation; it is not assigned a guessed model,
firmware, number of channels or sample-rate capability. Bootloader variants not
using that vendor, other vendors, permissions and driver-access readiness are
outside this observation's claims.

Each response has a fresh request ID, explicit scope, completeness and unreadable
descriptor count. Failure to read any enumerated descriptor makes the snapshot
incomplete, even when no matching devices were readable. Initialization and
enumeration failure exit unsuccessfully rather than returning an empty success.
Unknown port topology remains null. USB addresses/ports may be reused and never
serve as persistent identity or an open-device lease.

The managed adapter verifies worker and DLL hashes before starting an isolated
process. It uses the same bounded process lifetime as serial decoding, with no
unneeded sample input file. Cancellation confirms child exit before returning;
partial stdout is never published. The default process deadline is 45 seconds,
stdout is bounded to 4,000,000 characters and stderr to 65,536. The discovery
envelope itself is limited to 65,536 characters, 64 matching devices, 4,096
enumerated descriptors and seven port levels. Unknown/duplicate/missing JSON
fields, request mismatch, duplicate locations and invalid values are rejected.

Hash checks are runtime provenance checks, not a complete SBOM or protection
against arbitrary changes to the surrounding machine. No binary runtime is
bundled or released.

## Development configuration and checks

`native-usb.json` has four exact, required properties:

```json
{
  "Worker": "C:/reviewed/runtime/usb-discovery.exe",
  "WorkerSha256": "<64 hexadecimal characters>",
  "Library": "C:/reviewed/runtime/libusb-1.0.dll",
  "LibrarySha256": "<64 hexadecimal characters>"
}
```

Build the worker with a Windows MinGW-w64 compiler and reviewed libusb headers:

```sh
gcc -std=gnu11 -O2 -Wall -Wextra -static-libgcc -municode -I/path/to/libusb-headers tools/native/usb-discovery.c -o /development/output/usb-discovery.exe
```

The local experiment uses libusb 1.0.30. Its upstream header states LGPL-2.1-or-later;
the original wrapper/fixture are GPL-3.0-or-later. Headers, DLL, compiler runtime
and their full distribution obligations still require release review.

The checks executable accepts `--usb-discovery-only --native-usb <config>`.
`--usb-fixture <dll>` additionally exercises the actual worker against
`tests/native/usb-discovery-fixture.c`, built as a separate test DLL. That fixture
never accesses USB. Its `DSVIEW_USB_FIXTURE` environment variable controls empty,
partial, missing-topology and initialization/enumeration failure cases in the
child process only. Controlled fixture observations must not be presented as
hardware findings. The real configured DLL is queried separately, with its count
and completeness printed in the check log.

## Next connection and capture work

Keep one owner for the worker/device lease and one operation generation. A
refreshed descriptor snapshot cannot silently replace an active lease. On
explicit connection, bind the actual device identity, verify activation readback,
then query supported mode/rate/channel/trigger combinations. Never select a
firmware from a marketing name alone. Firmware-induced re-enumeration must
invalidate the old handle and preserve uncertainty until identity is confirmed.

Capture configuration is separate from Group visibility. Only verified applied
settings may define the raw sample rate and enabled channels. A data callback
must copy bounded borrowed data into owned storage before returning; decode and
render work must not block USB callbacks. Queue limits need explicit overflow
behavior. Do not concatenate across a missing interval or invent sample times.

Separate Stop from Cancel/discard policy. Record whether capture ended normally,
was stopped by the user, disconnected, overflowed or failed. An incomplete prefix
must retain its actual sample count and uncertainty; neither an end packet nor
a successful stop call alone establishes a full requested capture. Very short
captures also need an explicit policy before using the workbench's current
minimum-sample constraints. Preserve the previous committed analysis until a new
capture is validated and the user-facing partial-result policy is satisfied.

Use controlled event sequences for lifecycle failures and native/demo sources for
packet ownership/transport checks, then real hardware for readback, trigger,
disconnect and sustained acquisition. Keep these evidence levels separate.
