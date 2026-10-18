#!/usr/bin/env python3
"""Supplemental PP-OCRv6 small on the immutable consumed development pixels."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

import yaml
import run
import run_frozen

MODEL = {'id': 'v6-small', 'repo': 'PP-OCRv6_small_rec_onnx',
         'revision': 'b8f84f0b80c529de40b4fbb3544b84fa7233a513', 'bytes': 21159378,
         'sha256': '5435fd747c9e0efe15a96d0b378d5bd157e9492ed8fd80edf08f30d02fa24634',
         'symbols': 18710}
CONFIG_SHA = 'ab078671bb49f06228eadccd34f1bb501e157f7a047095ffb943ba81512c77d1'
DICTIONARY_SHA = 'a49d51120786924a642c9e8a14b2a2ab73c019699abf5b2b6970ec7355c0415f'


def main():
    p = argparse.ArgumentParser(); p.add_argument('--output', required=True, type=Path)
    p.add_argument('--dotnet', default='dotnet'); p.add_argument('--cleanup', action='store_true')
    args = p.parse_args(); out = args.output.resolve(); out.mkdir(parents=True, exist_ok=False)
    marker = out / '.v6-small-owned'; marker.write_bytes(b'v1\n')
    try:
        url = f'https://huggingface.co/PaddlePaddle/{MODEL["repo"]}/resolve/{MODEL["revision"]}/'
        model = out / 'v6-small.onnx'; config = out / 'v6-small-inference.yml'
        run.download(url+'inference.onnx', model, MODEL['sha256'], MODEL['bytes'], MODEL['bytes'])
        run.download(url+'inference.yml', config, CONFIG_SHA, 150579, 150579)
        chars = yaml.safe_load(config.read_text(encoding='utf-8'))['PostProcess']['character_dict']
        dictionary = out / 'v6-small-dictionary.json'
        data = (json.dumps(['']+chars+[' '], ensure_ascii=False, separators=(',', ':'))+'\n').encode('utf-8')
        if len(chars)+2 != MODEL['symbols'] or hashlib.sha256(data).hexdigest() != DICTIONARY_SHA:
            raise ValueError('Small model dictionary mismatch')
        dictionary.write_bytes(data)
        models = [{**MODEL, 'file': run.asset(model, out), 'dictionary': run.asset(dictionary, out), 'config': run.asset(config, out)}]
        rows = run_frozen.prepare_rows(out)
        run_frozen.canonical_write(out/'inputs.json', {'recipe': run.RECIPE, 'models': models, 'rows': rows})
        freeze = {'recipe': 'frozen-v6-small-color-v1', 'pixelsSHA': run_frozen.PIXEL_SHA,
            'inputs': run.asset(out/'inputs.json', out), 'oracleSHA': run.sha(run_frozen.GOLD.read_bytes()),
            'models': models, 'callsPlanned': 384, 'qualification': 'unassessed',
            'sources': {p.name: run.sha(p.read_bytes()) for p in (Path(__file__), Path(__file__).with_name('Program.cs'),
                Path(__file__).with_name('run.py'), Path(__file__).with_name('run_frozen.py'))}}
        run_frozen.canonical_write(out/'freeze.json', freeze)
        print(json.dumps({'freezeSHA': run.sha((out/'freeze.json').read_bytes()), 'callsPlanned': 384}), flush=True)
        env = {**os.environ, 'DOTNET_CLI_TELEMETRY_OPTOUT':'1', 'DOTNET_NOLOGO':'1', 'ORT_TELEMETRY_DISABLED':'1'}
        subprocess.run([args.dotnet, 'build', str(Path(__file__).with_name('Probe.csproj')), '-c', 'Release', '--verbosity', 'quiet'], check=True, timeout=180, env=env)
        worker = subprocess.run([args.dotnet, str(Path(__file__).parent/'bin/Release/net10.0/Takupoke.OcrComparison.dll'),
            str(out/'inputs.json'), str(out/'observations.jsonl')], check=True, timeout=900, env=env,
            capture_output=True, text=True, encoding='utf-8')
        (out/'worker-stdout.txt').write_text(worker.stdout, encoding='utf-8')
        raw = (out/'observations.jsonl').read_bytes()
        run_frozen.canonical_write(out/'raw-receipt.json', {'bytes':len(raw), 'sha256':run.sha(raw)})
        observations = [json.loads(line) for line in raw.decode('utf-8').splitlines()]
        if run.sha(run_frozen.GOLD.read_bytes()) != freeze['oracleSHA']: raise ValueError('Oracle changed')
        # Only the completed immutable raw file is evaluated against the oracle.
        report = {**freeze, 'callsCompleted':len(observations), 'rawSHA':run.sha(raw),
            'outcomeSHA':run.outcome_sha(observations),
            'aggregates':run.score(observations, json.loads(run_frozen.GOLD.read_text(encoding='utf-8')), ['v6-small']),
            'worker':[json.loads(line) for line in worker.stdout.splitlines() if line.startswith('{')],
            'scope':'Consumed recognition components only; no PDF/role/ink certification, qualification or adoption',
            'modelActivation':False}
        run_frozen.canonical_write(out/'report.json', report); print(json.dumps(report, ensure_ascii=False), flush=True)
    finally:
        if args.cleanup and marker.read_bytes() == b'v1\n':
            shutil.rmtree(out); print(json.dumps({'ownedScratchRemoved':not out.exists()}), flush=True)


if __name__ == '__main__': main()
