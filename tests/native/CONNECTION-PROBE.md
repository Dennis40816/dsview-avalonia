<!-- SPDX-FileCopyrightText: 2026 Dennis40816 -->
<!-- SPDX-License-Identifier: GPL-3.0-or-later -->

# Native connection behavior probe

This optional experiment runs the real pinned `lib_main.c` and saved-session
driver. It exposes behavior hidden by a fake process runner: fallback to a
different device, partial initialization reported as success, and stale native
information after release. It is not installed in the App and does not implement
USB connection, capabilities, configuration writes or acquisition.

The build stages 23 unmodified source/header/license files from DSView revision
`2e9e2c8e726df4ef5687d39b83d4f797cc44b574` into a **new** output directory. Its
generated configuration leaves the hardware/demo driver table empty. Original
libusb initialization and management threads still run, but no hardware driver
can scan/open an instrument or upload firmware. Generic input/output modules are
absent; unexpected calls to hardware option parsers terminate with an error.

Use existing reviewed MinGW-w64, GLib and libusb/zlib development packages:

```powershell
python tests/native/build-connection-probe.py --upstream C:/source/DSView --compiler-root C:/toolchains/mingw64 --glib-root C:/deps/glib/ucrt64 --extra-root C:/deps/usb-zlib/ucrt64 --output C:/work/native-connection/build
python tests/native/check-connection-probe.py --worker C:/work/native-connection/build/bin/connection-probe.exe --fixture 'C:/Program Files/DSView/demo/logic/protocol.demo' --output C:/work/native-connection/checks --runtime-dir C:/deps/glib/ucrt64/bin --runtime-dir C:/deps/usb-zlib/ucrt64/bin --runtime-dir C:/toolchains/mingw64/bin
```

The check requires fixture SHA-256
`9df041ddae3fd0873a0f327986528d1117c35cf9c4e31f91c4032b607f322cc7`.
It stages private copies, moves only its own files after native registration to
induce real open failures, and retains JSON traces, stderr and result counts.
The installed fixture is read-only. Each worker is controlled through redirected
stdin/stdout; command-pipe EOF releases the session. Failure/timeout terminates
the known child and waits for exit. Run Python with assertions enabled.

Nine grouped checks cover six child sessions: independent expected metadata
(25 MHz, 131072 samples, physical channels 0–15), same-handle reopening, stale
generation, release, orderly teardown, failed switching, failed first opening,
invalid rate readback, parent EOF and unsupported command teardown. These are
nine grouped behavior checks, not nine distinct hardware scenarios.

`connected` in a trace means only that the **file session** passed this experiment's
identity/status/activation/readback guards. It never means hardware capture-ready.
The file-only probe reads borrowed pointers on a single control thread, with no
device removal command. This cannot be copied unchanged into a USB/hotplug
backend: that requires a synchronized owned snapshot and explicit worker/session
generation, including revocation before activation or release begins.

The native build retains upstream compiler warnings. Its manifest identifies the
source and binary; it is not a dependency SBOM or redistribution approval.
