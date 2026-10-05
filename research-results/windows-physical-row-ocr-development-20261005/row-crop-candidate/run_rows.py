"""One fixed recognizer for ALL actual original pixel rows; no detector/layout/gold."""
from pathlib import Path
import sys,json,time,types,hashlib
import numpy as np,cv2
from PIL import Image
from no_rotation import build
from token_stats import describe
P=Path(__file__).resolve().parent;D=P.parent;R=Path('/workspace/recovery-research/windows-nonpaddle-ocr-20261004');sys.path.insert(0,str(R/'ndl-source/src'))
import ocr,onnxruntime
from parseq import PARSEQ
onnxruntime.disable_telemetry_events()
arm=sys.argv[1]
names={'30':'parseq-ndl-24x256-30-tiny-189epoch-tegaki3-r8data-202604.onnx','100':'parseq-ndl-24x768-100-tiny-153epoch-tegaki3-r8data-202604.onnx'}
if arm not in names:raise SystemExit('Frozen whole-document model arm required')
item=json.loads((D/'native-inputs.json').read_text())['images'][0];path=Path(item['pngPath']);assert hashlib.sha256(path.read_bytes()).hexdigest()==item['pngSHA256'];image=np.array(Image.open(path).convert('RGB'));plan=json.loads((P/'rows-before-native.json').read_text());assert plan['originalPNG']==item['pngSHA256'] and plan['completeOriginalPixelPlanningOwnership'] and len(plan['rows'])<=512
for row in plan['rows']:
 x,y,ex,ey=row['box'];view=image[y:ey,x:ex];assert list(view.shape)==row['shape'] and hashlib.sha256(np.ascontiguousarray(view).tobytes()).hexdigest()==row['originalRGBSHA256']
args=types.SimpleNamespace(rec_classes=str(R/'ndl-source/src/config/NDLmoji.yaml'),device='cpu',enable_tcy=False);rec=ocr.get_recognizer(args,str(R/'ndl-source/src/model'/names[arm]));method,proof=build(PARSEQ,np,cv2);rec.preprocess=types.MethodType(method,rec)
def emit(r):print(json.dumps(r,ensure_ascii=False,allow_nan=False),flush=True)
current=None;calls=0
class Observe:
 def __init__(self,session):self.base=session
 def __getattr__(self,k):return getattr(self.base,k)
 def run(self,*args,**kwargs):
  global calls
  outputs=self.base.run(*args,**kwargs);calls+=1;emit({'type':'token-logit-observation','rowID':current['id'],**describe(outputs[0],rec.charlist)});return outputs
rec.session=Observe(rec.session)
emit({'type':'runtime','recipe':'original-physical-pixel-rows-uniform-recognizer-v1','arm':arm,'onnxruntime':onnxruntime.__version__,'models':[names[arm]],'detectorCalls':0,'plannedRows':len(plan['rows']),'ROIListSHA256':hashlib.sha256((P/'rows-before-native.json').read_bytes()).hexdigest(),'originalPNG':item['pngSHA256'],'preprocessing':proof,'pageUprightOrientation':'Original generated raster orientation0; bypass automatic CCW in both arms. No orientation/recognizer selector per answer.','geometryScope':'Original pixel-derived support ROI, not model character/word bbox','tokenSoftmaxScope':'uncalibrated diagnostic only, no threshold'})
units=[];errors=[];empty=[];returned=0
for row in plan['rows']:
 current=row;x,y,ex,ey=row['box'];view=image[y:ey,x:ex];start=time.monotonic()
 try:
  text=rec.read(view);returned+=1
  if text=='':empty.append(row['id'])
  unit={'id':row['id'],'text':text,'boundingBox':[[x,y],[ex,y],[ex,ey],[x,ey]],'parentRegionID':row['parentRegionID'],'originalPixelRowID':row['id'],'confidence':None,'transcriptionConfidence':None,'geometryScope':'Exact original pixel support rectangle for occupied y-run; actual model returns string only; no native character box claim','sourceOrderScope':'Physical y/x ROI order initialized before output, no generated-answer sorting/repair'};units.append(unit);emit({'type':'row-result','rowID':row['id'],'returned':True,'milliseconds':(time.monotonic()-start)*1000,'actualInputRGBSHA256':row['originalRGBSHA256'],'unit':unit})
 except Exception as e:
  error={'rowID':row['id'],'errorType':type(e).__name__,'error':str(e)[:1024]};errors.append(error);emit({'type':'row-result','rowID':row['id'],'returned':False,'error':error})
emit({'type':'result','id':item['id'],'arm':arm,'returned':not errors,'plannedRows':len(plan['rows']),'returnedRows':returned,'actualPARSeqCalls':calls,'detectorCalls':0,'runtimeErrors':errors,'emptyTranscriptionsOnPrintedInk':empty,'result':{'json_lines':units},'sourceAdoptionEligible':not errors and not empty,'scope':'Pixel row planning ownership/geometry and nonempty transcription availability only. Word correctness/confidence calibration/current host semantic/formal proof independently assessed. No automatic activation/production qualification.','noRoleStateGeneration':True,'fullFormal':'unassessed'})
