import json,math,hashlib
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parent
raw=Path('/workspace/review-notes/windows-ocr-preprocess-development-v2-full.log')
report=json.loads(raw.read_text().splitlines()[-1]);inputs=json.loads((ROOT/'inputs.json').read_text())
lookup={(d['id'],p['page']):p for d in inputs['documents'] for p in d['pages']}
output=[]
for row in report['pages']:
 if not row['ocrReadReturned']:continue
 p=lookup[row['id'],row['page']];W=p['width'];H=p['height'];pixels=Path(p['path']).read_bytes()
 if hashlib.sha256(pixels).hexdigest()!=row['bgraSha256']:raise ValueError('original pixels changed')
 bgra=np.frombuffer(pixels,np.uint8).reshape(H,W,4);ink=np.any(bgra[:,:,:3]!=255,axis=2);covered=np.zeros((H,W),bool)
 # Independent replay of production RuleMask + HasUnrecognizedInk, no OCR calls.
 for r in row['rules']:
  if r['Horizontal']:
   first=max(0,min(W-1,math.ceil(min(r['X1'],r['X2']))));last=max(0,min(W-1,math.floor(max(r['X1'],r['X2']))))
   for y in range(max(0,math.floor(r['Y1'])-2),min(H-1,math.ceil(r['Y1'])+2)+1):
    if first<last and ink[y,first:last+1].all():covered[y,first:last+1]=True
  elif r['Vertical']:
   first=max(0,min(H-1,math.ceil(min(r['Y1'],r['Y2']))));last=max(0,min(H-1,math.floor(max(r['Y1'],r['Y2']))))
   for x in range(max(0,math.floor(r['X1'])-2),min(W-1,math.ceil(r['X1'])+2)+1):
    if first<last and ink[first:last+1,x].all():covered[first:last+1,x]=True
 for b in row['recognized']:
  left=max(0,math.floor(b['X'])-1);top=max(0,math.floor(b['Y'])-1);right=min(W-1,math.ceil(b['X']+b['Width'])+1);bottom=min(H-1,math.ceil(b['Y']+b['Height'])+1)
  covered[top:bottom+1,left:right+1]=True
 ys,xs=np.where(ink&~covered)
 if (len(xs)==0)!=row['completeInk']:raise ValueError('coverage replay differs actual production flag')
 source=Path('/workspace/recovery-research/windows-raster-independent-heldout-v3-20261004')/row['id']/'drawing-source.json'
 drawing=json.loads(source.read_text())['pages'][row['page']-1]['textsAndInk']
 refs=[]
 for text in drawing:
  a,b,c,d=text['bbox'];count=int(np.sum((xs>=a)&(xs<c)&(ys>=b)&(ys<d)))
  if count:refs.append({'originalPrintedSpan':text['text'],'bbox':text['bbox'],'uncoveredPixels':count})
 output.append({'id':row['id'],'page':row['page'],'count':len(xs),'boundsInclusive':[int(xs.min()),int(ys.min()),int(xs.max()),int(ys.max())],'originalPrintedSpans':refs,'first32Pixels':[[int(x),int(y),bgra[y,x,:3].tolist()] for y,x in zip(ys[:32],xs[:32])],'drawingSourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'cause':'Actual complete-text return still leaves original printed pixels outside recognized boxes and physical rule mask. Protective wholepage-ink guard rejects. No box inflation/threshold change applied.'})
(ROOT/'posthoc-coverage.json').write_text(json.dumps({'scope':'No new native inference; original drawing geometry used posthoc only, never model input. Remaining failed CTC pages cannot be localized from API evidence.','pages':output},ensure_ascii=False,indent=2)+'\n')
print(json.dumps(output,ensure_ascii=False))
