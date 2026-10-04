import sys,json,time,hashlib,types,threading
from pathlib import Path
import numpy as np
from PIL import Image
root=Path(sys.argv[1]).resolve();sys.path.insert(0,str(root/'ndl-source/src'))
import ocr,onnxruntime,cv2,PIL
onnxruntime.disable_telemetry_events()
inputs=json.loads((root/'native-inputs.json').read_text())['images']
m=root/'ndl-source/src/model';c=root/'ndl-source/src/config'
a=types.SimpleNamespace(det_weights=str(m/'deim-s-1024x1024.onnx'),det_classes=str(c/'ndl.yaml'),det_score_threshold=.2,det_conf_threshold=.25,det_iou_threshold=.2,device='cpu',rec_classes=str(c/'NDLmoji.yaml'),enable_tcy=False)
def native_scalar(v):
 if isinstance(v,np.ndarray):return v.tolist()
 if isinstance(v,np.generic):return v.item()
 raise TypeError(type(v).__name__)
emit_lock=threading.Lock()
def emit(v):
 with emit_lock:print(json.dumps(v,ensure_ascii=False,allow_nan=False,default=native_scalar),flush=True)
emit({'type':'runtime','engine':'ndlocr-lite','revision':'636d1cfeb1331f89f4048f416e49e23a09a714b5','onnxruntime':onnxruntime.__version__,'opencv':cv2.__version__,'pillow':PIL.__version__,'numpy':np.__version__,'nativeBinaryPins':[{'name':v.name,'sha256':hashlib.sha256(v.read_bytes()).hexdigest()} for v in Path(onnxruntime.__file__).parent.rglob('*.so')],'recognitionConfidenceAvailable':False,'characterAlignmentAvailable':False,'lineConfidenceScope':'detector, not transcription','defaultCascade':True})
det=ocr.get_detector(a);recs=[]
for name in ['parseq-ndl-24x256-30-tiny-189epoch-tegaki3-r8data-202604.onnx','parseq-ndl-24x384-50-tiny-300epoch-tegaki3-r8data-202604.onnx','parseq-ndl-24x768-100-tiny-153epoch-tegaki3-r8data-202604.onnx']:
 recs.append(ocr.get_recognizer(a,str(m/name)))
current={'id':None};original=det.detect
# Observe exactly the already-required detector invocation; return its unchanged value.
def detect(image):
 value=original(image);emit({'type':'detections','id':current['id'],'detections':value});return value
det.detect=detect
for ri,rec in enumerate(recs):
 method=rec.read
 def read(image,method=method,ri=ri):
  value=method(image);emit({'type':'recognition','id':current['id'],'cascadeModelIndex':ri,'cropShape':list(image.shape),'text':value,'confidence':None,'characterBoxes':None});return value
 rec.read=read
for item in inputs:
 current['id']=item['id'];p=Path(item['pngPath']);b=p.read_bytes()
 if hashlib.sha256(b).hexdigest()!=item['pngSHA256']:raise RuntimeError('Frozen PNG mismatch')
 start=time.monotonic()
 try:
  image=np.array(Image.open(p).convert('RGB'))
  result=ocr._run_ocr_on_image_array(det,*recs,p.name,image,str(root/'owned-output'),save_viz=False)
  emit({'type':'result','id':item['id'],'returned':True,'milliseconds':(time.monotonic()-start)*1000,'result':result})
 except Exception as error:emit({'type':'result','id':item['id'],'returned':False,'errorType':type(error).__name__,'error':str(error)[:2048],'milliseconds':(time.monotonic()-start)*1000})
