"""Fixed independent English LSTM reader on all native eligible development crops."""
import base64
import csv
import hashlib
import io
import json
import os
import subprocess
import time
from pathlib import Path
from prepare import sha,write

packet=Path('/workspace/scratch/glm-ocr-native-tails-20261016-v1/packet.json')
out=packet.parent/'tesseract-v2';out.mkdir(exist_ok=False);model=Path('/workspace/scratch/tesseract-eng-best-assets/eng.traineddata')
rows=json.loads(packet.read_text())['rows']
write(out/'freeze.json',dict(recipe='eng-best-original-native-tails-v1',packetSha256=sha(packet),modelSha256=sha(model),sourceSha256=sha(Path(__file__)),version=subprocess.check_output(['tesseract','--version'],stderr=subprocess.STDOUT,text=True),oem=1,pageSegmentationModes=[7,13],threads=1,systemAndFrequencyDawg=False,characterWhitelist=None,formatHandling='Remove exactly one terminal LF emitted by command-line text renderer, preserve all other text bytes.',scope='All61eligible oldnativecrops, no gold or error-based selection, no product activation. Word confidence is native Tesseract percentage, not CTC/calibrated field confidence.'))
started=time.monotonic()
with (out/'observations.jsonl').open('x') as stream:
 for row in rows:
  png=base64.b64decode(row['pngBase64'],validate=True);assert hashlib.sha256(png).hexdigest()==row['pngSha256'];image=out/(row['id']+'.png');image.write_bytes(png)
  for psm in [7,13]:
   prefix=out/(row['id']+'-psm'+str(psm));args=['tesseract',str(image),str(prefix),'--tessdata-dir',str(model.parent),'-l','eng','--oem','1','--psm',str(psm),'-c','load_system_dawg=0','-c','load_freq_dawg=0','-c','tessedit_create_txt=1','-c','tessedit_create_tsv=1']
   run=subprocess.run(args,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=30,env={**os.environ,'OMP_THREAD_LIMIT':'1'})
   if run.returncode:raise RuntimeError(run.stderr.decode())
   raw=prefix.with_suffix('.txt').read_text();text=raw[:-1] if raw.endswith('\n') else raw
   tsv=list(csv.DictReader(io.StringIO(prefix.with_suffix('.tsv').read_text()),delimiter='\t'))
   record=dict(id=row['id'],psm=psm,rawText=raw,text=text,nativeTsv=tsv,sourcePngSha256=row['pngSha256']);stream.write(json.dumps(record,ensure_ascii=False)+'\n');stream.flush()
write(out/'receipt.json',dict(exitCode=0,calls=len(rows)*2,seconds=time.monotonic()-started,rawSha256=sha(out/'observations.jsonl')))
print(json.dumps(dict(closed=True,calls=len(rows)*2,seconds=time.monotonic()-started)))
