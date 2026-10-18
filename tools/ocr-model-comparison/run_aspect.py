#!/usr/bin/env python3
"""Fixed horizontal source-pixel replication for the consumed V6 medium corpus."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess

import run
import run_frozen

MODEL={'id':'v6-medium','repository':'PaddlePaddle/PP-OCRv6_medium_rec_onnx',
       'revision':'50c7eacafc52fa7bcf4194e8cd08e46f8558504b','bytes':76554979,
       'sha256':'9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba','symbols':18710}
DICT_SHA='a49d51120786924a642c9e8a14b2a2ab73c019699abf5b2b6970ec7355c0415f'


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--dotnet',required=True);a=p.parse_args()
    if a.assets.joinpath('v6-medium.onnx').stat().st_size!=MODEL['bytes'] or run.sha((a.assets/'v6-medium.onnx').read_bytes())!=MODEL['sha256']:
        raise ValueError('Pinned converted model changed')
    if run.sha((a.assets/'v6-medium-dictionary.json').read_bytes())!=DICT_SHA:raise ValueError('Original full dictionary changed')
    a.output.mkdir(parents=True,exist_ok=False)
    for source,target in [('v6-medium.onnx','recognizer.onnx'),('v6-medium-dictionary.json','dictionary.json')]:shutil.copyfile(a.assets/source,a.output/target)
    models=[{**MODEL,'file':run.asset(a.output/'recognizer.onnx',a.output),'dictionary':run.asset(a.output/'dictionary.json',a.output)}]
    rows=run_frozen.prepare_rows(a.output)
    run_frozen.canonical_write(a.output/'inputs.json',{'recipe':run.RECIPE,'models':models,'rows':rows,'researchAspectX':2})
    root=Path(__file__).parent;env={**os.environ,'DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_NOLOGO':'1','ORT_TELEMETRY_DISABLED':'1'}
    subprocess.run([a.dotnet,'build',str(root/'Probe.csproj'),'-c','Release','--verbosity','quiet'],env=env,check=True,timeout=180)
    reader=a.output/'reader';shutil.copytree(root/'bin/Release/net10.0',reader)
    binaries={str(p.relative_to(reader)):run.asset(p,reader) for p in sorted(reader.rglob('*')) if p.is_file()}
    freeze={'recipe':'v6-medium-integer-x2-consumed-lines-v1','model':MODEL,'inputs':run.asset(a.output/'inputs.json',a.output),
        'pixelsSHA256':run_frozen.PIXEL_SHA,'oracleSHA256':run.sha(run_frozen.GOLD.read_bytes()),'readerFiles':binaries,'callsPlanned':384,
        'sources':{p.name:run.sha(p.read_bytes()) for p in [Path(__file__),root/'Program.cs',root/'run.py',root/'run_frozen.py',root/'ResearchAspect.cs']},
        'scope':'Every original pixel repeated twice horizontally before the unchanged BGR48 transform; original source crop and strict padding bounds retained. Consumed development, no final/production qualification',
        'dictionary':'Original complete18710 dictionary, no character filtering','qualification':False}
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
        'aggregates':run.score(observed,json.loads(run_frozen.GOLD.read_text()),['v6-medium']),
        'worker':[json.loads(l) for l in (a.output/'worker-stdout.txt').read_text().splitlines() if l.startswith('{')], 'modelActivation':False}
    run_frozen.canonical_write(a.output/'report.json',report)
    print(json.dumps({'callsCompleted':len(observed),'aggregates':report['aggregates']}),flush=True)


if __name__=='__main__':main()
