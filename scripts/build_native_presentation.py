"""Package the pinned upstream Nu adapter. Build-time Python only."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import tomllib

REVISION = "18df65b7eff51bc0c893ec687161654ea378ee7f"
SOURCES = (
    "scripts/build_nushell.py", "nushell/r3cli/mod.nu", "LICENSE",
    "src/r3_cli/default_theme.toml", "src/r3_cli/symbols.json", "pyproject.toml",
)


def reject_links(path: Path) -> None:
    for entry in (path, *path.parents):
        try:
            metadata = entry.lstat()
        except FileNotFoundError:
            continue
        if entry.is_symlink() or getattr(metadata, "st_file_attributes", 0) & 0x400:
            raise ValueError(f"Package output must not traverse links or reparse points: {entry}")


def build(repository: Path, destination: Path) -> None:
    destination = destination.absolute()
    reject_links(destination)
    # Read immutable Git objects, never the user's potentially dirty sources.
    with tempfile.TemporaryDirectory(prefix="modpacktools-r3cli-build-") as temporary:
        source = Path(temporary)
        for name in SOURCES:
            content = subprocess.run(
                ["git", "-C", str(repository), "show", f"{REVISION}:{name}"],
                check=True, capture_output=True,
            ).stdout
            path = source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
        version = tomllib.loads((source / "pyproject.toml").read_text(encoding="utf-8"))["project"]["version"]
        staging = source / "package"
        subprocess.run([sys.executable, str(source / "scripts/build_nushell.py"), "--output", str(staging)], check=True)
        files = {path.name: hashlib.sha256(path.read_bytes()).hexdigest().upper()
                 for path in sorted(staging.iterdir())}
        manifest = {"schema_version": 1, "revision": REVISION, "version": version, "files": files}
        (staging / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
        destination.mkdir(parents=True, exist_ok=True)
        reject_links(destination)
        for path in destination.iterdir():
            reject_links(path)
        unexpected = [path.name for path in destination.iterdir()
                      if not path.is_file() or path.name not in {*files, "manifest.json"}]
        if unexpected:
            raise ValueError(f"Package destination has unexpected content: {unexpected}")
        for path in staging.iterdir():
            (destination / path.name).write_bytes(path.read_bytes())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    build(args.repository, args.output)
