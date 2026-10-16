#!/usr/bin/env python3
"""Close both actual production workers before reference comparison."""
import argparse, hashlib, json, os, shutil, subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,x): p.write_text(json.dumps(x,indent=2)+'\n',encoding='utf-8')
p=argparse.ArgumentParser(); p.add_argument('--study',type=Path,required=True); p.add_argument('--dotnet',required=True); a=p.parse_args()
out=a.study.resolve(); freeze=json.loads((out/'freeze.json').read_text())
manifest=out/('native-inputs.json' if (out/'native-inputs.json').exists() else 'inputs.json')
reader=out/'reader'; shutil.copytree(ROOT/'tools/ocr-production-validation/bin/Release/net10.0',reader)
receipt={'inputSHA256':sha(manifest),'files':{str(p.relative_to(reader)):{'bytes':p.stat().st_size,'sha256':sha(p)} for p in reader.rglob('*') if p.is_file()},
         'runnerSHA256':sha(Path(__file__)),'goldReadDuringInference':False}
write(out/'execution-freeze.json',receipt)
env={k:v for k,v in os.environ.items() if not any(s in k.upper() for s in ['TOKEN','SECRET','PASSWORD','CREDENTIAL'])}
env.update(ORT_TELEMETRY_DISABLED='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
for arm in ['v5','hybrid']:
    raw=out/(arm+'-observations.jsonl')
    assert not raw.exists() and sha(manifest)==receipt['inputSHA256']
    with (out/(arm+'-stdout.txt')).open('w') as stdout,(out/(arm+'-stderr.txt')).open('w') as stderr:
        child=subprocess.run([a.dotnet,str(reader/'Takupoke.OcrComparison.dll'),str(manifest),str(raw),arm],stdout=stdout,stderr=stderr,env=env,timeout=800)
    write(out/(arm+'-receipt.json'),{'exitCode':child.returncode,'rawSHA256':sha(raw) if raw.exists() else None,'bytes':raw.stat().st_size if raw.exists() else None})
    print(json.dumps({'arm':arm,'closed':True,'exitCode':child.returncode}),flush=True)
    if child.returncode: raise RuntimeError('Retain failed worker unassessed')
    assert all(sha(reader/n)==v['sha256'] for n,v in receipt['files'].items())
assert sha(out/'oracle.json')==freeze['oracleSHA256']
