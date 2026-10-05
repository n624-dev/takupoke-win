"""ALL occupied y-runs inside original measured ROIs. No OCR/text/roles/gold."""
import numpy as np,hashlib
MAX_ROWS=512

def rows(image,plan):
 if image.dtype!=np.uint8 or image.ndim!=3 or image.shape[2]!=3:raise ValueError('RGB original required')
 h,w=image.shape[:2];owner=np.zeros((h,w),bool);result=[];expected=0
 for region in plan['regions']:
  x,y,ex,ey=region['box'];view=image[y:ey,x:ex];ink=np.any(view!=255,axis=2);expected+=int(ink.sum());occupied=np.flatnonzero(ink.any(axis=1));runs=[]
  for p in occupied:
   p=int(p)
   if not runs or p!=runs[-1][1]:runs.append([p,p+1])
   else:runs[-1][1]=p+1
  for a,b in runs:
   if len(result)>=MAX_ROWS:raise ValueError('Occupied-row capacity exhausted; no silent omit')
   xs=np.flatnonzero(ink[a:b].any(axis=0));left,right=int(xs[0]),int(xs[-1])+1;box=[x+left,y+a,x+right,y+b];roi=image[box[1]:box[3],box[0]:box[2]];local=np.any(roi!=255,axis=2);previous=owner[box[1]:box[3],box[0]:box[2]]
   if np.any(previous&local):raise ValueError('Multiple row owners for original ink')
   previous[local]=True
   result.append({'parentRegionID':region['id'],'box':box,'originalRGBSHA256':hashlib.sha256(np.ascontiguousarray(roi).tobytes()).hexdigest(),'shape':list(roi.shape),'nonwhitePixels':int(local.sum()),'geometryScope':'Exact original pixel support rectangle of contiguous occupied row-run; NOT predicted character geometry or asserted semantic line/role'})
 if int(owner.sum())!=expected or expected!=plan['nonRuleInkPixels']:raise ValueError('Original nonrule ink accounting mismatch')
 # Pre-output deterministic physical reading order, never model-answer repair.
 result.sort(key=lambda r:(r['box'][1],r['box'][0]))
 for i,r in enumerate(result):r['id']=i
 return {'rows':result,'originalNonRuleInkPixels':expected,'uniquelyOwnedOriginalPixels':int(owner.sum()),'completeOriginalPixelPlanningOwnership':True,'scope':'Disconnected stroke/dot fragments remain separate ROI; no implied3rows/role binding/EMPTY or native completeness. Current Strict/Builder must validate any interpretation.'}
