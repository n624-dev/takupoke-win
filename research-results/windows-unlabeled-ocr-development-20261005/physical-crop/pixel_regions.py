"""Original raster/rules only. No text, role, drawing, or oracle input.
Closed measured-cell interiors and all residual ink-row whitespace blocks.
Only physically proved rule strips are removed; all non-rule ink has one owner.
"""
import numpy as np,math,hashlib
MAX_REGIONS=128

def runs(values):
 points=np.flatnonzero(values);out=[]
 for p in points:
  p=int(p)
  if not out or p!=out[-1][1]:out.append([p,p+1])
  else:out[-1][1]=p+1
 return out

def plan(rgb,mask,measured):
 if rgb.dtype!=np.uint8 or rgb.ndim!=3 or rgb.shape[2]!=3 or max(rgb.shape[:2])>4096:raise ValueError('Original RGB shape invalid')
 h,w=rgb.shape[:2]
 if mask.shape!=(h,w) or mask.dtype!=np.bool_:raise ValueError('Actual rule mask shape invalid')
 if len(measured)>128:raise ValueError('Physical region capacity exceeded')
 ink=np.any(rgb!=255,axis=2)&~mask;owners=np.zeros((h,w),np.uint8);regions=[]
 def add(box,kind,physical=None):
  if len(regions)>=MAX_REGIONS:raise ValueError('Region capacity exceeded')
  x,y,ex,ey=map(int,box)
  if not(0<=x<ex<=w and 0<=y<ey<=h):raise ValueError('Region outside raster')
  if mask[y:ey,x:ex].any():raise ValueError('Proposed ROI contains physical rule; never erase/mutate it: '+repr((kind,box)))
  local=ink[y:ey,x:ex]
  if np.any(owners[y:ey,x:ex][local]):raise ValueError('Original non-rule ink has multiple region owners')
  owners[y:ey,x:ex][local]=1
  if local.any():
   regions.append({'id':len(regions),'kind':kind,'box':[x,y,ex,ey],'physicalBox':physical,'inkPixels':int(local.sum()),'originalRGBSHA256':hashlib.sha256(np.ascontiguousarray(rgb[y:ey,x:ex]).tobytes()).hexdigest(),'coordinateTransform':'global=local+integerCropOrigin; no character subdivision or posthoc clipping'})
 for b in sorted(measured,key=lambda b:(b['Top'],b['Left'])):
  if not b.get('closedRails'):raise ValueError('Physical rectangle not independently closed')
  raw=[b[k] for k in ['Left','Top','Right','Bottom']]
  if not np.isfinite(raw).all():raise ValueError('Physical rectangle not finite')
  x,y=math.ceil(raw[0]),math.ceil(raw[1]);ex,ey=math.floor(raw[2])+1,math.floor(raw[3])+1
  if not(0<=x<ex<=w and 0<=y<ey<=h):raise ValueError('Physical rectangle outside raster')
  old_nonrule=int(ink[y:ey,x:ex].sum())
  while x<ex and y<ey:
   changed=False
   if mask[y:ey,x].all():x+=1;changed=True
   if x<ex and mask[y:ey,ex-1].all():ex-=1;changed=True
   if x<ex and y<ey and mask[y,x:ex].all():y+=1;changed=True
   if x<ex and y<ey and mask[ey-1,x:ex].all():ey-=1;changed=True
   if not changed:break
  if x>=ex or y>=ey:raise ValueError('Physical cell has no rectangular interior')
  if int(ink[y:ey,x:ex].sum())!=old_nonrule:raise AssertionError('Removed non-rule source ink')
  add([x,y,ex,ey],'closed-physical-cell',raw)
 # All remaining non-rule pixels must be retained, regardless of their text/role.
 remainder=ink&(owners==0);bands=runs(remainder.any(axis=1))
 if len(bands)>128:raise ValueError('Outside pixel-row capacity exceeded')
 for y,ey in bands:
  columns=runs(remainder[y:ey].any(axis=0));height=ey-y
  groups=[]
  for a,b in columns:
   if groups and a-groups[-1][1]<max(2,height):groups[-1][1]=b
   else:groups.append([a,b])
  if len(groups)>128:raise ValueError('Outside pixel-column capacity exceeded')
  for x,ex in groups:
   # Context is exactly one observed ink-row height, bounded by raster.
   # Reject context intersecting a physical rule or another retained ink owner.
   box=[max(0,x-height),max(0,y-height),min(w,ex+height),min(h,ey+height)]
   add(box,'outside-pixel-row-whitespace-block')
 if np.any(ink&(owners!=1)):raise ValueError('Original non-rule ink unowned')
 return {'regions':regions,'nonRuleInkPixels':int(ink.sum()),'uniquelyOwnedNonRuleInkPixels':int((ink&(owners==1)).sum()),'completeOriginalNonRuleInkOwnership':True,'rulePixelsExcluded':int((np.any(rgb!=255,axis=2)&mask).sum()),'scope':'Planning covers original non-rule pixels exactly once, NOT native OCR completeness/correctness or formal recovery; glyph touching/crossing unknown rails is not repaired.'}
