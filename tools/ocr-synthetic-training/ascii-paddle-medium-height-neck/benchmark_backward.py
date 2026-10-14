"""Numeric-only full18710 backward with trainable pool/local-neck, frozen SVTR."""
import hashlib
import json
from pathlib import Path
import resource
import time

import onnx
from onnx import numpy_helper
from safetensors.torch import load_file
import torch
import torch.nn.functional as F

from neck import HeightNeck
import importlib.util
_spec=importlib.util.spec_from_file_location('height_reproduction_reference',Path(__file__).with_name('reproduce.py'))
_reference=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(_reference)
ROOT,RESULTS,MODEL,DICT,save=(_reference.ROOT,_reference.RESULTS,_reference.MODEL,_reference.DICT,_reference.save)


def main():
 assert json.loads((RESULTS/'fidelity.json').read_text(encoding='utf-8'))['fidelityGatePassed'];record=RESULTS/'backward-budget.json'
 if record.exists():raise ValueError('Numeric budget already measured')
 torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.manual_seed(773711);initializers={v.name:torch.from_numpy(numpy_helper.to_array(v).copy()) for v in onnx.load(str(MODEL)).graph.initializer};model=HeightNeck(initializers);model.load_state_dict(load_file(str(ROOT/'original-height-neck.safetensors')),strict=True);weight=initializers['linear_8.w_0'].T.contiguous();bias=initializers['linear_8.b_0'];dictionary=json.loads(DICT.read_text(encoding='utf-8'));before={k:v.clone() for k,v in model.state_dict().items()};ctc=torch.nn.CTCLoss(blank=0,zero_infinity=True);target=torch.tensor([dictionary.index(c) for c in 'uI0lO1e_rR']);rows=[]
 for batch,local_width in [(8,80),(16,80),(8,240),(16,240)]:
  x=torch.randn(batch,768,3,local_width)
  with torch.no_grad():teacher_feature=model(x);teacher=F.linear(teacher_feature,weight,bias).softmax(-1)
  seconds=[];finite=True;maximum=0.
  for iteration in range(3):
   model.zero_grad(set_to_none=True);started=time.monotonic();feature=model(x);frames=feature.shape[1];log_prob=F.linear(feature,weight,bias).log_softmax(-1);loss=ctc(log_prob.transpose(0,1),target.repeat(batch),torch.full((batch,),frames,dtype=torch.long),torch.full((batch,),len(target),dtype=torch.long))+.2*F.kl_div(log_prob,teacher,reduction='batchmean')/frames+.02*F.mse_loss(feature,teacher_feature);loss.backward();seconds.append(time.monotonic()-started);finite=finite and all(torch.isfinite(p.grad).all() for p in model.parameters() if p.requires_grad);maximum=max(maximum,max(float(p.grad.abs().max()) for p in model.parameters() if p.requires_grad))
  rows.append({'batch':batch,'localWidth':local_width,'frames':local_width//2,'seconds':seconds,'meanSeconds':sum(seconds)/len(seconds),'gradientsFinite':bool(finite),'maximumGradient':maximum,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024})
 assert all(torch.equal(value,before[key]) for key,value in model.state_dict().items());result={'scope':'Numeric random prepool maps only; full18710CTC/fullteacherKL/teacherfeatureMSE backward; no optimizer/render/quality/model selection','sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'stateUnchanged':True,'svtrParametersFrozen':True,'trainableParameters':sum(p.numel() for p in model.parameters() if p.requires_grad),'threads':2,'seed':773711,'measurements':rows,'limits':'Three numeric repeats per shape under concurrent Linux workload. Lossless map decompression/rejecthead/cache/dev inference additional; nativeWindows/iPhone not measured.'};save(record,result);print(json.dumps(result))


if __name__=='__main__':main()
