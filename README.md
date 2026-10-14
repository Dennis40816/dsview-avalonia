<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# dsview-avalonia

An independent project to develop an Avalonia logic analyzer frontend using
DSView's acquisition and decoding libraries.

[繁體中文](README.zh-TW.md)

## Status

Repository bootstrap. **License clearance is incomplete; no binary releases yet.**

This revision contains licensing and repository documentation plus a pinned
upstream source reference. It does not contain a runnable Avalonia application,
a native bridge, a decoder implementation, or a verified build pipeline.

The planned interface puts waveforms first, with readable typography, layered
decoder annotations, expandable packet contents, and named time measurements.
Those are development goals, not implemented capabilities of this revision.

Before binary distribution, the project must resolve upstream source and notice
gaps, determine the actual build and runtime dependencies, and produce a software
bill of materials (SBOM). See [third-party review status](THIRD-PARTY-NOTICES.md).

## Get the sources

```sh
git clone --recurse-submodules https://github.com/Dennis40816/dsview-avalonia.git
cd dsview-avalonia
git -C external/DSView rev-parse HEAD
```

The expected upstream revision is:

```text
2e9e2c8e726df4ef5687d39b83d4f797cc44b574
```

For an existing clone:

```sh
git submodule update --init --recursive
```

The `external/DSView` gitlink fixes the revision. Do not use
`git submodule update --remote` when reproducing this revision. GitHub's automatic
source ZIP does not include the submodule checkout; use Git for the complete
source reference.

The submodule contains the full upstream source tree, including components that
are not selected for this project's planned runtime. Its presence is not a
runtime packaging allowlist or a completed license review. No upstream files
are copied into the frontend's own source tree.

## License

Original project code, configuration, and documentation are licensed under
**GNU GPL version 3 or, at your option, any later version**
(`GPL-3.0-or-later`). See [LICENSE](LICENSE), [NOTICE](NOTICE), and the
[SPDX header rules](docs/SPDX.md).

Third-party sources retain their own licenses. The root license does not
relicense the DSView submodule or its dependencies.

This project is not affiliated with or endorsed by DreamSourceLab and is not
an official successor to DSView.
