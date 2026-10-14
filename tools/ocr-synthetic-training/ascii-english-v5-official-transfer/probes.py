"""Consumed source contradictions after frozen selection; no new final credit."""
import hashlib,json,time
from pathlib import Path
import numpy as np,torch,yaml
from PIL import Image
from safetensors.torch import load_file
from data import ROOT,RESULTS,save,tensor
from model import ASSETS,feature_logits
from adapter import Adapter
from image_tensor import greedy
PROBES=Path('/workspace/scratch/ascii-paddle-transfer-20261013-v1/source-proof-probes')
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def main():
 if (RESULTS/'source-proof-freeze.json').exists():raise ValueError('Postselection controls cannot repeat')
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));manifest=json.loads((PROBES/'manifest.json').read_text(encoding='utf-8'));dictionary=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' '];save(RESULTS/'source-proof-freeze.json',{'scope':'Oldconsumed64samepixelscript/missingprefixcontrols aftermodelselection only; unscored fornewquality/final/activation','selectionSha256':sha(ROOT/'selection.json'),'sourceSha256':sha(Path(__file__)),'manifestSha256':sha(PROBES/'manifest.json'),'rows':64});torch.set_num_threads(2);torch.set_num_interop_threads(1);inputs=[]
 for row in manifest['rows']:
  p=PROBES/row['file'];assert sha(p)==row['pngSha256'];x,valid=tensor(Image.open(p));inputs.append((x,valid))
 summaries=[];start=time.monotonic()
 for candidate in [selection['officialBaseline']]+selection['candidates']:
  assert sha(candidate['path'])==candidate['sha256'];model=Adapter().eval();trained=candidate['epoch']>0
  if trained:model.load_state_dict(load_file(candidate['path']),strict=True)
  raw=RESULTS/f"source-proof-{candidate['name']}-unjudged.jsonl";records=[]
  with raw.open('x',encoding='utf-8',newline='\n') as out,torch.inference_mode():
   for row,(x,valid) in zip(manifest['rows'],inputs):
    feature,logits=feature_logits(model.official,torch.from_numpy(x[None]));frames=feature.shape[1];mask=torch.from_numpy(((np.arange(frames)+.5)*x.shape[-1]/frames<valid)[None]);reject=float(model.reject((feature*mask[...,None]).sum(1)/mask.sum(1,keepdim=True).clamp_min(1)).sigmoid()[0,0]) if trained else None;pieces=greedy(logits.softmax(-1)[0].numpy(),dictionary);text=''.join(p['Text'] for p in pieces);minimum=min((p['Confidence'] for p in pieces),default=0);supported=all(p['End']*x.shape[-1]<=valid*frames for p in pieces);domain=all(32<=ord(c)<=126 for c in text);accepted=bool(text) and minimum>=.8 and supported and domain and (not trained or reject<=.05);r={'id':row['id'],'prediction':text,'pieces':pieces,'minimumConfidence':minimum,'rejectProbability':reject,'accepted':accepted,'supported':supported,'printableAscii':domain,'sourceProofRequired':True};records.append(r);out.write(json.dumps(r,ensure_ascii=False)+'\n')
  groups={}
  for row,result in zip(manifest['rows'],records):groups.setdefault(row['pair'],[]).append((row,result))
  assert len(groups)==32
  for pair in groups.values():assert len(pair)==2 and pair[0][0]['pngSha256']==pair[1][0]['pngSha256'] and pair[0][1]['prediction']==pair[1][1]['prediction'] and pair[0][1]['accepted']==pair[1][1]['accepted']
  summaries.append({'name':candidate['name'],'pairs':32,'acceptedBothConflictingSources':sum(v[0][1]['accepted'] for v in groups.values()),'rawSha256':sha(raw),'groups':{kind:sum(v[0][1]['accepted'] for v in groups.values() if v[0][0]['kind']==kind) for kind in ['scriptAlias','wholePrefixRemoved']}})
 save(RESULTS/'source-proof-summary.json',{'models':summaries,'seconds':time.monotonic()-start,'scope':'Consumed unscored postselection controls; identical suppliedpixels cannot establishscriptorigin, completeness or wholeprefix. Refusingboth losesvalidcounterpart; independent originalsource/geometry/inkownership required.'});print(json.dumps(summaries))
if __name__=='__main__':main()
