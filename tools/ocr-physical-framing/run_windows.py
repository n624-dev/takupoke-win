#!/usr/bin/env python3
"""Owned Windows acquisition -> native OCR -> closed, literal document scoring."""
import argparse
import base64
import gzip
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tarfile
import urllib.request

from run import ROOT, PACKET_SHA, sha, write
from score import evaluate

REPO = ROOT.parent.parent
FIXTURE_REV = '708556af1b039bd789996a69ee3bce9e0850c6bb'
REC_REV = 'ed152b8b495f84de93cda5709d768548a9127622'


def download(url, path, size, digest):
    with urllib.request.urlopen(url, timeout=60) as response:
        data = response.read(size + 1)
    if len(data) != size or hashlib.sha256(data).hexdigest() != digest:
        raise ValueError('Pinned public artifact changed: ' + path.name)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)


def command(argv, env, timeout=300, output=None):
    result = subprocess.run(argv, env=env, capture_output=True, text=True,
                            encoding='utf-8', timeout=timeout)
    if output:
        (output / 'worker-stdout.txt').write_text(result.stdout, encoding='utf-8', newline='\n')
        (output / 'worker-stderr.txt').write_text(result.stderr, encoding='utf-8', newline='\n')
    if result.returncode:
        print(result.stdout, flush=True)
        print(result.stderr, flush=True)
        raise RuntimeError('Native process failed; missing calls stay unassessed')


def emit_evidence(root, files):
    # Only named synthetic public research files are transported in logs.
    # The acquisition-only diagnosis also preserves the native synthetic pixels.
    # No models, PDFs, secrets, caches or original school data are transported.
    stream = io.BytesIO()
    with tarfile.open(fileobj=stream, mode='w') as archive:
        for name in files:
            data = (root / name).read_bytes()
            info = tarfile.TarInfo(name)
            info.size = len(data)
            info.mtime = 0
            archive.addfile(info, io.BytesIO(data))
    packed = gzip.compress(stream.getvalue(), mtime=0)
    if len(packed) > 8 * 1024 * 1024:
        raise ValueError('Synthetic evidence exceeds fixed log transport bound')
    encoded = base64.b64encode(packed).decode('ascii')
    print('PHYSICAL_FRAME_EVIDENCE_SHA256 ' + hashlib.sha256(packed).hexdigest(), flush=True)
    for offset in range(0, len(encoded), 16000):
        print('PHYSICAL_FRAME_EVIDENCE ' + encoded[offset:offset+16000], flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--acquisition-only', action='store_true')
    args = parser.parse_args()
    if os.name != 'nt':
        raise RuntimeError('Actual Windows renderer required')
    args.output.mkdir(parents=True, exist_ok=False)
    fixture = args.output / 'reference/tests/fixtures/recovery-generic-ruled-original-two.json'
    # Fixture byte size is bounded first; exact SHA identity is mandatory.
    with urllib.request.urlopen(f'https://raw.githubusercontent.com/n624-dev/takupoke-ios/{FIXTURE_REV}/tests/fixtures/recovery-generic-ruled-original-two.json', timeout=60) as response:
        data = response.read(16 * 1024 * 1024 + 1)
    if len(data) > 16 * 1024 * 1024 or hashlib.sha256(data).hexdigest() != PACKET_SHA:
        raise ValueError('Public consumed fixture identity failed')
    fixture.parent.mkdir(parents=True)
    fixture.write_bytes(data)
    rec = args.output / 'v5-mobile.onnx'
    download(f'https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_rec_onnx/resolve/{REC_REV}/inference.onnx', rec,
             16534782, 'da72dc72ca4dc220df0dfde68c1dedc31c58d3e76a25871122e5056227d50092')
    owned = args.output / 'measurement'
    env = {**os.environ, 'ORT_TELEMETRY_DISABLED': '1'}
    command([sys.executable, '-B', str(ROOT / 'run.py'), '--prepare-only',
             '--ios-reference', str(args.output / 'reference'), '--recognizer', str(rec),
             '--dictionary', str(REPO / 'src/Takupoke.Win/Assets/Recovery/ocr-characters.json'),
             '--dotnet', 'dotnet', '--output', str(owned)], env)
    command([sys.executable, '-B', str(ROOT / 'prepare_pdf.py'), '--manifest', str(owned / 'inputs.json')], env)
    write(owned / 'source-freeze.json', {
        'gitHead': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=REPO, text=True).strip(),
        'sources': {str(p.relative_to(REPO)): sha(p) for p in [
            ROOT / 'Program.cs', ROOT / 'PhysicalCropFrame.cs', ROOT / 'run.py', ROOT / 'run_windows.py',
            ROOT / 'prepare_pdf.py', ROOT / 'score.py', ROOT / 'windows-render/WindowsRender.cs',
            REPO / 'src/Takupoke.Infrastructure/Recovery/RecoveryDocumentBuilder.cs']},
        'plannedConditions': ['production', 'physical-input-frame'],
        'scope': 'Consumed independent-at-origin drawing pixels; new PDF containers, zero new heldout credit'})
    command(['dotnet', 'build', str(ROOT / 'windows-render/WindowsRender.csproj'), '-c', 'Release', '--verbosity', 'quiet'], env)
    runtime = ROOT / 'windows-render/bin/Release/net10.0-windows10.0.26100.0/win-x64/WindowsRender.dll'
    safe = {k: v for k, v in env.items() if not any(x in k.upper() for x in ('TOKEN', 'SECRET', 'PASSWORD', 'CREDENTIAL'))}
    command(['dotnet', str(runtime), str(owned / 'pdf-inputs.json')], safe)
    if args.acquisition_only:
        # Reacquire the closed synthetic drawings to diagnose the observed
        # native ownership refusal; do not repeat OCR or add accuracy cases.
        native = json.loads((owned / 'native-inputs.json').read_text(encoding='utf-8'))
        pixels = [p['pixels']['file'] for d in native['documents'] for p in d['pages']]
        emit_evidence(owned, ['inputs.json', 'pdf-inputs.json', 'native-inputs.json',
            'native-render-receipt.json', 'source-freeze.json', *pixels])
        print('Acquisition diagnosis only; detector/recognizer calls 0, no accuracy credit.', flush=True)
        return
    command(['dotnet', 'build', str(ROOT / 'Probe.csproj'), '-c', 'Release', '--verbosity', 'quiet'], env)
    raw = owned / 'observations.jsonl'
    command(['dotnet', str(ROOT / 'bin/Release/net10.0/Takupoke.OcrComparison.dll'),
             str(owned / 'native-inputs.json'), str(raw)], safe, timeout=1000, output=owned)
    write(owned / 'raw-receipt.json', {'exitCode': 0, 'rawSHA256': sha(raw), 'bytes': raw.stat().st_size})
    # Parent consults the oracle only after the native child has terminated.
    report = evaluate(raw, fixture)
    write(owned / 'report.json', report)
    print(json.dumps(report, ensure_ascii=False), flush=True)
    emit_evidence(owned, ['inputs.json', 'pdf-inputs.json', 'native-inputs.json', 'native-render-receipt.json',
        'source-freeze.json', 'raw-receipt.json', 'observations.jsonl', 'report.json', 'worker-stdout.txt', 'worker-stderr.txt'])
    if any(o['quality'] == 'unassessed' for o in report['observations']):
        raise RuntimeError('Incomplete native measurement; no quality claim')
    print('Closed paired experiment. Positive refusals remain failures; no deployment qualification.', flush=True)


if __name__ == '__main__':
    main()
