#!/usr/bin/env python3
"""English V5 first-pass on the fixed whole original-line development corpus."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess

import run
import run_frozen

MODEL={'id':'english-v5','repository':'PaddlePaddle/en_PP-OCRv5_mobile_rec_onnx',
       'revision':'3fafbc3b5dcf93dd72add9f48368be8a3a2cd33b','bytes':7848423,
       'sha256':'b5f833dfc5d0eb71da397b4efa06ebeee9b431b690a47d6af40d77d8eabc557f','symbols':438}
DICT_SHA='ef84489f927fb92b64714c7f110404e5bfb83c4067bd609fe4c41d5f23e25659'


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--dotnet',required=True);a=p.parse_args()
    if a.assets.joinpath('inference.onnx').stat().st_size!=MODEL['bytes'] or run.sha((a.assets/'inference.onnx').read_bytes())!=MODEL['sha256']:
        raise ValueError('Pinned converted model changed')
    if run.sha((a.assets/'dictionary.json').read_bytes())!=DICT_SHA:raise ValueError('Official full English dictionary changed')
    a.output.mkdir(parents=True,exist_ok=False)
    for source,target in [('inference.onnx','recognizer.onnx'),('dictionary.json','dictionary.json')]:shutil.copyfile(a.assets/source,a.output/target)
    models=[{**MODEL,'file':run.asset(a.output/'recognizer.onnx',a.output),'dictionary':run.asset(a.output/'dictionary.json',a.output)}]
    rows=run_frozen.prepare_rows(a.output)
    run_frozen.canonical_write(a.output/'inputs.json',{'recipe':run.RECIPE,'models':models,'rows':rows})
    root=Path(__file__).parent;env={**os.environ,'DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_NOLOGO':'1','ORT_TELEMETRY_DISABLED':'1'}
    subprocess.run([a.dotnet,'build',str(root/'Probe.csproj'),'-c','Release','--verbosity','quiet'],env=env,check=True,timeout=180)
    reader=a.output/'reader';shutil.copytree(root/'bin/Release/net10.0',reader)
    binaries={str(p.relative_to(reader)):run.asset(p,reader) for p in sorted(reader.rglob('*')) if p.is_file()}
    freeze={'recipe':'english-v5-consumed-clean-line-color-v1','model':MODEL,'inputs':run.asset(a.output/'inputs.json',a.output),
        'pixelsSHA256':run_frozen.PIXEL_SHA,'oracleSHA256':run.sha(run_frozen.GOLD.read_bytes()),'readerFiles':binaries,'callsPlanned':384,
        'sources':{p.name:run.sha(p.read_bytes()) for p in [Path(__file__),root/'Program.cs',root/'run.py',root/'run_frozen.py']},
        'scope':'Consumed original whole mixed-language lines, no Japanese prefix removal. English-only capacity is tested without oracle-dependent routing; no heldout/production qualification',
        'dictionary':'Original blank +436 characters +space; no alphabet filtering','qualification':False}
    run_frozen.canonical_write(a.output/'freeze.json',freeze)
    raw=a.output/'observations.jsonl'
    with (a.output/'worker-stdout.txt').open('w') as stdout,(a.output/'worker-stderr.txt').open('w') as stderr:
        child=subprocess.run([a.dotnet,str(reader/'Takupoke.OcrComparison.dll'),str(a.output/'inputs.json'),str(raw)],env=env,stdout=stdout,stderr=stderr,timeout=900)
    run_frozen.canonical_write(a.output/'raw-receipt.json',{'exitCode':child.returncode,'sha256':run.sha(raw.read_bytes()) if raw.exists() else None,'bytes':raw.stat().st_size if raw.exists() else None})
    if child.returncode:raise RuntimeError('Native execution failed; no accuracy credit')
    if any(run.asset(reader/name,reader)!=v for name,v in binaries.items()):raise ValueError('Native binaries changed')
    if run.sha(run_frozen.GOLD.read_bytes())!=freeze['oracleSHA256']:raise ValueError('Oracle changed')
    observed=[json.loads(l) for l in raw.read_text().splitlines()]
    report={**freeze,'rawSHA256':run.sha(raw.read_bytes()),'callsCompleted':len(observed),
        'aggregates':run.score(observed,json.loads(run_frozen.GOLD.read_text()),['english-v5']),
        'worker':[json.loads(l) for l in (a.output/'worker-stdout.txt').read_text().splitlines() if l.startswith('{')], 'modelActivation':False}
    run_frozen.canonical_write(a.output/'report.json',report)
    print(json.dumps({'callsCompleted':len(observed),'aggregates':report['aggregates']}),flush=True)


if __name__=='__main__':main()
