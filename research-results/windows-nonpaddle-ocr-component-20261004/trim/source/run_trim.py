"""Same native detector/cascade; only original-pixel white outer trim changes crops.
Source extraction is exact before two explicit ROI observation substitutions.
All proposed source ownership remains diagnostic/rejected when not proved.
"""
import sys,json,time,hashlib,types,threading,inspect
from pathlib import Path
import numpy as np
import cv2,PIL
from PIL import Image
HERE=Path(__file__).resolve().parent;ROOT=HERE.parent
sys.path.insert(0,str(ROOT/'ndl-source/src'))
import ocr,onnxruntime
from pixel_trim import OriginalPixelProof
from token_stats import describe
onnxruntime.disable_telemetry_events()
lock=threading.Lock();local=threading.local();current={'id':None,'image':None,'proof':None}
def scalar(v):
 if isinstance(v,np.ndarray):return v.tolist()
 if isinstance(v,np.generic):return v.item()
 raise TypeError(type(v).__name__)
def emit(v):
 with lock:print(json.dumps(v,ensure_ascii=False,allow_nan=False,default=scalar),flush=True)
def bbox_view(view):
 img=current['image'];offset=int(view.__array_interface__['data'][0])-int(img.__array_interface__['data'][0]);stride=img.strides[0]
 if view.dtype!=np.uint8 or view.ndim!=3 or view.shape[2]!=3 or view.strides!=img.strides or offset<0 or offset%3: return None
 y,x=divmod(offset,stride);x//=3;h,w=view.shape[:2]
 if x+w>img.shape[1] or y+h>img.shape[0]:return None
 return [x,y,x+w,y+h]
def trim_observe(image,original_box,index):
 try:
  crop,record=current['proof'].trim(original_box,index)
  emit({'type':'crop-proof','id':current['id'],'nativeIndex':index,'record':record,'ownershipCheckPending':True})
  return crop,record.get('trimmedBox')
 except ValueError as error:
  x,y,ex,ey=original_box
  invalid={'nativeIndex':index,'originalBox':original_box,'trimmedBox':None,'sourceAdmissible':False,'whiteTrimApplied':False,'error':str(error),'noPosthocClamping':True}
  current['proof'].regions.append(invalid)
  emit({'type':'crop-proof','id':current['id'],**invalid})
  # Preserve baseline NumPy view solely for measurement; this source is rejected.
  return image[y:ey,x:ex],None
original=inspect.getsource(ocr._run_ocr_on_image_array)
first='        lineimg = img[ymin:ymin + line_h, xmin:xmin + line_w, :]'
second='            lineimg = img[int(ymin):int(ymax), int(xmin):int(xmax), :]'
assert original.count(first)==1 and original.count(second)==1
hook1='''        lineimg, trimmed_box = trim_observe(img, [xmin, ymin, xmin+line_w, ymin+line_h], idx)
        if trimmed_box is not None:
            tx,ty,tex,tey=trimmed_box
            for key,value in [("X",tx),("Y",ty),("WIDTH",tex-tx),("HEIGHT",tey-ty)]: lineobj.set(key,str(value))'''
hook2='''            lineimg, trimmed_box = trim_observe(img, [int(xmin),int(ymin),int(xmax),int(ymax)], idx)
            if trimmed_box is not None:
                tx,ty,tex,tey=trimmed_box
                for key,value in [("X",tx),("Y",ty),("WIDTH",tex-tx),("HEIGHT",tey-ty)]: line_elem.set(key,str(value))'''
generated=original.replace(first,hook1).replace(second,hook2)
namespace=dict(vars(ocr));namespace['trim_observe']=trim_observe
exec(compile(generated,'verified-source-extracted-ndl-roi-trim','exec'),namespace)
run=namespace['_run_ocr_on_image_array']
inputs=json.loads((ROOT/'native-inputs.json').read_text())['images'];rule_rows={v['id']:v for v in [json.loads(l) for l in (HERE/'physical-rules.jsonl').read_text().splitlines()]}
m=ROOT/'ndl-source/src/model';c=ROOT/'ndl-source/src/config'
a=types.SimpleNamespace(det_weights=str(m/'deim-s-1024x1024.onnx'),det_classes=str(c/'ndl.yaml'),det_score_threshold=.2,det_conf_threshold=.25,det_iou_threshold=.2,device='cpu',rec_classes=str(c/'NDLmoji.yaml'),enable_tcy=False)
emit({'type':'runtime','recipe':'NDL-white-only-outer-trim-v1','sourceRevision':'636d1cfeb1331f89f4048f416e49e23a09a714b5','onnxruntime':onnxruntime.__version__,'sourceFunctionSHA256':hashlib.sha256(original.encode()).hexdigest(),'generatedFunctionSHA256':hashlib.sha256(generated.encode()).hexdigest(),'recognitionSettingsUnchanged':True,'predictionAlteredByObserver':False,'tokenConfidenceScope':'stable softmax of actual native Add logits, uncalibrated correctness; no threshold or alternate selection','nativeThresholdChange':False})
det=ocr.get_detector(a);detect=det.detect

def observed_detect(image):
 value=detect(image);emit({'type':'detections','id':current['id'],'detections':value});return value
det.detect=observed_detect
recs=[]
for name in ['parseq-ndl-24x256-30-tiny-189epoch-tegaki3-r8data-202604.onnx','parseq-ndl-24x384-50-tiny-300epoch-tegaki3-r8data-202604.onnx','parseq-ndl-24x768-100-tiny-153epoch-tegaki3-r8data-202604.onnx']:recs.append(ocr.get_recognizer(a,str(m/name)))
emit({'type':'actual-model-contracts','detectorInputs':[{'name':v.name,'shape':v.shape,'type':v.type} for v in det.session.get_inputs()],'detectorOutputs':[{'name':v.name,'shape':v.shape,'type':v.type} for v in det.session.get_outputs()],'recognizers':[{'input':[{'name':v.name,'shape':v.shape,'type':v.type} for v in r.session.get_inputs()],'output':[{'name':v.name,'shape':v.shape,'type':v.type} for v in r.session.get_outputs()]} for r in recs]})
class SessionObserver:
 def __init__(self,session,index,chars):self.session=session;self.index=index;self.chars=chars
 def __getattr__(self,name):return getattr(self.session,name)
 def run(self,*args,**kwargs):
  values=self.session.run(*args,**kwargs);v=values[0]
  stats=describe(v,self.chars)
  emit({'type':'token-logit-observation','id':current['id'],'cascadeModelIndex':self.index,'cropContext':getattr(local,'crop_context',None),**stats})
  return values
for index,rec in enumerate(recs):
 rec.session=SessionObserver(rec.session,index,rec.charlist);method=rec.read
 def observed_read(image,method=method,index=index):
  local.crop_context={'sourceViewBox':bbox_view(image),'shape':list(image.shape),'characterBoxes':None}
  text=method(image);emit({'type':'recognition','id':current['id'],'cascadeModelIndex':index,'cropContext':local.crop_context,'text':text,'characterBoxes':None});return text
 rec.read=observed_read
for item in inputs:
 current['id']=item['id'];path=Path(item['pngPath']);data=path.read_bytes()
 if hashlib.sha256(data).hexdigest()!=item['pngSHA256']:raise RuntimeError('Frozen PNG changed')
 image=np.array(Image.open(path).convert('RGB'));current['image']=image
 row=rule_rows[item['id']];rb=Path(row['maskPath']).read_bytes()
 if hashlib.sha256(rb).hexdigest()!=row['sha256']:raise RuntimeError('Physical rules changed')
 mask=np.frombuffer(rb,np.uint8).reshape(image.shape[:2]).astype(bool);current['proof']=OriginalPixelProof(image,mask);start=time.monotonic()
 try:
  result=run(det,*recs,path.name,image,str(HERE/'owned-output'),save_viz=False)
  ownership=current['proof'].ownership()
  emit({'type':'result','id':item['id'],'returned':True,'milliseconds':(time.monotonic()-start)*1000,'result':result,'ownership':ownership,'sourceAdoptionEligible':ownership['allRetainedSourcesAdmissible'],'fullFormal':'unassessed','allRecognitionForDiagnosticsOnly':True})
 except Exception as error:emit({'type':'result','id':item['id'],'returned':False,'errorType':type(error).__name__,'error':str(error)[:2048],'milliseconds':(time.monotonic()-start)*1000,'sourceAdoptionEligible':False,'partialOwnership':current['proof'].ownership()})
