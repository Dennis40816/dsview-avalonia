<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# Third-party sources and review status

License clearance is incomplete. This bootstrap has no application binaries,
dependency packages, fonts, icons, device firmware, or customer capture files.
This document is a source-reference inventory, not a completed runtime SBOM or
binary distribution notice bundle.

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
or implementation is shipped in this bootstrap.

## Before adding a build or binary release

- Map actual compiled files, included headers, runtime modules, and helpers to
  their source versions, copyright notices, and licenses. Resolve ambiguous files
  or omit them from the selected build and distribution.
- Pin actual versions and packaging modes of .NET, Avalonia, native capture and
  decoding dependencies, and all transitive packages. None are restored or
  bundled by this bootstrap.
- Audit the actual font, icon, and firmware assets if they are added. Preserve
  their separate license texts and applicable notices.
- Generate the final file manifest and SBOM. Prepare matching source, build
  scripts, patches, license texts, and other applicable distribution materials.
  A submodule URL alone is not the planned source delivery mechanism for binaries.

This review status will be updated as concrete components are selected.
