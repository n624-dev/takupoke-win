#!/usr/bin/env python3
"""Fixed official plain-OCR protocol; weights and assets acquired before images."""
import argparse
from collections import defaultdict
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT=Path(__file__).resolve().parent
PIXELS=ROOT.parent/'vision-ocr-comparison/fictional-pixels.json'
GOLD=ROOT.parent/'vision-ocr-comparison/oracle.json'


def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path,obj):path.write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True);args=p.parse_args()
    receipt=json.loads((args.assets/'download-receipt.json').read_text(encoding='utf-8'))
    if receipt['revision']!='d3017ef2c2c1395888c8d635c5e0508bcb0ac78d':raise ValueError('Model revision mismatch')
    if sha(PIXELS)!='6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff':raise ValueError('Frozen pixel packet changed')
    args.output.mkdir(parents=True,exist_ok=False)
    freeze={'recipe':'got-ocr2-official-plain-consumed-pixels-v1','repository':receipt['repository'],
            'revision':receipt['revision'],'modelFiles':receipt['files'],'pixelsSHA':sha(PIXELS),'oracleSHA':sha(GOLD),
            'sources':{name:sha(ROOT/name) for name in ('run_got.py','got_worker.py')},
            'runtime':{x:importlib.metadata.version(x) for x in ('torch','transformers','Pillow','safetensors')},
            'callsPlanned':192,'device':'cpu','dtype':'float32','threads':2,'interopThreads':1,
            'attention':'eager','processor':'Native AutoProcessor PIL backend; format=False; no custom prompt or remote code',
            'generation':{'do_sample':False,'stop_strings':'<|im_end|>','max_new_tokens':128,'seed':17,'use_cache':True},
            'memoryEnvelopeBytes':8*1024**3,'globalDeadlineSeconds':5100,'qualification':'unassessed',
            'scope':'Consumed development pixels; raw exact literals, no source geometry or full-document credit'}
    write(args.output/'freeze.json',freeze)
    env={k:v for k,v in os.environ.items() if not any(s in k.upper() for s in ('TOKEN','SECRET','PASSWORD','CREDENTIAL'))}
    env.update({'HF_HUB_OFFLINE':'1','TRANSFORMERS_OFFLINE':'1','TOKENIZERS_PARALLELISM':'false'})
    print(json.dumps({'freezeSHA':sha(args.output/'freeze.json'),'callsPlanned':192}),flush=True)
    raw=args.output/'observations.jsonl'
    with (args.output/'worker-stdout.txt').open('w',encoding='utf-8',newline='\n') as stdout,(args.output/'worker-stderr.txt').open('w',encoding='utf-8',newline='\n') as stderr:
        child=subprocess.run([sys.executable,str(ROOT/'got_worker.py'),'--assets',str(args.assets),
                              '--pixels',str(PIXELS),'--freeze',str(args.output/'freeze.json'),'--output',str(raw)],
                             env=env,stdout=stdout,stderr=stderr,timeout=5400)
    receipt={'exitCode':child.returncode,'rawSHA':sha(raw) if raw.exists() else None}
    write(args.output/'raw-receipt.json',receipt)
    if child.returncode:raise RuntimeError('Unassessed worker failure; raw/console outputs retained')
    if sha(GOLD)!=freeze['oracleSHA']:raise ValueError('Oracle changed')
    # No oracle JSON is opened before the offline inference process has exited.
    records=[json.loads(x) for x in raw.read_text(encoding='utf-8').splitlines()]
    gold={x['id']:x for x in json.loads(GOLD.read_text(encoding='utf-8'))}
    if len(records)!=192 or {x['id'] for x in records}!=set(gold):raise ValueError('Incomplete/duplicate raw calls')
    aggregates=defaultdict(lambda:{'rows':0,'exact':0,'wrong':0,'tokenBudgetReached':0})
    for record in records:
        item=gold[record['id']]
        for kind in (item['kind'],'body' if item['kind'] in ('subject','teacher','room') else 'nonbody'):
            a=aggregates[(item['cohort'],kind)];exact=record['text']==item['literal']
            a['rows']+=1;a['exact']+=int(exact);a['wrong']+=int(not exact);a['tokenBudgetReached']+=int(record['tokenBudgetReached'])
    report={**freeze,**receipt,'callsCompleted':len(records),'aggregates':[{'cohort':c,'kind':k,**a} for (c,k),a in sorted(aggregates.items())],
            'peakRssBytes':max(r['processPeakRssBytes'] for r in records),'activation':False,'documentsQualified':0,'adoptions':0}
    write(args.output/'report.json',report)
    print(json.dumps({'callsCompleted':len(records),'aggregates':report['aggregates'],'peakRssBytes':report['peakRssBytes']},ensure_ascii=False),flush=True)


if __name__=='__main__':main()
