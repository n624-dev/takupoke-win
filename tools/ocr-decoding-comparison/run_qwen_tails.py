"""One fixed existing Qwen3-VL-2B Q8 reader; all eligible original native crops."""
import base64
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

source=Path(__file__).resolve().parents[1]/'vision-ocr-comparison/run.py'
sys.path.insert(0,str(source.parent));spec=importlib.util.spec_from_file_location('pinned_vision_reader',source);v=importlib.util.module_from_spec(spec);spec.loader.exec_module(v)
study=Path('/workspace/scratch/glm-ocr-native-tails-20261016-v1');out=study/'qwen';out.mkdir(exist_ok=False)
server=Path('/workspace/scratch/local-vision-runtime/llama-b11371/llama-server');models=Path('/workspace/scratch/local-vision-models')
name,model,model_sha,projector,projector_sha,prompt=v.MODELS[1]
if v.digest(server)!='67cc62bf21713881160b8ca69856d3899273ebe8924ae9aa8c99c2802967ee21':raise ValueError('Server pin changed')
if v.digest(models/model)!=model_sha or v.digest(models/projector)!=projector_sha:raise ValueError('Model pin changed')
rows=json.loads((study/'packet.json').read_text())['rows']
for row in rows:
 if __import__('hashlib').sha256(base64.b64decode(row['pngBase64'],validate=True)).hexdigest()!=row['pngSha256']:raise ValueError('Source PNG changed')
freeze=dict(recipe='qwen3vl2b-q8-all-native-original-tails-v1',model=model,modelSha256=model_sha,projector=projector,projectorSha256=projector_sha,serverSha256=v.digest(server),readerHelperSha256=v.digest(source),sourceSha256=v.digest(Path(__file__)),packetSha256=v.digest(study/'packet.json'),oracleSha256=v.digest(study/'oracle.json'),baselineSha256=v.digest(study/'baseline.jsonl'),protocolSha256=v.digest(study/'protocol.json'),callsPlanned=len(rows),prompt=prompt,threads=2,parallelSlots=1,contextTokens=4096,maximumOutputTokens=128,temperature=0,topK=1,topP=1,seed=17,gpuLayers=0,projectorOffload=False,promptCache=False,scope='Consumed native firstpage source-bound fictional code crops only, no gold to reader, no product activation or calibrated confidence/CTC proof.')
(out/'freeze.json').write_text(json.dumps(freeze,indent=2)+'\n');(out/'as-run-source.py').write_bytes(Path(__file__).read_bytes())
env={k:value for k,value in os.environ.items() if not any(s in k.upper() for s in ['TOKEN','SECRET','PASSWORD','CREDENTIAL'])};port=18793
command=[str(server),'-m',str(models/model),'--mmproj',str(models/projector),'--host','127.0.0.1','--port',str(port),'-ngl','0','--no-mmproj-offload','-c','4096','-t','2','-tb','2','-np','1','-fa','off','-fit','off','--no-warmup']
started=time.monotonic();completed=0;peak=0;success=False
with (out/'server.txt').open('xb') as log:
 child=subprocess.Popen(command,env=env,stdout=log,stderr=log,start_new_session=True)
 try:
  for attempt in range(600):
   if child.poll() is not None:raise RuntimeError('Reader stopped loading')
   try:
    if v.local_http(port,'health',timeout=1).get('status')=='ok':break
   except Exception:pass
   time.sleep(.25)
  else:raise TimeoutError('Loading deadline exceeded')
  with (out/'observations.jsonl').open('x') as stream:
   for row in rows:
    payload=dict(messages=[dict(role='user',content=[dict(type='image_url',image_url=dict(url='data:image/png;base64,'+row['pngBase64'])),dict(type='text',text=prompt)])],max_tokens=128,temperature=0,top_k=1,top_p=1,seed=17,cache_prompt=False,stream=False)
    timer=time.monotonic();response=v.local_http(port,'v1/chat/completions',payload,timeout=180);choices=response.get('choices',[])
    if len(choices)!=1:raise ValueError('Native choice count mismatch')
    text=choices[0]['message'].get('content')
    if not isinstance(text,str) or len(text.encode())>16384:raise ValueError('Native text bound failed')
    stream.write(json.dumps(dict(id=row['id'],text=text,finishReason=choices[0].get('finish_reason'),sourcePngSha256=row['pngSha256'],seconds=time.monotonic()-timer,response=response,confidenceKind='Uncalibrated autoregressive output; not CTC/native field confidence'),ensure_ascii=False)+'\n');stream.flush()
    completed+=1;peak=max(peak,v.rss(child.pid)['VmRSS'])
    if peak>10*1024**3 or time.monotonic()-started>2400:raise RuntimeError('Frozen resource budget exceeded')
    if completed%16==0:print(json.dumps(dict(completed=completed,planned=len(rows))),flush=True)
  success=True
 finally:
  if child.poll() is None:
   os.killpg(child.pid,signal.SIGTERM)
   try:child.wait(timeout=10)
   except subprocess.TimeoutExpired:os.killpg(child.pid,signal.SIGKILL);child.wait(timeout=10)
  raw=out/'observations.jsonl';receipt=dict(exitCode=0 if success else 1,completed=completed,seconds=time.monotonic()-started,peakRssBytes=peak,sha256=v.digest(raw) if raw.exists() else None,readerStopped=True)
  (out/'raw-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
print(json.dumps(receipt),flush=True)
