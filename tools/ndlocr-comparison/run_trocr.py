#!/usr/bin/env python3
"""Suitability of printed English OCR; existing consumed short header pixels."""
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


def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,x):p.write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    receipt=json.loads((a.assets/'download-receipt.json').read_text(encoding='utf-8'))
    if receipt['repository']!='microsoft/trocr-base-printed' or receipt['revision']!='93450be3f1ed40a930690d951ef3932687cc1892':raise ValueError('Official model revision changed')
    model=receipt['files']['model.safetensors']
    if model!={'bytes':1333384464,'sha256':'1cf4a6eedab26afaaf505f1c7f73d9634944924dbd1ed049d569db98039cd596'}:raise ValueError('Official LFS identity changed')
    if sha(PIXELS)!='6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff':raise ValueError('Fixed consumed pixel packet changed')
    ordinals=[f'n{x:03d}' for start in [72,168] for x in range(start,start+24)]
    a.output.mkdir(parents=True,exist_ok=False)
    freeze={**receipt,'recipe':'official-trocr-printed-consumed-headers-v1','pixelsSHA256':sha(PIXELS),'oracleSHA256':sha(GOLD),
        'plannedOrdinals':ordinals,'callsPlanned':48,'seed':17,'cpuThreads':2,'dtype':'float32','backend':'pil','attention':'eager',
        'processor':'Official native RGB384 bilinear/normalization, no custom crop/contrast/rotation','generation':{'do_sample':False,'num_beams':1,'max_new_tokens':32,'use_cache':True},
        'sourceSHA256':{p.name:sha(p) for p in [Path(__file__),ROOT/'trocr_worker.py']},
        'runtime':{name:importlib.metadata.version(name) for name in ['torch','transformers','Pillow','safetensors']},
        'licenseScope':'HF model card has no explicit weight license, no distribution or activation authorized by this study',
        'qualification':False,'scope':'Consumed 42 short headers and 6 blank crops, no heldout/source-glyph/PDF/full-document/Windows qualification'}
    write(a.output/'freeze.json',freeze)
    env={k:v for k,v in os.environ.items() if not any(s in k.upper() for s in ['TOKEN','SECRET','PASSWORD','CREDENTIAL'])}
    env.update({'HF_HUB_OFFLINE':'1','TRANSFORMERS_OFFLINE':'1','TOKENIZERS_PARALLELISM':'false'})
    raw=a.output/'observations.jsonl'
    with (a.output/'worker-stdout.txt').open('w',encoding='utf-8') as stdout,(a.output/'worker-stderr.txt').open('w',encoding='utf-8') as stderr:
        child=subprocess.run([sys.executable,str(ROOT/'trocr_worker.py'),'--assets',str(a.assets),'--pixels',str(PIXELS),'--freeze',str(a.output/'freeze.json'),'--output',str(raw)],env=env,stdout=stdout,stderr=stderr,timeout=1800)
    write(a.output/'raw-receipt.json',{'exitCode':child.returncode,'bytes':raw.stat().st_size if raw.exists() else None,'sha256':sha(raw) if raw.exists() else None})
    if child.returncode:raise RuntimeError('Native inference failure; retain partial observations, no quality credit')
    observations=[json.loads(l) for l in raw.read_text(encoding='utf-8').splitlines()]
    if len(observations)!=48 or {o['id'] for o in observations}!=set(ordinals):raise ValueError('Missing/duplicate raw observations')
    if sha(GOLD)!=freeze['oracleSHA256']:raise ValueError('Oracle changed')
    # No expected literal enters inference, prompts, decoder or crop selection.
    gold={o['id']:o for o in json.loads(GOLD.read_text(encoding='utf-8'))}
    aggregates=defaultdict(lambda:{'observed':0,'exact':0,'correctAboveDiagnosticFloor':0,'wrongAboveDiagnosticFloor':0})
    for o in observations:
        g=gold[o['id']];v=aggregates[(g['cohort'],g['kind'])];exact=o['text']==g['literal'];above=o['minimumGeneratedTokenProbabilityIncludingEos'] is not None and o['minimumGeneratedTokenProbabilityIncludingEos']>=.8
        v['observed']+=1;v['exact']+=int(exact);v['correctAboveDiagnosticFloor']+=int(exact and above);v['wrongAboveDiagnosticFloor']+=int(not exact and above)
    report={**freeze,'rawSHA256':sha(raw),'callsCompleted':48,'aggregates':[{'cohort':c,'kind':k,**v} for (c,k),v in sorted(aggregates.items())],
        'peakRssBytes':max(o['processPeakRssBytes'] for o in observations),'totalInferenceSeconds':sum(o['inferenceSeconds'] for o in observations),
        'confidenceScope':'Uncalibrated autoregressive token probabilities including EOS, not CTC scores or source evidence','modelActivation':False}
    write(a.output/'report.json',report);print(json.dumps({'callsCompleted':48,'aggregates':report['aggregates'],'peakRssBytes':report['peakRssBytes']}),flush=True)


if __name__=='__main__':main()
