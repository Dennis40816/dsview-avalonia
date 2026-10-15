# SPDX-FileCopyrightText: 2026 Dennis40816
# SPDX-License-Identifier: GPL-3.0-or-later
"""Read decoder class metadata without importing/executing upstream Python."""
import argparse
import ast
import hashlib
import json
from pathlib import Path


def literal(node):
    try:
        return ast.literal_eval(node)
    except (ValueError, TypeError):
        if isinstance(node, (ast.Tuple, ast.List)):
            return [literal(item) for item in node.elts]
        if isinstance(node, ast.Dict) and all(key is not None for key in node.keys):
            return {str(literal(key)): literal(value) for key, value in zip(node.keys, node.values)}
        return {"unresolved_expression": ast.unparse(node)}


def inventory(root):
    fields = {"id", "name", "longname", "license", "inputs", "outputs", "channels", "optional_channels", "options", "annotation_rows", "binary"}
    entries = []
    for path in sorted((root / "libsigrokdecode4DSL" / "decoders").glob("*/pd.py")):
        data = path.read_bytes()
        entry = {"path": path.relative_to(root).as_posix(), "sha256": hashlib.sha256(data).hexdigest()}
        try:
            module = ast.parse(data, filename=str(path))
            decoder = next(node for node in module.body if isinstance(node, ast.ClassDef) and node.name == "Decoder")
            entry["metadata"] = {
                target.id: literal(node.value)
                for node in decoder.body if isinstance(node, ast.Assign)
                for target in node.targets if isinstance(target, ast.Name) and target.id in fields
            }
        except (SyntaxError, StopIteration) as error:
            entry["inspection_error"] = type(error).__name__
        entries.append(entry)
    if not entries:
        raise ValueError("No decoder modules found in the selected upstream checkout")
    return entries


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("upstream", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    entries = inventory(args.upstream.resolve())
    args.output.write_text(json.dumps({"schema": 1, "module_count": len(entries), "modules": entries}, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Indexed {len(entries)} modules; {sum('inspection_error' in e for e in entries)} inspection errors. Source presence is not runtime support.")
