"""Numeric no-update full CNN/encoder/full438 backward feasibility only."""
import json,hashlib,resource,time
from pathlib import Path
import torch
from torch.nn import functional as F
from native_math import load
from reproduce_math import RESULTS,BASE,save
OUT=RESULTS.parent/'numeric-budget'
def main():
 OUT.mkdir(exist_ok=False);torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.manual_seed(842173);model=load().eval();before={k:v.clone() for k,v in model.state_dict().items()};dictionary=json.loads((BASE/'dictionary.json').read_text(encoding='utf-8'));ctc=torch.nn.CTCLoss(blank=0,zero_infinity=True);target=torch.tensor([dictionary.index(c) for c in 'kIlO01_qR7']);rows=[]
 for batch,width in [(4,320),(8,320),(4,960),(8,960)]:
  x=torch.randn(batch,3,48,width).clamp(-1,1)
  with torch.no_grad():teacher_feature,teacher_logits=model(x);teacher_prob=teacher_logits.softmax(-1)
  timings=[]
  for repeat in range(3):
   model.zero_grad(set_to_none=True);start=time.monotonic();feat,logits=model(x);frames=feat.shape[1];logprob=logits.log_softmax(-1);loss=ctc(logprob.transpose(0,1),target.repeat(batch),torch.full((batch,),frames,dtype=torch.long),torch.full((batch,),len(target),dtype=torch.long))+.2*F.kl_div(logprob,teacher_prob,reduction='batchmean')/frames+.02*F.mse_loss(feat,teacher_feature);loss.backward();timings.append(time.monotonic()-start);assert torch.isfinite(loss) and all(p.grad is None or torch.isfinite(p.grad).all() for p in model.parameters());assert resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024<4096
  rows.append({'batch':batch,'inputWidth':width,'timeSteps':frames,'seconds':timings,'meanSeconds':sum(timings)/len(timings),'gradientsFinite':True,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024})
 assert all(torch.equal(v,before[k]) for k,v in model.state_dict().items());result={'scope':'Numeric FP32 random input only/fullCNN+encoder+full438head backward, no optimizer/render/training/quality or selection; numerical native fidelity gate still failed and is not overridden','seed':842173,'cpuThreads':2,'allParameters':sum(p.numel() for p in model.parameters()),'cnnParameters':sum(p.numel() for p in model.cnn.parameters()),'stateUnchanged':True,'batchNormRunningStatsFrozen':True,'measurements':rows,'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'trainingStarted':False};save(OUT/'budget.json',result);print(json.dumps(result))
if __name__=='__main__':main()
