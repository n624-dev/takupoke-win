"""Original source-bound crops for independent local multimodal development OCR."""
import base64
import io
import json
import math
from pathlib import Path
from PIL import Image
from prepare import sha,write

source=Path('/workspace/scratch/ocr-auxiliary-native-development-20261016-v1')
out=Path('/workspace/scratch/glm-ocr-native-tails-20261016-v1');out.mkdir(exist_ok=False)
m=json.loads((source/'inputs.json').read_text());inputs={r['id']:r for r in m['images']}
rows=[]
for r in map(json.loads,(source/'raw.jsonl').read_text().splitlines()):
 if not r.get('eligible'):continue
 original=inputs[r['id']];pixels=source/original['pixels']['file']
 assert sha(pixels)==original['pixels']['sha256']
 crop=next(v['recognitionCrop'] for v in r['variants'] if v['cropPolicy']=='original')
 bounds=(math.ceil(crop['X']-.5),math.ceil(crop['Y']-.5),math.floor(crop['X']+crop['Width']-.5)+1,math.floor(crop['Y']+crop['Height']-.5)+1)
 with Image.frombytes('RGBA',(original['width'],original['height']),pixels.read_bytes(),'raw','BGRA') as image:
  image=image.convert('RGB').crop(bounds);stream=io.BytesIO();image.save(stream,format='PNG');png=stream.getvalue()
 import hashlib
 rows.append(dict(id=r['id'],pngBase64=base64.b64encode(png).decode(),pngSha256=hashlib.sha256(png).hexdigest(),sourceCrop=crop,originalPixelCentreBounds=bounds,originalPageSha256=original['pixels']['sha256']))
write(out/'packet.json',dict(rows=rows,scope='All61eligible original native fictional crops, no error-dependent selection. Pixel-centre region only, no resize/threshold/erasure, no vocabulary or reference text.'))
(out/'oracle.json').write_bytes((source/'oracle.json').read_bytes())
(out/'baseline.jsonl').write_bytes((source/'raw.jsonl').read_bytes())
write(out/'protocol.json',dict(scope='Consumed native firstpage DEVELOPMENT only; local GLM originalFP32 official processor/template Text Recognition: greedy128, CPU4. Native AR scores retained but never calibrated CTC/source positions.',selection='All eligible original source crops. No oracle to worker.',formatArms=['exact raw code','single line transport: optional ONE leading LF and ONE trailing LF OR one complete fenced text block; inner string must be bounded code, no other stripping/repair'],code='2..32 ASCIIletters/digits/_/-, at least one digit',fallback='actual unchanged v1 baseline',lengthPolicies=['same-length','bounded-code'],noAutomaticAdoption=True))
print(json.dumps(dict(output=str(out),calls=len(rows))))
