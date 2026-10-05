from pathlib import Path
import json,hashlib,numpy as np
from PIL import Image
P=Path(__file__).resolve().parent;G=P.parent;D=G.parent
rows=[json.loads(l) for l in (G/'native-raw.log').read_text().splitlines() if l.startswith('{')];regions={r['id']:r for r in json.loads((G/'regions-before-native.json').read_text())['regions']};item=json.loads((D/'native-inputs.json').read_text())['images'][0];path=Path(item['pngPath']);assert hashlib.sha256(path.read_bytes()).hexdigest()==item['pngSHA256'];image=np.array(Image.open(path).convert('RGB'));groups={}
for r in rows:
 if r.get('type')!='recognition' or not r['cropContext']['upstreamAspectRotationPredicate']:continue
 region=regions[r['regionID']]
 if region['kind']!='closed-physical-cell':continue
 x,y,ex,ey=region['box'];a,b,c,e=r['cropContext']['localActualRecognitionROI'];im=image[y+b:y+e,x+a:x+c];spec={'shape':list(im.shape),'rgbSHA256':hashlib.sha256(np.ascontiguousarray(im).tobytes()).hexdigest()};key=json.dumps([r['cascadeModelIndex'],spec],sort_keys=True)
 v=groups.setdefault(key,{'id':len(groups),'actualModelIndex':r['cascadeModelIndex'],'input':spec,'occurrences':[]});v['occurrences'].append({'regionID':r['regionID'],'originalRegionBox':region['box'],'originalActualRecognitionROI':[a,b,c,e],'observedOldText':r['text']})
if len(groups)!=11:raise ValueError('Expected immutable11 unique original crop controls')
(P/'selected-inputs.json').write_text(json.dumps({'scope':'ALL41 observed true-predicate actual calls; byte-identical original RGB/shape/model-index dedup ONLY for11 diagnostic calls, not source/evidence ownership. No role/error/expected text selection.','originalPNG':item['pngSHA256'],'priorRawSHA256':hashlib.sha256((G/'native-raw.log').read_bytes()).hexdigest(),'controls':list(groups.values())},ensure_ascii=False,indent=2)+'\n')
