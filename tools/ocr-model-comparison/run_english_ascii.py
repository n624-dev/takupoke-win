#!/usr/bin/env python3
"""English V5 on complete consumed ASCII development images, not Japanese crops."""
import argparse
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import resource
import subprocess
import sys
import time

ROOT=Path(__file__).resolve().parents[2]
MODEL_SHA='b5f833dfc5d0eb71da397b4efa06ebeee9b431b690a47d6af40d77d8eabc557f'
DICT_SHA='ef84489f927fb92b64714c7f110404e5bfb83c4067bd609fe4c41d5f23e25659'


def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


def worker(a):
    import numpy as np
    import onnxruntime as ort
    from PIL import Image
    sys.path.insert(0,str(ROOT/'tools/ocr-synthetic-training'))
    from image_tensor import tensor,greedy
    if sha(a.assets/'inference.onnx')!=MODEL_SHA or sha(a.assets/'dictionary.json')!=DICT_SHA:raise ValueError('Pinned assets changed')
    options=ort.SessionOptions();options.intra_op_num_threads=2;options.inter_op_num_threads=1
    options.log_severity_level=3;session=ort.InferenceSession(str(a.assets/'inference.onnx'),sess_options=options,providers=['CPUExecutionProvider'])
    dictionary=json.loads((a.assets/'dictionary.json').read_text());rows=json.loads((a.output/'inputs.json').read_text());started=time.monotonic()
    with (a.output/'observations.jsonl').open('x',encoding='utf-8',newline='\n') as stream:
        for number,row in enumerate(rows,1):
            p=a.output/row['file']
            if sha(p)!=row['sha256']:raise ValueError('Frozen image changed')
            with Image.open(p) as image:array,valid=tensor(image)
            timer=time.monotonic();outputs=session.run(None,{'x':array[None]});elapsed=time.monotonic()-timer
            if len(outputs)!=1 or outputs[0].ndim!=3 or outputs[0].shape[0]!=1:raise ValueError('Native graph output changed')
            probabilities=outputs[0][0];pieces=greedy(probabilities,dictionary);text=''.join(p['Text'] for p in pieces)
            supported=all(p['End']*array.shape[-1]<=valid*probabilities.shape[0] for p in pieces)
            minimum=min((p['Confidence'] for p in pieces),default=None)
            ascii_domain=all(32<=ord(c)<=126 for c in text)
            record={'id':row['id'],'text':text,'pieces':pieces,'minimumConfidence':minimum,'unsupportedPadding':not supported,
                'printableAscii':ascii_domain,'diagnosticAccepted':bool(text) and minimum is not None and minimum>=.8 and supported and ascii_domain,
                'inputWidth':array.shape[-1],'validWidth':valid,'timeSteps':probabilities.shape[0],'sourceRect':row['sourceRect'],
                'inputTensorSha256':hashlib.sha256(array.tobytes()).hexdigest(),'inferenceMilliseconds':elapsed*1000}
            stream.write(json.dumps(record,ensure_ascii=False,separators=(',',':'))+'\n');stream.flush()
            if number%128==0:print(json.dumps({'completed':number,'planned':512}),flush=True)
    write(a.output/'worker-report.json',{'callsCompleted':len(rows),'elapsedSeconds':time.monotonic()-started,
        'peakRssBytes':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss*1024,'qualification':False})


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',type=Path,required=True);p.add_argument('--development',type=Path)
    p.add_argument('--output',type=Path,required=True);p.add_argument('--worker',action='store_true');a=p.parse_args()
    if a.worker:worker(a);return
    from PIL import Image,ImageChops
    a.output.mkdir(parents=True,exist_ok=False);manifest=a.development/'manifest.json';gold=json.loads(manifest.read_text())
    if len(gold['rows'])!=512 or len({r['id'] for r in gold['rows']})!=512:raise ValueError('Expected closed ASCII-v1 corpus')
    if any(not r['reject'] and r['text']!=r['target'] for r in gold['rows']):raise ValueError('Positive literal mismatch')
    dictionary=json.loads((a.assets/'dictionary.json').read_text());missing=[chr(c) for c in range(32,127) if chr(c) not in dictionary]
    rows=[]
    for number,row in enumerate(gold['rows']):
        original=a.development/row['file']
        if sha(original)!=row['sha256']:raise ValueError('Original image changed')
        with Image.open(original) as source:
            image=source.convert('RGB');box=ImageChops.difference(image,Image.new('RGB',image.size,'white')).getbbox()
            if box:box=(max(0,box[0]-2),max(0,box[1]-2),min(image.width,box[2]+2),min(image.height,box[3]+2))
            else:box=(0,0,image.width,image.height)
            frame=image.crop(box);path=a.output/(f'p{number:04d}.png');frame.save(path)
            rows.append({'id':row['id'],'file':path.name,'sha256':sha(path),'sourceRect':[box[0],box[1],box[2]-box[0],box[3]-box[1]]})
    write(a.output/'inputs.json',rows)
    sources=[Path(__file__),ROOT/'tools/ocr-synthetic-training/image_tensor.py']
    freeze={'recipe':'english-v5-whole-ink-ascii-consumed-v1','modelSha256':MODEL_SHA,'dictionarySha256':DICT_SHA,
        'manifestSha256':sha(manifest),'inputsSha256':sha(a.output/'inputs.json'),'missingPrintableAscii':missing,
        'runtime':{n:importlib.metadata.version(n) for n in ['onnxruntime','numpy','Pillow']},
        'sources':{str(p.relative_to(ROOT)):sha(p) for p in sources},'planned':512,
        'condition':'Whole original line ink +2 original px; no Japanese prefix removal; BGR48 uint8-rounded half-pixel emulation/min320/max960/normalized-zero padding, full438 greedy CTC, own min.8 and ASCII domain diagnostic',
        'scope':'Consumed ASCII-v1 development, no unused-font/final/document/Windows qualification; Python FP32 sampler emulates app, does not claim native byte parity',
        'qualification':False,'modelActivation':False}
    write(a.output/'freeze.json',freeze);snapshot=a.output/'as-run-source';snapshot.mkdir()
    for source in sources:(snapshot/source.name).write_bytes(source.read_bytes())
    env={**os.environ,'ORT_TELEMETRY_DISABLED':'1'}
    with (a.output/'worker-stdout.txt').open('w') as stdout,(a.output/'worker-stderr.txt').open('w') as stderr:
        child=subprocess.run([sys.executable,str(Path(__file__).resolve()),'--worker','--assets',str(a.assets),'--output',str(a.output)],env=env,stdout=stdout,stderr=stderr,timeout=900)
    raw=a.output/'observations.jsonl';write(a.output/'raw-receipt.json',{'exitCode':child.returncode,'sha256':sha(raw) if raw.exists() else None,'bytes':raw.stat().st_size if raw.exists() else None})
    if child.returncode:raise RuntimeError('Worker failed; preserve partial raw, no precision credit')
    if sha(manifest)!=freeze['manifestSha256'] or any(sha(ROOT/n)!=h for n,h in freeze['sources'].items()):raise ValueError('Source/oracle changed')
    records=[json.loads(l) for l in raw.read_text().splitlines()];expected={r['id']:r for r in gold['rows']}
    if len(records)!=512 or {r['id'] for r in records}!=set(expected):raise ValueError('Missing/duplicate calls')
    groups={}
    for r in records:
        g=expected[r['id']];v=groups.setdefault(g['kind'],{'rows':0,'exactPositive':0,'correctAccepted':0,'wrongAcceptedPositive':0,'acceptedNegative':0})
        exact=not g['reject'] and r['text']==g['text'];accepted=r['diagnosticAccepted'];v['rows']+=1;v['exactPositive']+=exact
        v['correctAccepted']+=exact and accepted;v['wrongAcceptedPositive']+=not g['reject'] and not exact and accepted;v['acceptedNegative']+=g['reject'] and accepted
    report={'rawSha256':sha(raw),'callsCompleted':512,'groups':groups,'worker':json.loads((a.output/'worker-report.json').read_text()),'qualification':False,'modelActivation':False}
    write(a.output/'report.json',report);print(json.dumps(report,ensure_ascii=False),flush=True)


if __name__=='__main__':main()
