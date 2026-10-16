"""Read-only post-generation normalized tensor collision audit; no inference/labels for training."""
import hashlib,json,sys
from pathlib import Path
from PIL import Image
from data import ROOT,RESULTS,save,tensor

def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def main():
 current={};manifests=[]
 for split in ['train','development','fresh']:
  path=ROOT/split/'manifest.json'
  if not path.exists():continue
  m=json.loads(path.read_text(encoding='utf-8'));manifests.append({'split':split,'sha256':sha(path)})
  for r in m['rows']:
   if r['kind']!='blank':current.setdefault(r['inputSha256'],[]).append({'id':r['id'],'split':split,'kind':r['kind']})
 receipt=json.loads((ROOT/'exclusion-receipt.json').read_text(encoding='utf-8'));scanned=0;missing=0;collisions=[];checked=[]
 for item in receipt['files']:
  p=Path(item['path'])
  if p.name!='manifest.json' or not p.exists():continue
  m=json.loads(p.read_text(encoding='utf-8'))
  if not isinstance(m,dict):continue
  n=0
  for r in m.get('rows',[]):
   if not isinstance(r,dict):continue
   filename=r.get('file')
   if not isinstance(filename,str):continue
   image_path=p.parent/filename
   if not image_path.exists() or image_path.suffix.lower() not in ['.png','.jpg','.jpeg']:missing+=1;continue
   try:a,_=tensor(Image.open(image_path))
   except (ValueError,OSError):continue
   scanned+=1;n+=1;h=hashlib.sha256(a.tobytes()).hexdigest()
   if h in current:collisions.append({'priorManifest':str(p),'priorId':r.get('id'),'current':current[h],'inputSha256':h})
  checked.append({'path':str(p),'scanned':n})
 duplicate_nonblank=[{'hash':h,'rows':rows} for h,rows in current.items() if len(rows)>1]
 result={'stage':'Read-only supplemental audit after initial freeze; no model execution or selection','manifests':manifests,'priorNormalizedPngRows':scanned,'priorMissingImageRows':missing,'priorManifests':checked,'nonblankPriorCollisions':collisions,'nonblankCurrentDuplicates':duplicate_nonblank,'whiteControlsExcludedFromNovelty':True,'passed':not collisions and not duplicate_nonblank,'sourceSha256':sha(__file__)}
 out=RESULTS/('corpus-overlap-fresh-audit.json' if any(x['split']=='fresh' for x in manifests) else 'corpus-overlap-train-dev-audit.json');save(out,result);print(json.dumps({k:v for k,v in result.items() if k not in ['priorManifests']}));assert result['passed']
if __name__=='__main__':main()
