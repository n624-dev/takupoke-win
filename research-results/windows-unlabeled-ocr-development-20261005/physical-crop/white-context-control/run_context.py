"""Finite actual PARSeq-only context diagnostic; no detector or evidence repair."""
import json,sys,hashlib,time,types
from pathlib import Path
import numpy as np
from PIL import Image
from white_context import extend
P=Path(__file__).resolve().parent;G=P.parent;D=G.parent;R=Path('/workspace/recovery-research/windows-nonpaddle-ocr-20261004')
sys.path.insert(0,str(R/'ndl-source/src'));sys.path.insert(0,str(D/'trim-only-candidate'))
import ocr,onnxruntime
from token_stats import describe
onnxruntime.disable_telemetry_events()
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def spec(a):return {'shape':list(a.shape),'rgbSHA256':hashlib.sha256(np.ascontiguousarray(a).tobytes()).hexdigest()}
def emit(x):print(json.dumps(x,ensure_ascii=False,allow_nan=False),flush=True)
selected=json.loads((P/'selected-inputs.json').read_text());inputs=json.loads((D/'native-inputs.json').read_text());imagepath=Path(inputs['images'][0]['pngPath']);assert sha(imagepath)==selected['originalPNG'];image=np.array(Image.open(imagepath).convert('RGB'));assert len(selected['controls'])<=16
prepared=[]
for c in selected['controls']:
 first=None
 for o in c['occurrences']:
  x,y,ex,ey=o['originalRegionBox'];region=image[y:ey,x:ex];a,b,e,f=o['originalActualRecognitionROI'];old=region[b:f,a:e];new,newbox=extend(region,o['originalActualRecognitionROI'])
  assert spec(old)==c['oldInput'] and spec(new)==c['contextInput'] and newbox==o['whiteContextROI']
  if first is None:first=new
 prepared.append((c,first))
names=['parseq-ndl-24x256-30-tiny-189epoch-tegaki3-r8data-202604.onnx','parseq-ndl-24x384-50-tiny-300epoch-tegaki3-r8data-202604.onnx','parseq-ndl-24x768-100-tiny-153epoch-tegaki3-r8data-202604.onnx'];args=types.SimpleNamespace(rec_classes=str(R/'ndl-source/src/config/NDLmoji.yaml'),device='cpu',enable_tcy=False)
recs={i:ocr.get_recognizer(args,str(R/'ndl-source/src/model'/names[i])) for i in sorted({c['actualModelIndex'] for c,_ in prepared})}
current=None;sessioncalls=0
class Observer:
 def __init__(self,base,chars,index):self.base=base;self.chars=chars;self.index=index
 def __getattr__(self,k):return getattr(self.base,k)
 def run(self,*args,**kwargs):
  global sessioncalls
  values=self.base.run(*args,**kwargs);sessioncalls+=1;emit({'type':'token-logit-observation','controlID':current['id'],'actualModelIndex':self.index,**describe(values[0],self.chars)});return values
for i,r in recs.items():r.session=Observer(r.session,r.charlist,i)
emit({'type':'runtime','recipe':'original-horizontal-white-context-causal-control-v1','onnxruntime':onnxruntime.__version__,'plannedCalls':len(prepared),'detectorCalls':0,'originalPNG':selected['originalPNG'],'originalRawSHA256':selected['priorRawSHA256'],'recognitionSettingsUnchanged':True,'nativeInputScope':'only original RGB crops + original white pixels; no selected old text or gold passed to recognizer','baselineScope':'previous actual native output is preserved; no old input recognition repeated'})
results=[];errors=[]
for c,im in prepared:
 current=c;start=time.monotonic()
 try:
  text=recs[c['actualModelIndex']].read(im);row={'type':'control-result','controlID':c['id'],'returned':True,'text':text,'actualModelIndex':c['actualModelIndex'],'inputShape':list(im.shape),'upstreamCCWPredicate':bool(im.shape[0]>im.shape[1]*.8),'occurrences':c['occurrences'],'milliseconds':(time.monotonic()-start)*1000,'scope':'transcription/context diagnostic only; no replacement in prior output/source IDs/bounding boxes or formal adoption'};results.append(row);emit(row)
 except Exception as e:
  row={'type':'control-result','controlID':c['id'],'returned':False,'errorType':type(e).__name__,'error':str(e)[:1024]};errors.append(row);emit(row)
emit({'type':'result','returnedControls':len(results),'plannedControls':len(prepared),'actualPARSeqSessionCalls':sessioncalls,'detectorCalls':0,'errors':errors,'formalAssessed':0,'priorEvidenceChanged':False})
