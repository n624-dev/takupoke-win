"""Postselection same-pixel provenance contradictions; no train/final credit."""
import hashlib
import json
from pathlib import Path
import time

import numpy as np
from PIL import Image
from safetensors.torch import load_file
import torch

from mixed_data import ROOT,PARENT,RESULTS,save,tensor
from mixed_train import DICT,TRUNK,HEAD
from mixed_model import DeltaHead
from fidelity import session
from image_tensor import greedy

PROBES=Path('/workspace/scratch/ascii-paddle-transfer-20261013-v1/source-proof-probes')


def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));manifest=json.loads((PROBES/'manifest.json').read_text(encoding='utf-8'));dictionary=json.loads(DICT.read_text(encoding='utf-8'));pool=json.loads((ROOT/'loss-pool.json').read_text(encoding='utf-8'))['indices']
 if (RESULTS/'source-proof-probe-freeze.json').exists():raise ValueError('No repeat of fixed probes')
 save(RESULTS/'source-proof-probe-freeze.json',{'scope':manifest['scope'],'consumedProbeManifestSha256':hashlib.sha256((PROBES/'manifest.json').read_bytes()).hexdigest(),'selectionSha256':hashlib.sha256((ROOT/'selection.json').read_bytes()).hexdigest(),'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'condition':'Reuse frozen provided pixels only; direct native FP32 BGR48; full18710; selected checkpoints immutable; never adjust selection/rejection thresholds','rows':64})
 torch.set_num_threads(2);torch.set_num_interop_threads(1);head=load_file(str(HEAD));trunk=session(TRUNK);features=[];started=time.monotonic()
 for row in manifest['rows']:
  path=PROBES/row['file'];assert hashlib.sha256(path.read_bytes()).hexdigest()==row['pngSha256'];value,valid=tensor(Image.open(path));feature=trunk.run(None,{'x':value[None]})[0];frames=feature.shape[1];valid_frames=int(np.sum((np.arange(frames)+.5)*value.shape[-1]/frames<valid));features.append((torch.from_numpy(feature),value.shape[-1],valid,valid_frames))
 summaries=[]
 for candidate in [selection['original']]+selection['candidates']:
  assert hashlib.sha256(Path(candidate['path']).read_bytes()).hexdigest()==candidate['sha256'];model=DeltaHead(head['weight'],head['bias'],dictionary,pool);trained=candidate['epoch']>0
  if trained:model.load_checkpoint(load_file(candidate['path']))
  model.eval();raw=RESULTS/f"source-proof-{candidate['name']}-unjudged.jsonl";records=[]
  with raw.open('x',encoding='utf-8',newline='\n') as stream,torch.inference_mode():
   for row,(feature,width,valid,valid_frames) in zip(manifest['rows'],features):
    probability=model.full_logits(feature).softmax(-1)[0].numpy();pieces=greedy(probability,dictionary);text=''.join(p['Text'] for p in pieces);minimum=min((p['Confidence'] for p in pieces),default=0);supported=all(p['End']*width<=valid*len(probability) for p in pieces);reject=float(model.rejection(feature,torch.arange(len(probability))[None]<valid_frames).sigmoid()[0]);accepted=bool(text) and minimum>=.8 and supported and (not trained or reject<=.05)
    record={'id':row['id'],'prediction':text,'pieces':pieces,'minimumConfidence':minimum,'supported':supported,'accepted':accepted,'rejectProbability':reject if trained else None,'inputWidth':width,'validWidth':valid,'timeSteps':len(probability),'sourceProofRequired':True};records.append(record);stream.write(json.dumps(record,ensure_ascii=False)+'\n')
  groups={}
  for row,record in zip(manifest['rows'],records):groups.setdefault(row['pair'],[]).append((row,record))
  for pair in groups.values():assert len(pair)==2 and pair[0][0]['pngSha256']==pair[1][0]['pngSha256'] and pair[0][1]['prediction']==pair[1][1]['prediction'] and pair[0][1]['accepted']==pair[1][1]['accepted']
  summaries.append({'name':candidate['name'],'samePixelPairs':len(groups),'acceptedBothConflictingSources':sum(p[0][1]['accepted'] for p in groups.values()),'refusedBothSources':sum(not p[0][1]['accepted'] for p in groups.values()),'groups':{kind:{'pairs':len(g),'acceptedBoth':sum(p[0][1]['accepted'] for p in g)} for kind in ['scriptAlias','wholePrefixRemoved'] for g in [[p for p in groups.values() if p[0][0]['kind']==kind]]},'rawSha256':hashlib.sha256(raw.read_bytes()).hexdigest()})
 save(RESULTS/'source-proof-probe-summary.json',{'scope':manifest['scope'],'models':summaries,'seconds':time.monotonic()-started,'limitations':'Identical provided pixels cannot establish script origin or missing whole-prefix geometry. Refusing both loses a valid counterpart. Require independent source/ownership proof; no model selection or production credit.'});print(json.dumps(summaries))


if __name__=='__main__':main()
