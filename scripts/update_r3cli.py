"""Vendor an exact clean R3CLI revision. Development only; no Python at runtime."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def update(source: Path, allow_dirty: bool = False) -> None:
    def git(*args: str) -> str:
        return subprocess.check_output(['git', '-C', str(source), *args], text=True).strip()

    if git('status', '--porcelain') and not allow_dirty:
        raise SystemExit('Commit R3CLI first; vendoring requires a clean revision.')
    revision = git('rev-parse', 'HEAD')
    destination = ROOT / 'Private/vendor/R3CLI'
    subprocess.run([sys.executable, str(source / 'scripts/build_powershell.py'), '--output', str(destination)], check=True)
    hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest().upper() for p in sorted(destination.iterdir()) if p.is_file()}
    version = re.search(r"ModuleVersion\s*=\s*'([^']+)'", (destination / 'R3CLI.psd1').read_text()).group(1)
    entry = "    R3CLI = @{\n        Version = '" + version + "'\n        Revision = '" + revision + "'\n        Source = 'https://github.com/R3Neer/R3CLI'\n        Files = @{\n"
    entry += ''.join(f"            '{name}' = '{value}'\n" for name, value in hashes.items())
    entry += '        }\n    }\n'
    path = ROOT / 'dependencies.psd1'
    text = path.read_text(encoding='utf-8')
    text = re.sub(r'    R3CLI = @\{.*?\n    \}\n', '', text, flags=re.S)
    path.write_bytes(text.replace('@{\n', '@{\n' + entry, 1).encode('utf-8'))
    print(json.dumps({'revision': revision, 'version': version, 'files': len(hashes)}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('--allow-dirty', action='store_true', help='Local development only; regenerate from a clean commit before delivery.')
    args = parser.parse_args()
    update(args.source.resolve(), args.allow_dirty)
