"""Uniformwarm jpn/OEM1/PSM7 ALLoriginalpixelrows. No oracle/detector/bestselection."""
from pathlib import Path
import json,time,hashlib
import numpy as np
from PIL import Image
from tess_api import Tess,LIB
from native_boxes import parse_words,parse_symbols
P=Path(__file__).resolve().parent;D=P.parent;R=Path('/workspace/recovery-research/windows-nonpaddle-ocr-20261004')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def emit(x):print(json.dumps(x,ensure_ascii=False,allow_nan=False),flush=True)
item=json.loads((D/'native-inputs.json').read_text())['images'][0];path=Path(item['pngPath']);assert sha(path)==item['pngSHA256'];image=np.array(Image.open(path).convert('RGB'));plan=json.loads((D/'row-crop-candidate/rows-before-native.json').read_text());assert plan['originalPNG']==item['pngSHA256'] and plan['completeOriginalPixelPlanningOwnership'] and len(plan['rows'])<=512
for row in plan['rows']:
 x,y,ex,ey=row['box'];im=np.ascontiguousarray(image[y:ey,x:ex]);assert list(im.shape)==row['shape'] and hashlib.sha256(im.tobytes()).hexdigest()==row['originalRGBSHA256']
api=Tess(R/'tess-models');words=[];allchars=[];errors=[];empty=[];guards=[];nativeReturned=0;serialized=0
emit({'type':'runtime','engine':'Tesseractofficial5.5CAPI','actualVersion':api.version,'librarySHA256':sha(LIB),'language':'jpn','OEM':1,'PSM':7,'models':['existing tessdata_best jpn.traineddata'],'warmModel':True,'freshHistory':'Clear+ClearAdaptiveClassifier before each originalrow; Init once, no learnedpreviousrowcontext','rendererAccessors':['GetUTF8Text','GetTsvText','GetHOCRText hocr_char_boxes=1','GetBoxText'],'rendererScope':'cachedsameRecognize output, no additionalnativeOCR','transcriptionConfidenceScope':'nativeword0..100+rawHOCRcharx_conf, uncalibratedagainstother engines/notproductionthreshold','nativeInputs':'original170RGBrows only','primarySourceWire':'nativeTSVwordboxes exactintegerorigintranslation, actualBOXsymbols diagnostic only','plannedRows':len(plan['rows']),'originalPNG':item['pngSHA256'],'ROIListSHA256':sha(D/'row-crop-candidate/rows-before-native.json')})
try:
 for row in plan['rows']:
  x,y,ex,ey=row['box'];start=time.monotonic()
  try:
   outputs=api.read(image[y:ey,x:ex]);nativeReturned+=1;emit({'type':'native-row-output','rowID':row['id'],'nativeRecognizeReturned':True,'outputs':outputs})
   if any(len(v.encode('utf-8'))>64*1024 for v in outputs.values()):raise ValueError('Nativeoutput64KiBperrow diagnosticcap')
   nativewords=parse_words(outputs['tsv'],row);symbols=parse_symbols(outputs['box'],row)
   if not nativewords:empty.append(row['id'])
   for word in nativewords:
    a,b,c,e=word['box'];words.append({'id':len(words),'text':word['text'],'boundingBox':[[a,b],[c,b],[c,e],[a,e]],'originalPixelRowID':row['id'],'confidence':None,'nativeWordConfidence':word['nativeWordConfidence'],'confidenceScope':word['confidenceScope'],'geometryScope':'ActualnativeTSVwordbbox, exactintegeroriginconversion; no character subdivision','nativeLocalBox':word['localBox']})
    if not word['insideOriginalRow']:guards.append({'rowID':row['id'],'nativeWord':word,'reason':'Actualnativewordbox outside originalROI, NOT clipped'})
   allchars.extend({'rowID':row['id'],**char} for char in symbols);serialized+=1;emit({'type':'row-result','rowID':row['id'],'nativeRecognizeReturned':True,'serializationCompleted':True,'returned':True,'milliseconds':(time.monotonic()-start)*1000,'nativeOutputs':outputs,'nativeWords':nativewords,'nativeSymbols':symbols,'originalInputRGBSHA256':row['originalRGBSHA256']})
  except Exception as e:
   # A native success followed by renderer/adapter failure is NOT no native output.
   if api.lastRecognizeReturned and not ('outputs' in locals()):nativeReturned+=1
   error={'rowID':row['id'],'errorType':type(e).__name__,'error':str(e)[:1024],'nativeRecognizeReturned':api.lastRecognizeReturned};errors.append(error);emit({'type':'row-result','rowID':row['id'],'returned':False,'serializationCompleted':False,'error':error,'partialNativeOutputs':api.lastOutputs})
  finally:
   if 'outputs' in locals():del outputs
finally:api.close()
emit({'type':'result','id':item['id'],'returned':not errors,'plannedRows':len(plan['rows']),'nativeRecognizeReturnedRows':nativeReturned,'serializationCompletedRows':serialized,'runtimeErrors':errors,'emptyTranscriptionsOnPrintedInk':empty,'sourceGuardFailures':guards,'result':{'json_lines':words},'nativeSymbols':allchars,'sourceAdoptionEligible':not errors and not empty and not guards,'scope':'Actualwordgeometry/outputavailability only; confidencecalibration/actualhostsemantic/formalgold independent. No productionactivation/automaticadoption or per-rowbestselection.','fullFormal':'unassessed'})
