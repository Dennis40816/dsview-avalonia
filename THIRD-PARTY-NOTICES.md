<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Third-party sources and review status

License clearance is incomplete. The native offline workbench now restores
NuGet dependencies, including a font package, for local development. Generated
binaries and restored packages are not checked into this repository. No device
firmware or customer capture files are bundled. This document is an evolving
inventory, not a completed runtime SBOM or binary distribution notice bundle.

## Current offline workbench dependencies

| Direct dependency | Pinned version | Source of this inventory |
| --- | --- | --- |
| Avalonia.Desktop | 12.1.1 | `src/DSView.Desktop/DSView.Desktop.csproj` and `packages.lock.json` |
| Avalonia.Themes.Fluent | 12.1.1 | Same project and lock file |
| Avalonia.Fonts.Inter | 12.1.1 | Same project and lock file; contains the Inter font dependency |
| CommunityToolkit.Mvvm | 8.4.2 | Same project and lock file |
| .NET SDK | 10.0.303, latest-patch roll-forward | `global.json`; target framework `net10.0` |

Restore uses nuget.org with package source mapping. Per-project lock files
record resolved transitive dependencies and content hashes. Package and asset
license texts, copyright notices, native runtime assets and font provenance
still need a complete distribution inventory before a binary release. Listing
a dependency here does not establish distribution clearance.

The UI also requests installed Cascadia Mono/Consolas fonts as system fallbacks;
this source tree does not bundle their font files. NVT Core theme code and the
NVT-specific parser are not included. The embedded demo JSON is project-authored
synthetic data, not a device/customer capture. The default examples have
precomputed annotations. The optional raw I²C example contains no annotations;
an explicitly configured external development worker generates them at runtime.

## Present references

| Item | Source and revision | Treatment |
| --- | --- | --- |
| GNU GPL version 3 text | [Free Software Foundation](https://www.gnu.org/licenses/gpl-3.0.txt), version 3, 29 June 2007 | Stored verbatim as `LICENSE`; the license document retains its own notice. Original project material is offered under GPL-3.0-or-later. |
| DSView | [DreamSourceLab/DSView](https://github.com/DreamSourceLab/DSView/tree/2e9e2c8e726df4ef5687d39b83d4f797cc44b574), commit `2e9e2c8e726df4ef5687d39b83d4f797cc44b574` | Referenced by the `external/DSView` gitlink. Upstream copyright and license notices remain with upstream files. No upstream content is copied into this repository's own source tree. |

DSView's [root COPYING](https://github.com/DreamSourceLab/DSView/blob/2e9e2c8e726df4ef5687d39b83d4f797cc44b574/COPYING)
contains GPLv3. Individual files have different notices. The root text is not a
substitute for checking each component selected for a future build or source bundle.

## Incomplete upstream review

A preliminary review of 408 selected source and license files at the pinned
revision found 20 files without an explicit license grant in their headers.
This was a limited header review, not clearance of the complete repository,
its assets, or a final build.

The unresolved set comprises 12 MiniZip source/header files, two generated
configuration headers, one capture test, two `pxx1` decoder files,
`spi_tpm/RangeDict.py`, and two `st7789` decoder files. MiniZip provenance and
the additional sources named by `crypt.h` need further verification.
Decoder metadata alone is not sufficient permission evidence.

The planned initial decoder candidate is `1-i2c` (`1:i2c`). The `fsi`, `pxx1`,
`st7789`, and `spi_tpm` decoders are outside the proposed runtime scope.
They remain in the upstream source checkout. No runtime packaging allowlist
or native decoder binary is shipped in this offline workbench.

The optional `NativeI2cSource` adapter invokes a separately built worker using
`libsigrokdecode4DSL` and the pinned `1-i2c` Python decoder. A local Windows
experiment uses a candidate runtime patch, Python 3.12.10, GLib 2.90.1,
gettext-runtime 1.0, PCRE2 10.49, libiconv 1.19 and winpthreads with GCC 15.1.0.
Those experimental files, binaries, runtime packages and machine configuration
are outside this source repository. The worker and two decoder Python files
are hash checked before execution; dependent DLLs and Python modules still
require a complete runtime inventory. Process separation does not establish
license clearance. See [the development boundary](docs/NATIVE-I2C.md).

## Before a binary release

- Map actual compiled files, included headers, runtime modules, and helpers to
  their source versions, copyright notices, and licenses. Resolve ambiguous files
  or omit them from the selected build and distribution.
- Verify the pinned .NET/Avalonia package graph and selected packaging mode.
  Inventory native capture and decoding dependencies when they are connected;
  the current offline build does not link the DSView native libraries.
- Audit the actual font, icon, and firmware assets if they are added. Preserve
  their separate license texts and applicable notices.
- Generate the final file manifest and SBOM. Prepare matching source, build
  scripts, patches, license texts, and other applicable distribution materials.
  A submodule URL alone is not the planned source delivery mechanism for binaries.

This review status will be updated as concrete components are selected.
