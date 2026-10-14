#!/usr/bin/env python3
"""Unmeasured v2 profile; does not reproduce the lost v1 Python source bytes."""
import argparse
import base64
import hashlib
import json
import math
import os
from pathlib import Path
import signal
import subprocess
import time

from run import digest, local_http, rss, PIXEL_SHA

ROOT = Path(__file__).resolve().parent
FILES = {
    'PaddleOCR-VL-1.6-GGUF.gguf':(935769056,'f3ae46ec885050acf4b3d31944431e1fd90d50664fb09126af4a3c050ba14ee8'),
    'PaddleOCR-VL-1.6-GGUF-mmproj.gguf':(881770560,'204d757d7610d9b3faab10d506d69e5b244e32bf765e2bab2d0167e65e0a058a'),
}


def write(p,v): p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


def main():
    p=argparse.ArgumentParser();p.add_argument('--server',required=True,type=Path)
    p.add_argument('--models',required=True,type=Path);p.add_argument('--output',required=True,type=Path);a=p.parse_args()
    a.output.mkdir(parents=True,exist_ok=False)
    receipt=json.loads((a.models/'download-receipt.json').read_text())
    if receipt['repository']!='PaddlePaddle/PaddleOCR-VL-1.6-GGUF' or receipt['revision']!='511b09642bb324401f15f97cc23bc67e8f0a291d':raise ValueError('Official revision changed')
    for name,(size,h) in FILES.items():
        if (a.models/name).stat().st_size!=size or digest(a.models/name)!=h:raise ValueError('Official GGUF changed')
    for n,v in receipt['files'].items():
        if digest(a.models/n)!=v['sha256']:raise ValueError('Pinned supporting artifact changed')
    if digest(a.server)!='67cc62bf21713881160b8ca69856d3899273ebe8924ae9aa8c99c2802967ee21':raise ValueError('Pinned llama.cpp b11371 launcher changed')
    if digest(ROOT/'fictional-pixels.json')!=PIXEL_SHA:raise ValueError('Original corpus changed')
    rows=json.loads((ROOT/'fictional-pixels.json').read_text())['rows']
    if len(rows)!=192 or len({r['id'] for r in rows})!=192:raise ValueError('Incomplete original corpus')
    for r in rows:
        if hashlib.sha256(base64.b64decode(r['pngBase64'],validate=True)).hexdigest()!=r['pngSha256']:raise ValueError('Original PNG changed')
    native={str(f.relative_to(a.server.parent)):digest(f) for f in sorted(a.server.parent.iterdir()) if f.is_file()}
    freeze={'recipe':'official-paddleocr-vl16-explicit-template-cpu2-v2','model':receipt,
        'nativeBundleFiles':native,'serverSha256':digest(a.server),'pixelsSha256':PIXEL_SHA,'oracleSha256':digest(ROOT/'oracle.json'),
        'sources':{f.name:digest(f) for f in [Path(__file__),ROOT/'run.py']},'callsPlanned':192,
        'prompt':'OCR:','chatTemplate':'Official pinned chat_template.jinja, passed explicitly; no source strings',
        'generation':{'temperature':0,'topK':1,'topP':1,'seed':17,'maxTokens':128,'logprobs':True},
        'cpuThreads':2,'gpuLayers':0,'projectorOffload':False,'contextTokens':4096,'parallelSlots':1,
        'cachePrompt':False,'maximumRssBytes':8*1024**3,'deadlineSeconds':2700,
        'scope':'Consumed fictional original192PNG component comparison; no final/nativeWindows/source-glyph/full-document qualification',
        'qualification':False,'modelActivation':False}
    write(a.output/'freeze.json',freeze)
    snapshots=a.output/'as-run-source';snapshots.mkdir()
    for f in [Path(__file__),ROOT/'run.py']:
        (snapshots/f.name).write_bytes(f.read_bytes())
    print(json.dumps({'frozen':digest(a.output/'freeze.json'),'planned':192}),flush=True)
    env={k:v for k,v in os.environ.items() if not any(s in k.upper() for s in ['TOKEN','SECRET','PASSWORD','CREDENTIAL'])}
    port=18796;completed=0;started=time.monotonic();raw=a.output/'observations.jsonl';status='initialization-failed';resource=None
    command=[str(a.server),'-m',str(a.models/'PaddleOCR-VL-1.6-GGUF.gguf'),'--mmproj',str(a.models/'PaddleOCR-VL-1.6-GGUF-mmproj.gguf'),
        '--chat-template-file',str(a.models/'chat_template.jinja'),'--jinja','--host','127.0.0.1','--port',str(port),
        '-ngl','0','--no-mmproj-offload','-c','4096','-t','2','-tb','2','-np','1','-fa','off','-fit','off','--no-warmup']
    with (a.output/'native-server.txt').open('wb') as log:
        process=subprocess.Popen(command,env=env,stdout=log,stderr=log,start_new_session=True)
        try:
            ready=False
            for _ in range(600):
                if process.poll() is not None:raise RuntimeError('Native reader failed before measurement')
                try:
                    if local_http(port,'health',timeout=1).get('status')=='ok':ready=True;break
                except Exception:pass
                time.sleep(.25)
            if not ready:raise TimeoutError('Native initialization deadline')
            status='partial-inference'
            with raw.open('x',encoding='utf-8',newline='\n') as stream:
                for r in rows:
                    if time.monotonic()-started>2700:raise TimeoutError('Fixed total inference budget')
                    payload={'messages':[{'role':'user','content':[{'type':'image_url','image_url':{'url':'data:image/png;base64,'+r['pngBase64']}},
                        {'type':'text','text':'OCR:'}]}],'max_tokens':128,'temperature':0,'top_k':1,'top_p':1,'seed':17,
                        'cache_prompt':False,'stream':False,'logprobs':True,'top_logprobs':1}
                    timer=time.monotonic();response=local_http(port,'v1/chat/completions',payload,timeout=180)
                    choices=response.get('choices',[])
                    if len(choices)!=1:raise ValueError('Unexpected native choice count')
                    text=choices[0]['message'].get('content')
                    if not isinstance(text,str) or len(text.encode())>16384:raise ValueError('Native output exceeds bound')
                    logs=(choices[0].get('logprobs') or {}).get('content') or []
                    probabilities=[math.exp(t['logprob']) for t in logs]
                    if any(not math.isfinite(v) or v<0 or v>1 for v in probabilities):raise ValueError('Invalid native logprob')
                    record={'id':r['id'],'text':text,'pngSha256':r['pngSha256'],'finishReason':choices[0].get('finish_reason'),
                        'seconds':time.monotonic()-timer,'response':response,'minimumReportedTokenProbability':min(probabilities) if probabilities else None,
                        'confidenceScope':'Reported generated token probabilities; EOS coverage unconfirmed, uncalibrated, not CTC/source ownership',
                        'productionAdoption':False}
                    stream.write(json.dumps(record,ensure_ascii=False,separators=(',',':'))+'\n');stream.flush();os.fsync(stream.fileno())
                    completed+=1;resource=rss(process.pid)
                    if resource['VmRSS']>8*1024**3:raise MemoryError('Fixed CPU16GB memory budget')
                    if completed%24==0:print(json.dumps({'completed':completed,'planned':192,'rss':resource}),flush=True)
            status='closed-complete'
        finally:
            if process.poll() is None:
                os.killpg(process.pid,signal.SIGTERM)
                try:process.wait(timeout=10)
                except subprocess.TimeoutExpired:os.killpg(process.pid,signal.SIGKILL);process.wait(timeout=10)
            write(a.output/'raw-receipt.json',{'status':status,'completed':completed,'rawSha256':digest(raw) if raw.exists() else None,
                'bytes':raw.stat().st_size if raw.exists() else None,'nativeLogSha256':digest(a.output/'native-server.txt'),
                'readerStopped':process.poll() is not None,'processExitCode':process.returncode,'resources':resource,'qualification':False})
    if completed!=192:raise ValueError('Incomplete calls; no partial precision credit')
    if any(digest(a.server.parent/n)!=h for n,h in native.items()) or digest(ROOT/'oracle.json')!=freeze['oracleSha256']:raise ValueError('Frozen identity changed')
    # The local reader is stopped and raw closed before any expected literal.
    gold={r['id']:r for r in json.loads((ROOT/'oracle.json').read_text())};groups={}
    for line in raw.read_text().splitlines():
        r=json.loads(line);g=gold[r['id']];v=groups.setdefault(g['cohort']+'/'+g['kind'],{'rows':0,'rawExact':0,'wrongAboveDiagnosticFloor':0,'correctAboveDiagnosticFloor':0,'truncated':0})
        exact=r['text']==g['literal'];above=bool(r['text']) and r['finishReason']=='stop' and r['minimumReportedTokenProbability'] is not None and r['minimumReportedTokenProbability']>=.8
        v['rows']+=1;v['rawExact']+=exact;v['wrongAboveDiagnosticFloor']+=not exact and above;v['correctAboveDiagnosticFloor']+=exact and above;v['truncated']+=r['finishReason']!='stop'
    report={'groups':groups,'callsCompleted':completed,'rawSha256':digest(raw),'resources':resource,'elapsedSeconds':time.monotonic()-started,
        'qualification':False,'modelActivation':False,'limits':'No trimming, Markdown/Unicode/space repair, output boxes or invented calibrated confidence; Linux CPU under concurrent research, not Windows speed.'}
    write(a.output/'report.json',report);print(json.dumps(report,ensure_ascii=False),flush=True)


if __name__=='__main__':main()
