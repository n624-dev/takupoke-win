"""Observation-only original-pixel white trim and physical ownership proof.
No text, gold coordinates, model output selection, or character boxes are used.
"""
import hashlib
import numpy as np
import cv2

class OriginalPixelProof:
 def __init__(self,rgb,rule_mask):
  if rgb.dtype!=np.uint8 or rgb.ndim!=3 or rgb.shape[2]!=3 or rgb.shape[0]>4096 or rgb.shape[1]>4096:raise ValueError('RGB raster shape/type invalid')
  if rule_mask.shape!=rgb.shape[:2] or rule_mask.dtype!=np.bool_:raise ValueError('Rule mask shape/type invalid')
  self.image=rgb;self.nonwhite=np.any(rgb!=255,axis=2);self.rules=rule_mask
  _,self.components=cv2.connectedComponents((~rule_mask).astype(np.uint8),connectivity=4)
  self.outside=set(np.unique(np.concatenate((self.components[0],self.components[-1],self.components[:,0],self.components[:,-1]))).tolist())
  self.regions=[]
 def trim(self,raw_box,native_index):
  # No clipping: retain outside-box rejection rather than fabricate source coordinates.
  b=np.asarray(raw_box,dtype=np.float64)
  if b.shape!=(4,) or not np.isfinite(b).all() or not np.equal(b,np.floor(b)).all():raise ValueError('Original parent ROI must be finite integer pixels')
  x,y,ex,ey=map(int,b);h,w=self.image.shape[:2]
  if not(0<=x<ex<=w and 0<=y<ey<=h):raise ValueError('Original parent outside original raster')
  ink=self.nonwhite[y:ey,x:ex];ys,xs=np.where(ink)
  if not len(xs):
   record={'nativeIndex':native_index,'originalBox':[x,y,ex,ey],'trimmedBox':None,'inkPixels':0,'allOriginalInkPreserved':True,'sourceAdmissible':False,'reason':'original parent has no nonwhite pixels','whiteTrimApplied':False};self.regions.append(record)
   return self.image[y:ey,x:ex],record
  tx,ty,tex,tey=x+int(xs.min()),y+int(ys.min()),x+int(xs.max())+1,y+int(ys.max())+1
  trimmed=self.nonwhite[ty:tey,tx:tex];old_count=int(ink.sum());new_count=int(trimmed.sum())
  if old_count!=new_count:raise AssertionError('Trim removed original nonwhite pixels')
  rule_ink=bool(np.any(self.rules[y:ey,x:ex]&ink))
  cells=np.unique(self.components[y:ey,x:ex][ink & ~self.rules[y:ey,x:ex]])
  bounded_cell=len(cells)==1 and int(cells[0]) not in self.outside and int(cells[0])!=0
  # Hash exact nonwhite support in original coordinate order, not generated string positions.
  support=np.stack((ys+y,xs+x),axis=1).astype('<i4')
  record={'nativeIndex':native_index,'originalBox':[x,y,ex,ey],'trimmedBox':[tx,ty,tex,tey],'inkPixels':old_count,'trimmedInkPixels':new_count,'supportSHA256':hashlib.sha256(support.tobytes()).hexdigest(),'allOriginalInkPreserved':True,'whiteOnlyOuterTrim':True,'whiteTrimApplied':[x,y,ex,ey]!=[tx,ty,tex,tey],'physicalRuleInkPresent':rule_ink,'physicalInteriorComponents':cells.tolist(),'singleBoundedPhysicalCell':bool(bounded_cell),'sourceAdmissible':bool(bounded_cell and not rule_ink),'reason':None if bounded_cell and not rule_ink else 'rule or cross-cell/outside physical source ownership is unproved'}
  self.regions.append(record)
  return self.image[ty:tey,tx:tex],record
 def ownership(self):
  pairs=[]
  for i,a in enumerate(self.regions):
   if a.get('trimmedBox') is None:continue
   for j,b in enumerate(self.regions[i+1:],i+1):
    if b.get('trimmedBox') is None:continue
    aa=a['trimmedBox'];bb=b['trimmedBox'];x=max(aa[0],bb[0]);y=max(aa[1],bb[1]);ex=min(aa[2],bb[2]);ey=min(aa[3],bb[3])
    if x<ex and y<ey:
     shared=int(self.nonwhite[y:ey,x:ex].sum())
     if shared:pairs.append({'firstNativeIndex':a['nativeIndex'],'secondNativeIndex':b['nativeIndex'],'sharedOriginalNonwhitePixels':shared,'conflictingOwners':True})
  conflict_ids={p[k] for p in pairs for k in ['firstNativeIndex','secondNativeIndex']}
  for a in self.regions:
   a['conflictingOwner']=a['nativeIndex'] in conflict_ids
   a['sourceAdmissible']=bool(a['sourceAdmissible'] and not a['conflictingOwner'])
  return {'regions':self.regions,'sharedInkConflicts':pairs,'allRetainedSourcesAdmissible':bool(self.regions) and all(r['sourceAdmissible'] for r in self.regions),'noTextDedupOrGoldUsed':True,'scope':'Observation only; invalid sources rejected for adoption, raw recognition may be retained for cause analysis. No complete global ink/field proof.'}
