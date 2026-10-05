"""ONE original image. Geometry-only region plan; unchanged NDL native settings.
Each region uses required detector/default cascade once; actual origin translation.
No role/gold input, text dedup, character-box division, or alternate adoption.
"""
import sys,json,time,hashlib,types,threading,inspect
from pathlib import Path
import numpy as np
from PIL import Image
P=Path(__file__).resolve().parent;D=P.parent;R=Path('/workspace/recovery-research/windows-nonpaddle-ocr-20261004')
sys.path.insert(0,str(R/'ndl-source/src'));sys.path.insert(0,str(D/'trim-only-candidate'))
import ocr,onnxruntime
from pixel_trim import OriginalPixelProof
from token_stats import describe
from coordinates import translate
onnxruntime.disable_telemetry_events()
lock=threading.Lock();counters={'detectorCalls':0,'recognizerCalls':[0,0,0]};local=threading.local();state={'region':None,'image':None,'proof':None,'parentGuardFailures':[]}
def scalar(v):
 if isinstance(v,np.ndarray):return v.tolist()
 if isinstance(v,np.generic):return v.item()
 raise TypeError(type(v).__name__)
def emit(v):
 with lock:print(json.dumps(v,ensure_ascii=False,allow_nan=False,default=scalar),flush=True)
def context():return {'regionID':state['region']['id'],'originalRegionBox':state['region']['box']}
def viewbox(view):
 image=state['image'];offset=int(view.__array_interface__['data'][0])-int(image.__array_interface__['data'][0]);stride=image.strides[0]
 if view.dtype!=np.uint8 or view.ndim!=3 or view.shape[2]!=3 or view.strides!=image.strides or offset<0 or offset%3:return None
 y,x=divmod(offset,stride);x//=3;h,w=view.shape[:2]
 if x+w>image.shape[1] or y+h>image.shape[0]:return None
 return [x,y,x+w,y+h]
def trim_observe(image,box,index):
 try:
  crop,proof=state['proof'].trim(box,index)
  if proof.get('inkPixels',0)==0:state['parentGuardFailures'].append({'nativeIndex':index,'reason':'Native parent contains no original ink; not EMPTY proof'})
  emit({'type':'crop-proof',**context(),'nativeIndex':index,'record':proof,'topologyScope':'local ROI raster. Global original physical cell proof and complete non-rule planning ledger recorded separately; local component touches its cropped frame by construction.'});return crop,proof.get('trimmedBox')
 except ValueError as e:
  state['parentGuardFailures'].append({'nativeIndex':index,'reason':str(e)})
  emit({'type':'crop-proof',**context(),'nativeIndex':index,'originalBox':box,'whiteTrimApplied':False,'error':str(e),'sourceAdmissible':False});x,y,ex,ey=box;return image[y:ey,x:ex],None
original=inspect.getsource(ocr._run_ocr_on_image_array)
first='        lineimg = img[ymin:ymin + line_h, xmin:xmin + line_w, :]'
second='            lineimg = img[int(ymin):int(ymax), int(xmin):int(xmax), :]'
assert original.count(first)==1 and original.count(second)==1
h1='''        lineimg, trimmed_box = trim_observe(img, [xmin, ymin, xmin+line_w, ymin+line_h], idx)
        if trimmed_box is not None:
            tx,ty,tex,tey=trimmed_box
            for key,value in [("X",tx),("Y",ty),("WIDTH",tex-tx),("HEIGHT",tey-ty)]: lineobj.set(key,str(value))'''
h2='''            lineimg, trimmed_box = trim_observe(img, [int(xmin),int(ymin),int(xmax),int(ymax)], idx)
            if trimmed_box is not None:
                tx,ty,tex,tey=trimmed_box
                for key,value in [("X",tx),("Y",ty),("WIDTH",tex-tx),("HEIGHT",tey-ty)]: line_elem.set(key,str(value))'''
generated=original.replace(first,h1).replace(second,h2);ns=dict(vars(ocr));ns['trim_observe']=trim_observe;exec(compile(generated,'source-extracted-ndl-grid-roi','exec'),ns);run=ns['_run_ocr_on_image_array']
inputs=json.loads((D/'native-inputs.json').read_text());assert len(inputs['images'])==1;item=inputs['images'][0];path=Path(item['pngPath']);b=path.read_bytes();assert hashlib.sha256(b).hexdigest()==item['pngSHA256'];image=np.array(Image.open(path).convert('RGB'))
plan=json.loads((P/'regions-before-native.json').read_text());assert plan['completeOriginalNonRuleInkOwnership'];assert len(plan['regions'])<=128
model=R/'ndl-source/src/model';config=R/'ndl-source/src/config';a=types.SimpleNamespace(det_weights=str(model/'deim-s-1024x1024.onnx'),det_classes=str(config/'ndl.yaml'),det_score_threshold=.2,det_conf_threshold=.25,det_iou_threshold=.2,device='cpu',rec_classes=str(config/'NDLmoji.yaml'),enable_tcy=False)
emit({'type':'runtime','recipe':'NDL-physical-cell-and-pixel-heading-v1','originalPNG':item['pngSHA256'],'plannedRegions':len(plan['regions']),'onnxruntime':onnxruntime.__version__,'sourceFunctionSHA256':hashlib.sha256(original.encode()).hexdigest(),'generatedFunctionSHA256':hashlib.sha256(generated.encode()).hexdigest(),'modelSettingsUnchanged':True,'glyphboxesInvented':False,'originalPixelOnly':True,'uncalibratedTokenScores':True,'regionPlanningLedger':plan})
det=ocr.get_detector(a);detect=det.detect

def observed_detect(im):
 value=detect(im);counters['detectorCalls']+=1;emit({'type':'detections',**context(),'coordinateScope':'LOCAL region raster; exact integer origin maps to original image','detections':value});return value
det.detect=observed_detect
names=['parseq-ndl-24x256-30-tiny-189epoch-tegaki3-r8data-202604.onnx','parseq-ndl-24x384-50-tiny-300epoch-tegaki3-r8data-202604.onnx','parseq-ndl-24x768-100-tiny-153epoch-tegaki3-r8data-202604.onnx'];recs=[ocr.get_recognizer(a,str(model/n)) for n in names]
class Observer:
 def __init__(self,session,index,chars):self.session=session;self.index=index;self.chars=chars
 def __getattr__(self,k):return getattr(self.session,k)
 def run(self,*args,**kwargs):
  values=self.session.run(*args,**kwargs)
  with lock:counters['recognizerCalls'][self.index]+=1
  emit({'type':'token-logit-observation',**context(),'cascadeModelIndex':self.index,'cropContext':getattr(local,'crop_context',None),**describe(values[0],self.chars)});return values
for i,rec in enumerate(recs):
 rec.session=Observer(rec.session,i,rec.charlist);method=rec.read
 def observed_read(im,method=method,i=i):
  local.crop_context={'localActualRecognitionROI':viewbox(im),'originalInputShape':list(im.shape),'upstreamAspectRotationPredicate':bool(im.shape[0]>im.shape[1]*.8),'upstreamRotationWhenTrue':'cv2.ROTATE_90_COUNTERCLOCKWISE; unchanged original preprocessing, logged condition only','characterBoxes':None};text=method(im);emit({'type':'recognition',**context(),'cascadeModelIndex':i,'cropContext':local.crop_context,'text':text,'characterBoxes':None});return text
 rec.read=observed_read
results=[];global_units=[];errors=[];readreturned=0;regionsdone=0;guardfailures=[]
for region in plan['regions']:
 state['region']=region;state['parentGuardFailures']=[];x,y,ex,ey=region['box'];view=image[y:ey,x:ex];assert hashlib.sha256(np.ascontiguousarray(view).tobytes()).hexdigest()==region['originalRGBSHA256'];state['image']=view;state['proof']=OriginalPixelProof(view,np.zeros(view.shape[:2],bool));start=time.monotonic()
 try:
  result=run(det,*recs,'region-'+str(region['id'])+'.png',view,str(P/'owned-output'),save_viz=False);readreturned+=1;proof=state['proof'].ownership();emit({'type':'region-result',**context(),'returned':True,'milliseconds':(time.monotonic()-start)*1000,'result':result,'localOwnership':proof,'noLocalTopologyQualification':True})
  for u in result['json_lines']:
   if len(global_units)>=4096:raise ValueError('Global native unit capacity exceeded')
   value,inside=translate(u,region,len(global_units))
   if not inside:guardfailures.append({'regionID':region['id'],'originalNativeID':u['id'],'reason':'native exported box outside its original physical/pixel region; NOT clipped'})
   global_units.append(value)
  if state['parentGuardFailures']:guardfailures.append({'regionID':region['id'],'reason':'Original native crop parent guard failed','failures':state['parentGuardFailures']})
  if proof['sharedInkConflicts']:guardfailures.append({'regionID':region['id'],'reason':'Multiple local native parents share original nonwhite pixels','conflicts':proof['sharedInkConflicts']})
  results.append(region['id']);regionsdone+=1
 except Exception as e:
  errors.append({'regionID':region['id'],'errorType':type(e).__name__,'error':str(e)[:2048]});emit({'type':'region-result',**context(),'returned':False,'error':errors[-1],'milliseconds':(time.monotonic()-start)*1000})
emit({'type':'result','id':item['id'],'returned':not errors,'nativeCallCounters':counters,'publicOCRFunctionReturnedRegions':readreturned,'serializationCompletedRegions':regionsdone,'completedRegions':regionsdone,'plannedRegions':len(plan['regions']),'regionErrors':errors,'sourceGuardFailures':guardfailures,'result':{'json_lines':global_units},'sourceAdoptionEligible':not errors and not guardfailures,'sourceAdoptionEligibilityScope':'Original-region geometry/ownership guard only; actual global OCR ink coverage and host formal exact are checked independently. Transcription confidence calibration/production model qualification remain unproven; research results never activate catalog.','geometryRegionEligible':not errors and not guardfailures,'sourceScope':'Planning original non-rule ownership complete; actual native region guards/coverage and uncalibrated transcription safety separate. No formal qualification/automatic adoption.','fullFormal':'unassessed'})
