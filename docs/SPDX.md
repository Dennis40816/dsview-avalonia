<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->

# License identifiers and file notices

Original project code, configuration, and documentation use
`GPL-3.0-or-later`. Keep the full GPLv3 text in the root [LICENSE](../LICENSE).
The project's notice in [NOTICE](../NOTICE) explicitly offers version 3 or any
later version. The identifier distinguishes this grant from GPL-3.0-only.
See the [official SPDX entry](https://spdx.org/licenses/GPL-3.0-or-later.html).

## New original files

Use a comment syntax supported by the file. Record the actual copyright holder
and applicable year. For example, a new C# or C/C++ file may start with:

```csharp
// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
```

Markdown and XML files use a comment block:

```xml
<!--
SPDX-FileCopyrightText: 2026 Dennis40816
SPDX-License-Identifier: GPL-3.0-or-later
-->
```

Python and configuration files that accept hash comments use `#` comments.
Place a required shebang before the notices. For formats that cannot contain
comments, use an adjacent `.license` file containing the same notices and make
the association explicit in the file inventory.

## Existing and third-party material

- Preserve upstream copyright and license notices. Do not replace them with
  this project's header or infer permission from a directory's license alone.
- Keep the `external/DSView` submodule at the recorded commit. Review any
  source changes or revision updates before changing the reference.
- Retain the GPL license text verbatim; do not prepend a project header to it.
- Record generated-file provenance and applicable licenses. Generated files
  are not automatically original project material.
- Record source and license evidence for each imported asset or dependency in
  [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) and the eventual SBOM.

These conventions label files; they do not complete the dependency license audit.
