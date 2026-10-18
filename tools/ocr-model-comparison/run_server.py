#!/usr/bin/env python3
"""Official V5 server, unchanged app preprocessing, consumed fixed pixels."""
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

MODEL = {'id': 'v5-server', 'repo': 'PaddlePaddle/PP-OCRv5_server_rec_onnx',
         'revision': 'b70df217f4fd99d14f970bad092cebe7d74cc4d1', 'bytes': 84503027,
         'sha256': 'd9dc333c9c7b042c6dffb8e33d72b6f65c9c1d463d0a3c2f78174fea55e94752',
         'symbols': 18385, 'license': 'Apache-2.0'}
CONFIG_SHA = '2c719dba044c4e2228aef8ff92f5f575394d75d24c16de096a33b7cfd902f66d'
DICTIONARY_SHA = 'd858428fbe0ada92b439a5f277b51bc1b318fd985d80946a3ce859d7f2446a92'


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--assets', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--dotnet', default='dotnet')
    args = p.parse_args()
    # Reject artifact/source changes before materializing any recognition input.
    assets = args.assets
    for name, digest, size in [('inference.onnx', MODEL['sha256'], MODEL['bytes']),
                               ('inference.yml', CONFIG_SHA, 148345)]:
        source = assets/name
        if source.stat().st_size != size or run.sha(source.read_bytes()) != digest:
            raise ValueError('Official pinned server artifact changed')
    chars = yaml.safe_load((assets/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']
    dictionary = run.ROOT/'src/Takupoke.Win/Assets/Recovery/ocr-characters.json'
    if run.sha(dictionary.read_bytes()) != DICTIONARY_SHA or json.loads(dictionary.read_bytes()) != ['']+chars+[' ']:
        raise ValueError('Server official alphabet must exactly equal app V5 alphabet')
    out = args.output.resolve(); out.mkdir(parents=True, exist_ok=False)
    shutil.copyfile(assets/'inference.onnx', out/'v5-server.onnx')
    shutil.copyfile(assets/'inference.yml', out/'inference.yml')
    shutil.copyfile(dictionary, out/'dictionary.json')
    models = [{**MODEL, 'file': run.asset(out/'v5-server.onnx', out),
               'dictionary': run.asset(out/'dictionary.json', out), 'config': run.asset(out/'inference.yml', out)}]
    rows = run_frozen.prepare_rows(out)
    run_frozen.canonical_write(out/'inputs.json', {'recipe': run.RECIPE, 'models': models, 'rows': rows})
    env = {**os.environ, 'DOTNET_CLI_TELEMETRY_OPTOUT':'1', 'DOTNET_NOLOGO':'1', 'ORT_TELEMETRY_DISABLED':'1'}
    root = Path(__file__).parent
    subprocess.run([args.dotnet, 'build', str(root/'Probe.csproj'), '-c', 'Release', '--verbosity', 'quiet'], check=True, timeout=180, env=env)
    reader = out/'reader'; shutil.copytree(root/'bin/Release/net10.0', reader)
    binaries = {str(p.relative_to(reader)):run.asset(p, reader) for p in sorted(reader.rglob('*')) if p.is_file()}
    freeze = {'recipe':'official-v5-server-consumed-color-v1', 'pixelsSHA':run_frozen.PIXEL_SHA,
        'inputs':run.asset(out/'inputs.json', out), 'oracleSHA':run.sha(run_frozen.GOLD.read_bytes()),
        'models':models, 'readerFiles':binaries, 'callsPlanned':384, 'qualification':'unassessed',
        'cpuThreads':2, 'confidenceFloor':.8, 'wholeDocumentCredit':0,
        'sources':{str(p.relative_to(run.ROOT)):run.sha(p.read_bytes()) for p in [Path(__file__), root/'Program.cs', root/'run.py', root/'run_frozen.py',
            run.ROOT/'src/Takupoke.Infrastructure/Recovery/OcrInputTransform.cs', run.ROOT/'src/Takupoke.Infrastructure/Recovery/OcrCtcDecoder.cs']}}
    run_frozen.canonical_write(out/'freeze.json', freeze)
    print(json.dumps({'freezeSHA':run.sha((out/'freeze.json').read_bytes()), 'callsPlanned':384}), flush=True)
    with (out/'worker-stdout.txt').open('w',encoding='utf-8',newline='\n') as stdout, (out/'worker-stderr.txt').open('w',encoding='utf-8',newline='\n') as stderr:
        worker = subprocess.run([args.dotnet,str(reader/'Takupoke.OcrComparison.dll'),str(out/'inputs.json'),str(out/'observations.jsonl')],
            env=env,stdout=stdout,stderr=stderr,timeout=900)
    raw = (out/'observations.jsonl').read_bytes() if (out/'observations.jsonl').exists() else b''
    run_frozen.canonical_write(out/'raw-receipt.json', {'exitCode':worker.returncode,'bytes':len(raw),'sha256':run.sha(raw)})
    if worker.returncode: raise RuntimeError('Worker failure; retain partial raw, no accuracy score')
    if any(run.asset(reader/name,reader)!=expected for name,expected in binaries.items()):
        raise ValueError('Active reader binary changed')
    if run.sha((out/'inputs.json').read_bytes()) != freeze['inputs']['sha256'] or run.sha(run_frozen.GOLD.read_bytes()) != freeze['oracleSHA']:
        raise ValueError('Frozen inputs/oracle changed')
    observed = [json.loads(line) for line in raw.decode('utf-8').splitlines()]
    # The oracle is opened only after native inference exits and raw is frozen.
    report = {**freeze, 'callsCompleted':len(observed), 'rawSHA':run.sha(raw), 'readerBinaryIdentityStable':True,
        'aggregates':run.score(observed,json.loads(run_frozen.GOLD.read_text(encoding='utf-8')),['v5-server']),
        'worker':[json.loads(line) for line in (out/'worker-stdout.txt').read_text(encoding='utf-8').splitlines() if line.startswith('{')],
        'modelActivation':False, 'scope':'Consumed recognition pixels only; no detector/PDF/roles/full document/Windows device qualification'}
    run_frozen.canonical_write(out/'report.json',report)
    print(json.dumps(report,ensure_ascii=False),flush=True)


if __name__ == '__main__': main()
