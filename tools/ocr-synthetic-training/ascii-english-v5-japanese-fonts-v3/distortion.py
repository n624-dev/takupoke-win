"""Once-only post-selection geometric distortion monitoring; no optimization or selection."""
import hashlib,importlib.util,io,json,math,random
from pathlib import Path
import numpy as np,torch,yaml
from PIL import Image
from safetensors.torch import load_file
from data import ROOT,RESULTS,FONTS,code,render,frame,tensor,save
from adapter import Adapter
from model import ASSETS
_spec=importlib.util.spec_from_file_location('v3_probe_training',Path(__file__).with_name('train.py'));_m=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(_m)

def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));c=selection['selected'];selected_for_development=c['epoch']>0
 if selected_for_development:assert (RESULTS/'fresh-comparison.json').exists()
 else:
  assert not (ROOT/'fresh').exists();c=next(x for x in selection['candidates'] if x['epoch']==3)
 spec=json.loads(Path(__file__).with_name('distortion_protocol.json').read_text(encoding='utf-8'));out=ROOT/'distortion'
 if out.exists():raise RuntimeError('Probe cannot repeat')
 out.mkdir();save(RESULTS/'distortion-freeze.json',{'protocol':spec,'protocolSha256':_m.sha(Path(__file__).with_name('distortion_protocol.json')),'sourceSha256':_m.sha(__file__),'evaluatedCheckpointSha256':c['sha256'],'selectedForDevelopment':selected_for_development,'postSelectionMonitoringOnly':True});rng=random.Random(spec['seed']);used=set(json.loads((ROOT/'excluded-values.json').read_text(encoding='utf-8')))
 for split in ['train','development','fresh']:
  if not (ROOT/split/'manifest.json').exists():continue
  for row in json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'))['rows']:
   for key in ['renderedLiteral','inputSha256','pngSha256']:used.add(row[key])
 rows=[]
 for i in range(spec['rows']):
  name=spec['fonts'][i%4];angle=spec['anglesDegrees'][(i//4)%4]
  while True:
   value=code(rng,(i//16)%2==0)
   if value in used:continue
   original,geometry=render(value,name,rng.randrange(2**31));shear=math.tan(math.radians(angle));padx=4+max(0,-shear*original.height);width=original.width+math.ceil(abs(shear)*original.height)+8
   transformed=original.transform((width,original.height+4),Image.Transform.AFFINE,(1,-shear,-padx+2*shear,0,1,-2),resample=Image.Resampling.BICUBIC,fillcolor='white');image,source=frame(transformed)
   try:x,valid=tensor(image)
   except ValueError:continue
   buf=io.BytesIO();image.save(buf,format='PNG');pnghash=hashlib.sha256(buf.getvalue()).hexdigest();tensorhash=hashlib.sha256(x.tobytes()).hexdigest()
   if pnghash in used or tensorhash in used:continue
   used.update([value,pnghash,tensorhash]);break
  path=out/f'{i:05d}.png';path.write_bytes(buf.getvalue());source.update({'shearDegrees':angle,'originalBeforeTransformSize':list(original.size),'transformedSize':list(transformed.size),'sourceSupportRetainedByExpandedCanvas':True,'notProductOriginalPixelIdentity':True})
  rows.append({'id':f'distortion-{i:05d}','kind':'lookalike' if (i//16)%2==0 else 'code','font':name,'text':value,'renderedLiteral':value,'reject':False,'ctcEligible':True,'selectionPositive':True,'file':path.name,'pngSha256':pnghash,'inputSha256':tensorhash,'inputWidth':x.shape[-1],'validWidth':valid,'source':source,'geometry':geometry,'width':image.width,'height':image.height})
 manifest={'split':'distortion','fonts':{name:_m.sha(FONTS[name]) for name in spec['fonts']},'rows':rows};save(out/'manifest.json',manifest);torch.set_num_threads(2);torch.set_num_interop_threads(1);baseline=Adapter().eval();baseline.load_state_dict(load_file(selection['activeBaseline']['path']),strict=True);cache=_m.cache(manifest,baseline.official,ROOT/'distortion-cache',json.loads(Path(__file__).with_name('protocol.json').read_text(encoding='utf-8'))['training']);dictionary=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' ']
 bm,_=_m.evaluate('distortion-active',baseline,cache,ROOT/'distortion-cache',manifest,dictionary,'trained');candidate=Adapter().eval();candidate.load_state_dict(load_file(c['path']),strict=True);cm,_=_m.evaluate('distortion-candidate',candidate,cache,ROOT/'distortion-cache',manifest,dictionary,'trained');save(RESULTS/'distortion-comparison.json',{'active':bm,'candidate':cm,'evaluatedCheckpointSha256':c['sha256'],'selectedForDevelopment':selected_for_development,'noFurtherSelectionOrOptimization':True,'scope':'Consumed training fonts, new literals/mildshear only; not independent fresh credit or document qualification'})
if __name__=='__main__':main()
