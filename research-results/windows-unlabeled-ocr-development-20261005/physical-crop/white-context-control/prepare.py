from pathlib import Path
import json,hashlib,numpy as np
from PIL import Image
from white_context import extend
P=Path(__file__).resolve().parent;G=P.parent;D=G.parent
raw=G/'native-raw.log';rows=[json.loads(l) for l in raw.read_text().splitlines() if l.startswith('{')];plan=json.loads((G/'regions-before-native.json').read_text());regions={r['id']:r for r in plan['regions']};inputs=json.loads((D/'native-inputs.json').read_text());p=Path(inputs['images'][0]['pngPath']);assert hashlib.sha256(p.read_bytes()).hexdigest()==inputs['images'][0]['pngSHA256'];image=np.array(Image.open(p).convert('RGB'));groups={}
for r in rows:
 if r.get('type')!='recognition' or not r['cropContext']['upstreamAspectRotationPredicate']:continue
 region=regions[r['regionID']]
 if region['kind']!='closed-physical-cell':continue
 x,y,ex,ey=region['box'];rv=image[y:ey,x:ex];b=r['cropContext']['localActualRecognitionROI'];a,c,e,f=b;old=rv[c:f,a:e];new,nb=extend(rv,b)
 def spec(im):return {'shape':list(im.shape),'rgbSHA256':hashlib.sha256(np.ascontiguousarray(im).tobytes()).hexdigest()}
 before,after=spec(old),spec(new);key=json.dumps([r['cascadeModelIndex'],before,after],sort_keys=True)
 value=groups.setdefault(key,{'id':len(groups),'actualModelIndex':r['cascadeModelIndex'],'oldInput':before,'contextInput':after,'oldUpstreamCCWPredicate':bool(old.shape[0]>old.shape[1]*.8),'newUpstreamCCWPredicate':bool(new.shape[0]>new.shape[1]*.8),'occurrences':[]})
 value['occurrences'].append({'regionID':r['regionID'],'originalRegionBox':region['box'],'originalActualRecognitionROI':b,'whiteContextROI':nb,'observedOldText':r['text']})
if len(groups)>16:raise ValueError('Frozen causal control capacity16 exceeded')
v={'native0':True,'selection':'ALL actual recognizer calls whose observed upstream height > width*.8 predicate is true, inside an already measured closed physical crop; no expected text or error outcome selection. Byte-identical OLD+NEW RGB/shape/model-index inputs share one diagnostic call only, mapped to every original occurrence; no evidence dedup/adoption.','originalPNG':inputs['images'][0]['pngSHA256'],'priorRawSHA256':hashlib.sha256(raw.read_bytes()).hexdigest(),'controls':list(groups.values())};(P/'selected-inputs.json').write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n');print(len(groups),'unique controls',sum(len(g['occurrences']) for g in groups.values()),'prior actual occurrences')
