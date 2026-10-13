#!/usr/bin/env python3
"""Fixed pretrained comparisons on consumed dense actual source-image PDFs."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

from run import ROOT, sha, write
from score import assess

MODELS = {
    'v5-server': {'bytes':84503027,'sha256':'d9dc333c9c7b042c6dffb8e33d72b6f65c9c1d463d0a3c2f78174fea55e94752','symbols':18385,
                  'dictionarySHA256':'d858428fbe0ada92b439a5f277b51bc1b318fd985d80946a3ce859d7f2446a92'},
    'v6-medium': {'bytes':76554979,'sha256':'9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba','symbols':18710,
                  'dictionarySHA256':'a49d51120786924a642c9e8a14b2a2ab73c019699abf5b2b6970ec7355c0415f'},
}


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--closed-development',type=Path,required=True)
    p.add_argument('--model',choices=MODELS,required=True)
    p.add_argument('--weights',type=Path,required=True)
    p.add_argument('--dictionary',type=Path,required=True)
    p.add_argument('--oracle',type=Path,required=True)
    p.add_argument('--ink-margin',type=int,choices=[0,2,4],default=2)
    p.add_argument('--positives-only',action='store_true')
    p.add_argument('--dotnet',required=True)
    p.add_argument('--output',type=Path,required=True)
    a = p.parse_args(); expected=MODELS[a.model]; source=a.closed_development
    if a.weights.stat().st_size != expected['bytes'] or sha(a.weights)!=expected['sha256'] or sha(a.dictionary)!=expected['dictionarySHA256']:
        raise ValueError('Pinned model/dictionary identity failed')
    if len(json.loads(a.dictionary.read_bytes())) != expected['symbols']: raise ValueError('Alphabet size changed')
    closed=json.loads((source/'report.json').read_text(encoding='utf-8'))
    if not closed['readerBinaryIdentityStable'] or sha(source/'observations.jsonl')!=closed['rawSHA256']:
        raise ValueError('Closed native dense source study changed')
    manifest=json.loads((source/'inputs.json').read_text(encoding='utf-8'))
    if len(manifest['documents'])!=6: raise ValueError('Fixed complete cohort changed')
    if a.positives_only:
        manifest['documents']=[d for d in manifest['documents'] if d['id'].endswith('-positive')]
        if len(manifest['documents'])!=2:raise ValueError('Fixed complete positive selection changed')
    for d in manifest['documents']: d['plannedConditions']=['ink-input-frame']
    out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
    for asset in [manifest['detector']] + [p['pixels'] for d in manifest['documents'] for p in d['pages']]:
        path=source/asset['file']
        if path.stat().st_size!=asset['bytes'] or sha(path)!=asset['sha256']: raise ValueError('Source/detector changed')
        shutil.copyfile(path,out/asset['file'])
    def copy_asset(path,name):
        shutil.copyfile(path,out/name)
        return {'file':name,'bytes':path.stat().st_size,'sha256':sha(path)}
    manifest['recognizer']=copy_asset(a.weights,'recognizer.onnx')
    manifest['dictionary']=copy_asset(a.dictionary,'dictionary.json')
    manifest['researchSymbols']=expected['symbols']
    manifest['researchDeadlineSeconds']=3300
    manifest['researchInkMargin']=a.ink_margin
    manifest['sourceScope']='Consumed six dense development PDFs; fixed pretrained model comparison, no final/qualification credit'
    write(out/'inputs.json',manifest)
    env={**os.environ,'DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_NOLOGO':'1','ORT_TELEMETRY_DISABLED':'1'}
    subprocess.run([a.dotnet,'build',str(ROOT/'Probe.csproj'),'-c','Release','--verbosity','quiet'],env=env,check=True,timeout=240)
    reader=out/'reader';shutil.copytree(ROOT/'bin/Release/net10.0',reader)
    binaries={str(p.relative_to(reader)):{'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(reader.rglob('*')) if p.is_file()}
    freeze={'recipe':'consumed-dense-pretrained-ink-v1','model':a.model,'modelIdentity':expected,
        'priorRawSHA256':closed['rawSHA256'],'inputsSHA256':sha(out/'inputs.json'),'oracleSHA256':sha(a.oracle),
        'sourceSHA256':{str(p.relative_to(ROOT.parents[1])):sha(p) for p in [ROOT/'Program.cs',ROOT/'InkCropFrame.cs',ROOT/'OriginalImageRead.cs',Path(__file__),ROOT/'prepare_pdf.py',ROOT.parents[1]/'src/Takupoke.Infrastructure/Recovery/RecoveryDocumentBuilder.cs']},
        'readerFiles':binaries,'plannedConditions':{d['id']:d['plannedConditions'] for d in manifest['documents']},'qualification':False,
        'nativeConfidenceFloor':.8,'originalInkMargin':a.ink_margin,'manualCorrections':0,
        'selection':'Both fixed complete positives, no negative qualification' if a.positives_only else 'All previously fixed positive and paired negative documents; no gold selection'}
    freeze['nativeGlobalDeadlineSeconds']=3300
    write(out/'freeze.json',freeze)
    subprocess.run([sys.executable,'-B',str(ROOT/'prepare_pdf.py'),'--manifest',str(out/'inputs.json')],check=True,timeout=120)
    safe={k:v for k,v in env.items() if not any(s in k.upper() for s in ('TOKEN','SECRET','PASSWORD','CREDENTIAL'))}
    dll=reader/'Takupoke.OcrComparison.dll'
    subprocess.run([a.dotnet,str(dll),'--extract-original-images',str(out/'pdf-inputs.json')],env=safe,check=True,timeout=240)
    with (out/'worker-stdout.txt').open('w') as stdout,(out/'worker-stderr.txt').open('w') as stderr:
        child=subprocess.run([a.dotnet,str(dll),str(out/'source-original-inputs.json'),str(out/'observations.jsonl')],env=safe,stdout=stdout,stderr=stderr,timeout=3400)
    raw=out/'observations.jsonl';write(out/'raw-receipt.json',{'exitCode':child.returncode,'bytes':raw.stat().st_size if raw.exists() else None,'rawSHA256':sha(raw) if raw.exists() else None})
    if child.returncode: raise RuntimeError('Incomplete native execution; retain unassessed raw')
    if any(sha(reader/name)!=identity['sha256'] for name,identity in binaries.items()): raise ValueError('Active binary changed')
    oracle=a.oracle
    if sha(oracle)!=freeze['oracleSHA256']: raise ValueError('Oracle changed')
    gold={d['id']:d for d in json.loads(oracle.read_text(encoding='utf-8'))}
    observations=[json.loads(l) for l in raw.read_text().splitlines()]
    if len(observations)!=len(manifest['documents']) or {(o['id'],o['condition']) for o in observations}!={(d['id'],'ink-input-frame') for d in manifest['documents']}:
        raise ValueError('Missing/duplicate condition; unassessed')
    scored=[]
    for o in observations:
        g=gold[o['id']]
        row=assess(o,g) if g['expect']=='exact' else {'id':o['id'],'condition':o['condition'],'outcome':o['outcome'],'failure':o['failure'],'quality':'unassessed' if o['outcome']=='execution-error-unassessed' else 'negative-refused-unattributed' if o['formal'] is None else 'negative-wrongly-returned','formalReturned':o['formal'] is not None}
        row.update({k:o[k] for k in ['requiredSlots','cells','lowConfidenceSources','recognizerCalls','detectorCalls','milliseconds','processPeakWorkingSetBytes']});scored.append(row)
    report={'recipe':freeze['recipe'],'model':a.model,'rawSHA256':sha(raw),'readerBinaryIdentityStable':True,'qualification':False,'observations':scored}
    write(out/'report.json',report);print(json.dumps(report,ensure_ascii=False),flush=True)


if __name__=='__main__':main()
