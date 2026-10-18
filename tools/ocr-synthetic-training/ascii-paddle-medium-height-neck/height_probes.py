"""Postselection same-pixel source conflicts; never use them to select a model."""
import hashlib
import json
from pathlib import Path
import time

import numpy as np
from PIL import Image
from safetensors.torch import load_file
import torch
import torch.nn.functional as F
import onnx
from onnx import numpy_helper

from height_data import ROOT,RESULTS,save,tensor
from height_model import Adapter
from height_train import MODEL,DICT,session
from image_tensor import greedy

PROBES=Path('/workspace/scratch/ascii-paddle-transfer-20261013-v1/source-proof-probes')


def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));manifest=json.loads((PROBES/'manifest.json').read_text(encoding='utf-8'));dictionary=json.loads(DICT.read_text(encoding='utf-8'))
 if (RESULTS/'source-proof-probe-freeze.json').exists():raise ValueError('Fixed probes cannot repeat')
 save(RESULTS/'source-proof-probe-freeze.json',{'scope':manifest['scope'],'priorConsumedProbeManifestSha256':hashlib.sha256((PROBES/'manifest.json').read_bytes()).hexdigest(),'selectionSha256':hashlib.sha256((ROOT/'selection.json').read_bytes()).hexdigest(),'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'condition':'Existing provided PNGs only/direct native FP32 BGR48/full18710/fixed original and completed checkpoints; no thresholds or selection changes','rows':len(manifest['rows'])})
 torch.set_num_threads(2);torch.set_num_interop_threads(1);initializers={v.name:torch.from_numpy(numpy_helper.to_array(v).copy()) for v in onnx.load(str(MODEL)).graph.initializer};network=session(ROOT/'native-prepool-teacher-observer.onnx');cache=[];started=time.monotonic()
 for row in manifest['rows']:
  path=PROBES/row['file'];assert hashlib.sha256(path.read_bytes()).hexdigest()==row['pngSha256'];value,valid=tensor(Image.open(path));cnn,feature=network.run(None,{'x':value[None]});frames=feature.shape[1];mask=torch.from_numpy(((np.arange(frames)+.5)*value.shape[-1]/frames<valid)[None]);cache.append((torch.from_numpy(cnn),torch.from_numpy(feature),mask,value.shape[-1],valid))
 summaries=[]
 for candidate in [selection['original']]+selection['candidates']:
  assert hashlib.sha256(Path(candidate['path']).read_bytes()).hexdigest()==candidate['sha256'];model=Adapter(initializers,dictionary);trained=candidate['epoch']>0
  if trained:model.load_checkpoint(load_file(candidate['path']))
  model.eval();raw=RESULTS/f"source-proof-{candidate['name']}-unjudged.jsonl";records=[]
  with raw.open('x',encoding='utf-8',newline='\n') as stream,torch.inference_mode():
   for row,(cnn,feature,mask,width,valid) in zip(manifest['rows'],cache):
    if trained:logits,_,reject=model(cnn,mask);reject_probability=float(reject.sigmoid()[0])
    else:logits=F.linear(feature,model.originalWeight,model.originalBias);reject_probability=None
    probability=logits.softmax(-1)[0].numpy();pieces=greedy(probability,dictionary);text=''.join(p['Text'] for p in pieces);minimum=min((p['Confidence'] for p in pieces),default=0);supported=all(p['End']*width<=valid*len(probability) for p in pieces);accepted=bool(text) and minimum>=.8 and supported and (not trained or reject_probability<=.05);record={'id':row['id'],'prediction':text,'pieces':pieces,'minimumConfidence':minimum,'supported':supported,'accepted':accepted,'rejectProbability':reject_probability,'inputWidth':width,'validWidth':valid,'timeSteps':len(probability),'sourceProofRequired':True};records.append(record);stream.write(json.dumps(record,ensure_ascii=False)+'\n')
  groups={}
  for row,record in zip(manifest['rows'],records):groups.setdefault(row['pair'],[]).append((row,record))
  for pair in groups.values():assert len(pair)==2 and pair[0][0]['pngSha256']==pair[1][0]['pngSha256'] and pair[0][1]['prediction']==pair[1][1]['prediction'] and pair[0][1]['accepted']==pair[1][1]['accepted']
  summaries.append({'name':candidate['name'],'samePixelPairs':len(groups),'acceptedBothConflictingSources':sum(p[0][1]['accepted'] for p in groups.values()),'refusedBothSources':sum(not p[0][1]['accepted'] for p in groups.values()),'groups':{kind:{'pairs':len(g),'acceptedBoth':sum(p[0][1]['accepted'] for p in g)} for kind in ['scriptAlias','wholePrefixRemoved'] for g in [[p for p in groups.values() if p[0][0]['kind']==kind]]},'rawSha256':hashlib.sha256(raw.read_bytes()).hexdigest()})
 save(RESULTS/'source-proof-probe-summary.json',{'scope':manifest['scope'],'models':summaries,'seconds':time.monotonic()-started,'limitations':'Identical provided pixels cannot establish script origin or whole missing-prefix geometry. Refusing both loses a valid counterpart. Require independent source/ownership proof; no model selection/production credit.'});print(json.dumps(summaries))


if __name__=='__main__':main()
