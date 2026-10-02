#!/usr/bin/env python3
"""Verify fictional fixtures against the pinned, unmodified iOS implementation."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import urllib.request

BASE = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--ios-root', type=Path)
parser.add_argument('--write', action='store_true', help='Regenerate fictional expectations explicitly.')
args = parser.parse_args()
manifest = json.loads((BASE / 'tests/reference/ios-source.json').read_text())
fixture = BASE / 'tests/Takupoke.Core.Tests/fixtures/ios-parity.json'
with tempfile.TemporaryDirectory(prefix='takupoke-ios-reference-', dir=os.environ.get('RUNNER_TEMP')) as temp:
    task = Path(temp)
    sources = []
    for relative, expected_hash in manifest['files'].items():
        if args.ios_root:
            data = (args.ios_root / relative).read_bytes()
        else:
            url = f"https://raw.githubusercontent.com/{manifest['repository']}/{manifest['commit']}/{relative}"
            with urllib.request.urlopen(url, timeout=60) as response:
                data = response.read(2 * 1024 * 1024)
        if hashlib.sha256(data).hexdigest() != expected_hash:
            raise SystemExit(f'Pinned iOS source hash mismatch: {relative}')
        target = task / Path(relative).name
        target.write_bytes(data)
        sources.append(str(target))
    compiler = shutil.which('swiftc')
    if not compiler:
        raise SystemExit('Swift compiler is required to verify the original iOS rules.')
    subprocess.run([compiler, '-parse-as-library', '-swift-version', '5', '-module-cache-path', str(task / 'module-cache'),
                    *sources, str(BASE / 'tests/reference/ExportParity.swift'), '-o', str(task / 'export')], check=True)
    subprocess.run([str(task / 'export'), str(task / 'actual.json')], check=True)
    actual = json.loads((task / 'actual.json').read_text())
    if args.write:
        fixture.parent.mkdir(exist_ok=True)
        fixture.write_text(json.dumps(actual, ensure_ascii=False, indent=2) + '\n')
    elif json.loads(fixture.read_text()) != actual:
        raise SystemExit('Fictional expectations differ from the pinned iOS implementation.')
    print(f"Original iOS reference verified: {len(actual['schedule'])} schedule cases plus text, search, classes and colors.")
print('Reference sources, executable and compiler cache removed; no Actions cache or artifact created.')
