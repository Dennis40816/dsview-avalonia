<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Selected USB probe

This executable tests the real pinned DSLogic factory and driver failure paths
with a controlled USB transport. It performs no physical USB operations. A valid
inactive candidate is not evidence of a successful hardware connection.

The source pin is `2e9e2c8e726df4ef5687d39b83d4f797cc44b574`.
Build a direct-owner probe first as described in [CONNECTION-PROBE.md](CONNECTION-PROBE.md),
then use its reviewed dependency and source staging:

```powershell
python tests/native/build-selected-usb-probe.py `
  --base <direct-owner-build> --upstream <pinned-DSView-checkout> `
  --compiler-root <mingw-root> --glib-root <glib-root> `
  --extra-root <libusb-and-zlib-root> --output <fresh-output>
python tests/native/check-selected-usb-probe.py `
  --worker <fresh-output>/bin/selected-usb-probe.exe `
  --output <fresh-results> --runtime-dir <glib-bin> `
  --runtime-dir <libusb-and-zlib-bin> --runtime-dir <mingw-bin>
```

The builder checks the base manifest and original source hashes, extracts the
additional DSLogic files from the exact Git revision, and applies the small
connection guard patch in isolated staging. The build manifest records original
and patched hashes separately, product inputs, compiler command, log result and
binary hash. These records are not a runtime distribution manifest or full SBOM.

Twenty separate child processes cover:

- Ready and unknown-topology candidates, with selected bus 200 and 16 channels.
- Missing or ambiguous location, enumeration/descriptor failure, changed revision
  or port, disconnection, unsupported product and unsupported speed.
- Open denial and string-read failure, without incorrectly requesting firmware.
- Firmware-required, explicitly requested firmware success requiring re-enumeration,
  and firmware failure. The upload fixture counts requests; it transfers no firmware.
- Real driver open denial, incompatible firmware and short control PRE/read results.
  These must preserve failure, destroy the candidate and stop before interface claim.

Each process checks that no unselected device was opened or received a firmware
request, exactly one enumeration occurred, and all retained device references
were returned. Every stdout/stderr is saved before assertions. Child exit is
required and each process has a ten-second test deadline.

The last four cases call the real driver through `device-owner.c`. The USB
functions supply controlled responses, not real-device evidence. Interface claim,
unplanned control traffic and global scanning abort the fixture. Successful
hardware open/configuration, FPGA loading, acquisition, hotplug races and handle
leak detection remain outside this test. The firmware directory flag is a caller
contract, not an implemented runtime resource admission policy.
