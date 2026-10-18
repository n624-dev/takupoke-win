#!/usr/bin/env python3
"""Same V5/V6 recipes with immutable original pixel transport across OSes."""
import argparse
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess

from PIL import Image
import run

PIXELS = run.ROOT / "tools/vision-ocr-comparison/fictional-pixels.json"
GOLD = run.ROOT / "tools/vision-ocr-comparison/oracle.json"
PIXEL_SHA = "6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff"


def prepare_rows(out):
    data = PIXELS.read_bytes()
    if hashlib.sha256(data).hexdigest() != PIXEL_SHA: raise ValueError("Frozen pixel transport mismatch")
    rows = []
    for row in json.loads(data)['rows']:
        encoded = base64.b64decode(row['pngBase64'], validate=True)
        if hashlib.sha256(encoded).hexdigest() != row['pngSha256']: raise ValueError("PNG mismatch")
        image = Image.open(io.BytesIO(encoded)).convert('RGBA')
        if image.size != (row['width'], row['height']): raise ValueError("Image shape mismatch")
        original = image.tobytes('raw', 'BGRA')
        if hashlib.sha256(original).hexdigest() != row['bgraSha256']: raise ValueError("Original source pixels changed")
        gray = bytearray(original)
        for i in range(0, len(original), 4):
            b, g, r, a = original[i:i+4]
            value = (299*r + 587*g + 114*b + 500)//1000
            gray[i:i+3] = bytes((value, value, value))
        assets = {}
        for condition, pixels in (('bgr', original), ('gray', gray)):
            path = out / (row['id'] + '-' + condition + '.bgra'); path.write_bytes(pixels)
            assets[condition] = run.asset(path, out)
        rows.append({'id': row['id'], 'width': row['width'], 'height': row['height'], **assets})
    if len(rows) != 192: raise ValueError("Frozen row count changed")
    return rows


def canonical_write(path, value):
    path.write_bytes((json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'))+'\n').encode('utf-8'))


def main():
    p = argparse.ArgumentParser(); p.add_argument('--output', type=Path, required=True)
    p.add_argument('--dotnet', default='dotnet'); p.add_argument('--cleanup', action='store_true')
    args = p.parse_args(); out = args.output.resolve(); out.mkdir(parents=True, exist_ok=False)
    (out / '.frozen-ocr-owned').write_bytes(b'v1\n')
    try:
        models = run.make_models(out); rows = prepare_rows(out)
        canonical_write(out / 'inputs.json', {'recipe': run.RECIPE, 'models': models, 'rows': rows})
        gold = json.loads(GOLD.read_text(encoding='utf-8')); canonical_write(out / 'oracle.json', gold)
        freeze = {'recipe': 'frozen-pixel-ocr-model-color-v1', 'pixelsSHA': PIXEL_SHA,
                  'inputs': run.asset(out / 'inputs.json', out), 'oracle': run.asset(out / 'oracle.json', out),
                  'sourceSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'callsPlanned': 768,
                  'qualification': 'unassessed', 'sourceCommit': os.environ.get('GITHUB_SHA', 'local-uncommitted')}
        canonical_write(out / 'freeze.json', freeze); print(json.dumps(freeze), flush=True)
        env = {**os.environ, 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1', 'ORT_TELEMETRY_DISABLED': '1'}
        subprocess.run([args.dotnet, 'build', str(Path(__file__).parent / 'Probe.csproj'), '-c', 'Release', '--verbosity', 'quiet'], check=True, env=env, timeout=180)
        dll = Path(__file__).parent / 'bin/Release/net10.0/Takupoke.OcrComparison.dll'
        worker = subprocess.run([args.dotnet, str(dll), str(out / 'inputs.json'), str(out / 'observations.jsonl')],
                                check=True, env=env, capture_output=True, text=True, encoding='utf-8', timeout=900)
        (out / 'worker-stdout.txt').write_text(worker.stdout, encoding='utf-8')
        raw = (out / 'observations.jsonl').read_bytes(); raw_sha = hashlib.sha256(raw).hexdigest()
        canonical_write(out / 'raw-receipt.json', {'sha256': raw_sha, 'bytes': len(raw)})
        observations = [json.loads(line) for line in raw.decode('utf-8').splitlines()]
        # Source/roles/oracle are still scoring-only and never enter the worker.
        report = {**freeze, 'rawSHA': raw_sha, 'outcomeSHA': run.outcome_sha(observations),
                  'aggregates': run.score(observations, json.loads((out / 'oracle.json').read_text(encoding='utf-8'))),
                  'worker': [json.loads(line) for line in worker.stdout.splitlines() if line.startswith('{')], 'modelActivation': False}
        canonical_write(out / 'report.json', report); print(json.dumps(report, ensure_ascii=False), flush=True)
    finally:
        if args.cleanup and (out / '.frozen-ocr-owned').read_bytes() == b'v1\n':
            shutil.rmtree(out); print(json.dumps({'ownedScratchRemoved': not out.exists()}), flush=True)


if __name__ == '__main__': main()
