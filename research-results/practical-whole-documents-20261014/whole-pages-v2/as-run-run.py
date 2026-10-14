#!/usr/bin/env python3
"""Run fixed whole-page arms; gold is read only after both native readers close."""
import argparse, json, os, shutil, subprocess, sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'ocr-physical-framing'))
from run import sha, write
ROOT=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser();p.add_argument('--cohort',type=Path,required=True);p.add_argument('--dotnet',required=True);p.add_argument('--v6',type=Path,required=True);p.add_argument('--v6-dictionary',type=Path,required=True)
a=p.parse_args();out=a.cohort.resolve()
if sha(a.v6)!='9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba' or sha(a.v6_dictionary)!='a49d51120786924a642c9e8a14b2a2ab73c019699abf5b2b6970ec7355c0415f':raise ValueError('V6 pins changed')
if len(json.loads(a.v6_dictionary.read_bytes()))!=18710:raise ValueError('V6 alphabet changed')
subprocess.run([sys.executable,'-B',str(ROOT/'tools/ocr-physical-framing/prepare_pdf.py'),'--manifest',str(out/'inputs.json')],check=True)
reader=out/'reader';shutil.copytree(ROOT/'tools/ocr-physical-framing/bin/Release/net10.0',reader)
subprocess.run([a.dotnet,str(reader/'Takupoke.OcrComparison.dll'),'--extract-original-images',str(out/'pdf-inputs.json')],check=True)
manifest=json.loads((out/'source-original-inputs.json').read_text())
manifest['sourceScope']='New invented readable three-class complete development image PDFs, exact original-image extraction; no native Windows rendering or heldout qualification'
write(out/'v5-inputs.json',manifest)
v6=json.loads(json.dumps(manifest))
for d in v6['documents']:d['plannedConditions']=['ink-input-frame']
for key,path in [('recognizer',a.v6),('dictionary',a.v6_dictionary)]:
 target=out/('v6-'+path.name);shutil.copyfile(path,target);v6[key]={'file':target.name,'bytes':target.stat().st_size,'sha256':sha(target)}
v6['researchSymbols']=18710
write(out/'v6-inputs.json',v6)
binaries={str(f.relative_to(reader)):{'bytes':f.stat().st_size,'sha256':sha(f)} for f in sorted(reader.rglob('*')) if f.is_file()}
sources=[f for d in ['src/Takupoke.Core/Recovery','src/Takupoke.Infrastructure/Recovery','tools/ocr-physical-framing','tools/ocr-practical-documents'] for f in (ROOT/d).glob('*') if f.suffix in ['.cs','.py','.csproj']]
freeze={'recipe':'practical-complete-three-class-v1','generationSHA256':sha(out/'generation-freeze.json'),'oracleSHA256':sha(out/'oracle.json'),'readerFiles':binaries,'sourceSHA256':{str(f.relative_to(ROOT)):sha(f) for f in sources},'armManifests':{name:sha(out/(name+'-inputs.json')) for name in ['v5','v6']},'nativeConfidenceFloor':.8,'perArmDeadlineSeconds':3300,'automaticHumanCorrection':False,'qualification':False}
write(out/'reader-freeze.json',freeze)
env={k:v for k,v in os.environ.items() if not any(s in k.upper() for s in ['TOKEN','SECRET','PASSWORD','CREDENTIAL'])}
env.update(DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1',ORT_TELEMETRY_DISABLED='1')
for arm in ['v5','v6']:
 if sha(out/(arm+'-inputs.json'))!=freeze['armManifests'][arm]:raise ValueError('Arm changed')
 with (out/(arm+'-stdout.txt')).open('w') as stdout,(out/(arm+'-stderr.txt')).open('w') as stderr:
  child=subprocess.run([a.dotnet,str(reader/'Takupoke.OcrComparison.dll'),str(out/(arm+'-inputs.json')),str(out/(arm+'-observations.jsonl'))],env=env,stdout=stdout,stderr=stderr,timeout=3400)
 raw=out/(arm+'-observations.jsonl');write(out/(arm+'-receipt.json'),{'exitCode':child.returncode,'rawSHA256':sha(raw) if raw.exists() else None,'bytes':raw.stat().st_size if raw.exists() else None})
 print(json.dumps({'arm':arm,'exitCode':child.returncode,'closed':True}),flush=True)
 if child.returncode:raise RuntimeError('Partial arm remains unassessed')
 if any(sha(reader/name)!=identity['sha256'] for name,identity in binaries.items()):raise ValueError('Active reader changed')
# Both inference arms are closed before any comparison with the oracle.
if sha(out/'oracle.json')!=freeze['oracleSHA256']:raise ValueError('Gold changed')
subprocess.run([sys.executable,'-B',str(Path(__file__).with_name('score.py')),'--cohort',str(out)],check=True)
