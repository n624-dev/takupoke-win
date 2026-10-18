"""Numeric-only full18710 backward budget; no optimizer/recognition quality."""
import hashlib
import json
from pathlib import Path
import resource
import time

import numpy as np
import onnx
from onnx import numpy_helper
from safetensors.torch import load_file
import torch
import torch.nn.functional as F

from svtr_exact import MediumSvtr
from reproduce_v2 import ROOT,RESULTS,MODEL,DICT,save


def main():
 fidelity=json.loads((RESULTS/'fidelity.json').read_text(encoding='utf-8'));assert fidelity['fidelityGatePassed'];record=RESULTS/'backward-budget.json'
 if record.exists():raise ValueError('Numeric budget already measured')
 torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.manual_seed(749193);initializers={i.name:torch.from_numpy(numpy_helper.to_array(i).copy()) for i in onnx.load(str(MODEL)).graph.initializer};model=MediumSvtr(initializers);model.load_state_dict(load_file(str(ROOT/'original-svtr.safetensors')),strict=True);weight=initializers['linear_8.w_0'].T.contiguous();bias=initializers['linear_8.b_0'];dictionary=json.loads(DICT.read_text(encoding='utf-8'));before={k:v.clone() for k,v in model.state_dict().items()};ctc=torch.nn.CTCLoss(blank=0,zero_infinity=True);target=torch.tensor([dictionary.index(c) for c in 'zI0lO1a_qQ']);measurements=[]
 for batch,frames in [(8,40),(16,40),(8,120),(16,120)]:
  context=torch.randn(batch,192,1,frames);shortcut=torch.randn_like(context)
  with torch.no_grad():teacher=F.linear(model(context,shortcut),weight,bias).softmax(-1)
  times=[];finite=True;maxgrad=0.
  for iteration in range(3):
   model.zero_grad(set_to_none=True);started=time.monotonic();features=model(context,shortcut);log_prob=F.linear(features,weight,bias).log_softmax(-1);loss=ctc(log_prob.transpose(0,1),target.repeat(batch),torch.full((batch,),frames,dtype=torch.long),torch.full((batch,),len(target),dtype=torch.long))+.2*F.kl_div(log_prob,teacher,reduction='batchmean')/frames;loss.backward();times.append(time.monotonic()-started);finite=finite and all(torch.isfinite(p.grad).all() for p in model.parameters());maxgrad=max(maxgrad,max(float(p.grad.abs().max()) for p in model.parameters()))
  measurements.append({'batch':batch,'frames':frames,'inputWidthEquivalent':frames*8,'seconds':times,'meanSeconds':sum(times)/len(times),'gradientsFinite':bool(finite),'maximumGradient':maxgrad,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024})
 assert all(torch.equal(v,before[k]) for k,v in model.state_dict().items());result={'scope':'Numeric random pre-SVTR tensors only, full18710 CTC plus full18710 teacherKL backward; no optimizer update, rendering, model selection or quality claim','sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'stateUnchanged':True,'seed':749193,'threads':2,'parameters':sum(p.numel() for p in model.parameters()),'measurements':measurements,'limits':'Concurrent Linux workload. Only3 numeric repeats per shape, no isolated speed/nativeWindows/iPhone claim. Feature cache time, rejection head and development eval are additional.'};save(record,result);print(json.dumps(result))


if __name__=='__main__':main()
