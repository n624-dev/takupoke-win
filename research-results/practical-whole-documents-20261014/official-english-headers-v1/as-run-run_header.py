#!/usr/bin/env python3
"""Fixed English class-header measurements on closed primary observations."""
import argparse,hashlib,json,os,shutil,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,x):p.write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def asset(p):return {'file':p.name,'bytes':p.stat().st_size,'sha256':sha(p)}
p=argparse.ArgumentParser();p.add_argument('--cohort',type=Path,required=True);p.add_argument('--english-assets',type=Path,required=True);p.add_argument('--dotnet',required=True);p.add_argument('--output',type=Path,required=True)
a=p.parse_args();source=a.cohort.resolve();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
receipt=json.loads((source/'v6-receipt.json').read_text());assert receipt['exitCode']==0 and sha(source/'v6-observations.jsonl')==receipt['rawSHA256']
for name in ['v6-observations.jsonl','v6-inputs.json','oracle.json']:
 shutil.copyfile(source/name,out/name)
primary=json.loads((out/'v6-inputs.json').read_text())
for doc in primary['documents']:
 for page in doc['pages']:
  item=page['pixels'];path=source/item['file'];assert path.stat().st_size==item['bytes'] and sha(path)==item['sha256'];os.link(path,out/item['file'])
for name,digest in [('inference.onnx','b5f833dfc5d0eb71da397b4efa06ebeee9b431b690a47d6af40d77d8eabc557f'),('dictionary.json','ef84489f927fb92b64714c7f110404e5bfb83c4067bd609fe4c41d5f23e25659')]:
 path=a.english_assets/name;assert sha(path)==digest;shutil.copyfile(path,out/name)
reader=out/'reader';shutil.copytree(ROOT/'tools/ocr-practical-documents/header-probe/bin/Release/net10.0',reader)
manifest={'recipe':'consumed-primary-english-header-v1','primaryRaw':asset(out/'v6-observations.jsonl'),
 'primaryManifest':asset(out/'v6-inputs.json'),'model':asset(out/'inference.onnx'),'dictionary':asset(out/'dictionary.json')}
write(out/'inputs.json',manifest)
freeze={'recipe':manifest['recipe'],'sourceSHA256':{str(p.relative_to(ROOT)):sha(p) for p in [Path(__file__),Path(__file__).with_name('HeaderProgram.cs'),Path(__file__).parent/'header-probe/HeaderProbe.csproj']},
 'readerFiles':{str(p.relative_to(reader)):{'bytes':p.stat().st_size,'sha256':sha(p)} for p in reader.rglob('*') if p.is_file()},
 'inputsSHA256':sha(out/'inputs.json'),'oracleSHA256':sha(out/'oracle.json'),'primaryRawSHA256':receipt['rawSHA256'],
 'qualification':False,'plannedRouting':'Physical left grid column, raw primary ASCII only, literal canonical English output native minimum .8 and supported padding; otherwise retain cached primary',
 'additionalPrimaryCalls':0,'inputPixelsModified':False,'scope':'Consumed cohort, new English header calls only; no heldout/device/full primary rerun credit'}
write(out/'freeze.json',freeze)
env={k:v for k,v in os.environ.items() if not any(s in k.upper() for s in ['TOKEN','SECRET','PASSWORD','CREDENTIAL'])}
env.update(ORT_TELEMETRY_DISABLED='1',DOTNET_CLI_TELEMETRY_OPTOUT='1')
with (out/'worker-stdout.txt').open('w') as stdout,(out/'worker-stderr.txt').open('w') as stderr:
 child=subprocess.run([a.dotnet,str(reader/'Takupoke.OcrComparison.dll'),str(out/'inputs.json'),str(out/'observations.jsonl')],env=env,stdout=stdout,stderr=stderr,timeout=240)
raw=out/'observations.jsonl';write(out/'raw-receipt.json',{'exitCode':child.returncode,'rawSHA256':sha(raw) if raw.exists() else None,'bytes':raw.stat().st_size if raw.exists() else None})
if child.returncode:raise RuntimeError('Retain execution failure unassessed')
assert all(sha(reader/name)==v['sha256'] for name,v in freeze['readerFiles'].items())
# No oracle reading until the new model worker is closed.
assert sha(out/'oracle.json')==freeze['oracleSHA256']
print(json.dumps({'closed':True,'newRawSHA256':sha(raw),'additionalPrimaryCalls':0}),flush=True)
