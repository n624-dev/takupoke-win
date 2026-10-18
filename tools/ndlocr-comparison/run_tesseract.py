#!/usr/bin/env python3
"""Independent English-best arm; consumed 42 headers and six blank controls."""
import argparse
from collections import defaultdict
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT=Path(__file__).resolve().parent
PIXELS=ROOT.parent/'vision-ocr-comparison/fictional-pixels.json'
GOLD=ROOT.parent/'vision-ocr-comparison/oracle.json'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path,obj):
    path.write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--assets',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    args=p.parse_args()
    executable=Path(shutil.which('tesseract')).resolve()
    version=subprocess.check_output([str(executable),'--version'],text=True).splitlines()[0]
    model=args.assets/'eng.traineddata'
    if version!='tesseract 5.5.0' or model.stat().st_size!=15400601 or sha(model)!='8280aed0782fe27257a68ea10fe7ef324ca0f8d85bd2fd145d1c2b560bcb66ba':
        raise ValueError('Pinned recognizer changed')
    if sha(PIXELS)!='6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff':
        raise ValueError('Immutable input changed')
    args.output.mkdir(parents=True,exist_ok=False)
    ids=[f'n{i:03d}' for i in list(range(72,96))+list(range(168,192))]
    freeze={'recipe':'tesseract-eng-best-consumed-headers-v1','executable':str(executable),'executableSHA':sha(executable),
            'version':version,'modelRevision':'e12c65a915945e4c28e237a9b52bc4a8f39a0cec',
            'modelBytes':15400601,'modelSHA':sha(model),'pixelsSHA':sha(PIXELS),'oracleSHA':sha(GOLD),
            'ordinals':ids,'callsPlanned':48,'threads':1,'oem':1,'psm':7,
            'preprocessing':'Original PNG only; native Tesseract input processing, no enlargement/crop/whitelist',
            'txtProtocol':'Remove exactly one terminal LF; preserve every other character; raw TXT/TSV retained',
            'outputFlags':'tessedit_create_txt=1 and tessedit_create_tsv=1; no missing external config files',
            'sources':{f:sha(ROOT/f) for f in ('run_tesseract.py','tesseract_worker.py')},
            'scope':'Consumed header/blank component pixels; no new independent/full-document qualification'}
    write(args.output/'freeze.json',freeze)
    env={k:v for k,v in os.environ.items() if not any(s in k.upper() for s in ('TOKEN','SECRET','PASSWORD','CREDENTIAL'))}
    result=subprocess.run([sys.executable,str(ROOT/'tesseract_worker.py'),'--freeze',str(args.output/'freeze.json'),
                           '--pixels',str(PIXELS),'--assets',str(args.assets),'--output',str(args.output)],
                          env=env,capture_output=True,text=True,encoding='utf-8',timeout=1500)
    (args.output/'worker-stdout.txt').write_text(result.stdout,encoding='utf-8',newline='\n')
    (args.output/'worker-stderr.txt').write_text(result.stderr,encoding='utf-8',newline='\n')
    raw=args.output/'observations.jsonl'
    receipt={'exitCode':result.returncode,'rawSHA':sha(raw) if raw.exists() else None}
    write(args.output/'raw-receipt.json',receipt)
    if result.returncode:raise RuntimeError('Unassessed worker failure; outputs retained')
    if sha(GOLD)!=freeze['oracleSHA']:raise ValueError('Oracle changed')
    records=[json.loads(l) for l in raw.read_text(encoding='utf-8').splitlines()]
    if len(records)!=48 or {r['id'] for r in records}!=set(ids):raise ValueError('Incomplete/duplicate results')
    oracle={g['id']:g for g in json.loads(GOLD.read_text(encoding='utf-8'))}
    aggregates=defaultdict(lambda:{'rows':0,'exact':0,'wrong':0,'wrongWithWordConfidenceAtLeast80':0})
    for r in records:
        g=oracle[r['id']];a=aggregates[(g['cohort'],g['kind'])];exact=r['text']==g['literal']
        a['rows']+=1;a['exact']+=int(exact);a['wrong']+=int(not exact)
        a['wrongWithWordConfidenceAtLeast80']+=int(not exact and r['minimumWordConfidence'] is not None and r['minimumWordConfidence']>=80)
    report={**freeze,**receipt,'callsCompleted':len(records),'aggregates':[{'cohort':c,'kind':k,**a} for (c,k),a in sorted(aggregates.items())],
            'activation':False,'documentsQualified':0,'adoptions':0}
    write(args.output/'report.json',report)
    print(json.dumps({'callsCompleted':len(records),'aggregates':report['aggregates'],'worker':result.stdout},ensure_ascii=False))


if __name__=='__main__':main()
