#!/usr/bin/env python3
"""Closed native header measurements + unchanged-primary field diagnosis."""
import argparse,json,hashlib,subprocess
from pathlib import Path
from score import assess
ROOT=Path(__file__).resolve().parents[2]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,x):p.write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
p=argparse.ArgumentParser();p.add_argument('--study',type=Path,required=True);p.add_argument('--dotnet',required=True);a=p.parse_args();out=a.study
receipt=json.loads((out/'raw-receipt.json').read_text());assert receipt['exitCode']==0 and sha(out/'observations.jsonl')==receipt['rawSHA256']
freeze=json.loads((out/'freeze.json').read_text());assert sha(out/'oracle.json')==freeze['oracleSHA256']
subprocess.run([a.dotnet,'build',str(ROOT/'tools/ocr-practical-documents/Inspect.csproj'),'-c','Release','--nologo'],check=True)
inspector=ROOT/'tools/ocr-practical-documents/bin/Release/net10.0/Inspect.dll'
inspection_freeze={'sourceSHA256':{str(p.relative_to(ROOT)):sha(p) for p in [Path(__file__),Path(__file__).with_name('Program.cs'),ROOT/'src/Takupoke.Core/Recovery/RecoveryManualAssistance.cs']},'binarySHA256':sha(inspector),'nativeCalls':0,'rawSHA256':sha(out/'observations.jsonl')}
write(out/'inspection-freeze.json',inspection_freeze)
subprocess.run([a.dotnet,str(inspector),str(out/'observations.jsonl'),str(out/'v6-inputs.json'),str(out/'inspection.jsonl')],check=True)
gold={d['id']:d for d in json.loads((out/'oracle.json').read_text())};raw=[json.loads(l) for l in (out/'observations.jsonl').read_text().splitlines()];inspected={d['id']:d for d in [json.loads(l) for l in (out/'inspection.jsonl').read_text().splitlines()]}
assert len(raw)==len(gold)==len(inspected)==4
rows=[]
for r in raw:
 row=assess(r,inspected[r['id']],gold[r['id']]);row.update(headerCandidates=sum(d.get('headerCandidate',True) for d in r['decisions']),selectedHeaders=sum(d['chosen'] and d.get('headerCandidate',True) for d in r['decisions']),selectedUncertainHeaders=sum(d['chosen'] and d.get('headerCandidate',True) and d.get('belowConfidence',False) for d in r['decisions']),tailCandidates=sum(not d.get('headerCandidate',True) for d in r['decisions']),selectedTails=sum(d['chosen'] and not d.get('headerCandidate',True) for d in r['decisions']),cachedPrimaryRecognizerCalls=r['cachedPrimaryRecognizerCalls'])
 rows.append(row)
report={'recipe':freeze['recipe'],'observations':rows,'rawSHA256':sha(out/'observations.jsonl'),'qualification':False,'additionalPrimaryCalls':0,'additionalHeaderCalls':sum(r['recognizerCalls'] for r in raw),
 'limits':'Consumed complete development pixels and cached primary readings; new English inference for physically bounded headers only. No new primary recognition, actual user confirmations, output repairs, heldout/device or invalid-class refusal qualification.'}
write(out/'report.json',report)
for row in rows:print(json.dumps(row,ensure_ascii=False),flush=True)
