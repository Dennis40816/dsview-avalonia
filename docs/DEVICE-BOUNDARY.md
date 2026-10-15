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

### Verified native management failures

The optional [file-session connection probe](../tests/native/CONNECTION-PROBE.md)
now exercises the pinned management API and real saved-session driver, with an
empty hardware driver table. It does not simulate `ds_active_device` or replace
the session driver's open/close/configuration functions. Six child sessions pass
nine grouped behavior checks, including these observed failure cases:

| Trigger | Actual upstream result | Required behavior |
| --- | --- | --- |
| Open a registered file after its private test copy was moved, while another session was open | `SR_OK`, but the actual active handle belongs to the earlier session | Reject the selected connection and revoke the earlier operation generation. |
| Move the first registered file before opening it | `SR_OK` and the expected handle with `SR_ST_ACTIVE`, but zero successful activations | Reject partial initialization; matching handle and active status are insufficient. |
| Release a successfully opened session | `SR_OK`; active-info still contains the old handle, now `SR_ST_INACTIVE` | Revoke the lease before release; returned identity is not proof of an open connection. |
| Open metadata with zero sample rate | Open succeeds, but configuration readback is unusable | Reject the result before defining waveform timing. |
| Reopen the same native handle | The activation count changes | Earlier operations remain stale even when the handle value matches. |

The first-open failure follows from the session driver's early assignment of
`SR_ST_ACTIVE`, followed by the management layer retrying that same partially
initialized instance. This is verified for the file driver, not asserted as a
reproduced hardware failure. Normal readback independently matches the installed
fixture header: 25 MHz, 131072 samples and physical channels 0–15. EOF and explicit
close both run native cleanup and terminate the child. No capture runs here.

The probe's guards reject the demonstrated cases; they do not recover the original
open attempt's error once the global API has overwritten it with fallback success.
The native [device owner](../tools/native/device-owner.h) now retains the original
result by calling the selected instance's driver directly once. It consumes an
unregistered inactive instance, revokes the previous generation before opening,
and destroys a partially opened instance on failure. There is no fallback device.
The original open result is retained separately from validation and cleanup.

Readback uses that same owned instance and returns a value snapshot containing
driver/type/mode, sample rate/limit and enabled physical channel IDs. A failed
readback clears the output and revokes the connection. Closed snapshots remain
historical values; their generation cannot authorize new operations. This is a
small native component, not an App device session.

Ten direct-owner checks pass against the real saved-session driver, including
the previously demonstrated first-open and switching failures. That probe does
not link `lib_main.c` or initialize its manager/hotplug threads. The original nine
global-manager checks still reproduce the old behavior separately. No hardware
driver is substituted with a successful fake. Readback currently proves driver
configuration metadata, not an independent read of physical hardware registers.

The owner requires one native control thread and exclusive instance ownership.
USB integration must preserve that rule with synchronized owned snapshots: public active-info exposes
a borrowed device pointer after releasing its internal lock, and configuration
getters use global state without the same lock. File-only probe pointer access
must not be reused unchanged with USB hotplug.

Further source-only findings remain to be exercised on hardware: DSLogic scan
accepts `SR_CONF_CONN` as a bus/address filter, but global initialization passes
no filter; `sr_usb_find` rejects buses above 64 although descriptor bus numbers
have a wider range. Firmware/profile detection can open the device and initiate
firmware loading. The lower open path can retain `SR_ST_INCOMPATIBLE` for a firmware
version mismatch. The selected-device preparation component below now provides
the native selection boundary; worker/App integration remains pending.

### Selected USB preparation

`tools/native/selected-usb.c` retains one libusb device reference after matching
a unique bus/address and its descriptor, speed and known port path. It performs
no opens or transfers. Unknown topology stays unknown. Rechecking uses the retained
object, not a global scan or the upstream bus-64-limited connection filter.
This reference is valid only within its libusb context and is not persistent
physical identity across unplugging or re-enumeration.

`tools/native/dslogic-selected.c` is compiled after the pinned DSLogic source in
the same translation unit. It reuses the upstream profile table and instance
factory, without calling its scan function. Only the retained device is opened
for profile strings. Access and read failures are separate from missing firmware.
Firmware loading requires an explicit caller flag and resource directory; success
returns re-enumeration-required with no candidate. Firmware admission/provenance
and re-selection still need integration before this can be used from the App.

A ready profile produces an inactive candidate for the direct device owner.
It is not a connected or capture-ready device. The selection must outlive the
candidate and owner; destroy them before releasing the retained reference.

`tools/native/dsl-connection-guards.patch` stops an incompatible firmware version
before interface claim/configuration and requires exact control-read lengths.
The patch applies to isolated pinned sources; it does not modify the upstream
checkout. See [the selected-USB probe](../tests/native/SELECTED-USB-PROBE.md) for
reproduction and the distinction between controlled transport and real hardware.

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
Revoke the previous generation before attempting activation or release; a failed
switch may leave the previous native instance open. Do not expose that fallback
as success for either the new selection or an old, already invalidated operation.

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
