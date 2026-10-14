"""Independent official class self-consistency and numeric no-update budget."""
import hashlib,json,resource,time
from pathlib import Path
import numpy as np,torch,yaml
from PIL import Image
from model import ASSETS,load,feature_logits
ROOT=Path('/workspace/scratch/ascii-english-v5-font-balanced-20261014-v1');REPO=Path('/workspace/scratch/takupoke-win');RESULTS=REPO/'research-results/ocr-synthetic-training-20261013/ascii-english-v5-font-balanced-v1'
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def save(p,v):Path(p).write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
def main():
 ROOT.mkdir(exist_ok=True);RESULTS.mkdir(parents=True,exist_ok=True)
 if (RESULTS/'freeze.json').exists():raise ValueError('Fixed independent preflight cannot repeat')
 receipt=json.loads((ASSETS/'download-receipt.json').read_text(encoding='utf-8'))
 for name,item in receipt['files'].items():assert sha(ASSETS/name)==item['sha256']
 dictionary=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' '];assert len(dictionary)==438 and all(chr(c) in dictionary for c in range(32,127));torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.manual_seed(845371);model=load();assert all(n.eps==1e-6 for n in [model.head.encoder.norm]+[n for b in model.head.encoder.svtr_block for n in [b.layer_norm1,b.layer_norm2]]);before={k:v.clone() for k,v in model.state_dict().items()};config=model.config.to_dict();freeze={'scope':'Official HF class/config/safetensors as independent foundation; no ONNX transplant/native parity claim, no quality/final/training','repository':receipt['repository'],'revision':receipt['revision'],'assets':receipt['files'],'parameters':sum(p.numel() for p in model.parameters()),'headRows':438,'normEpsilon':'All five unchanged official1e-6','sources':{p.name:sha(p) for p in [Path(__file__),Path(__file__).with_name('model.py')]},'cpuThreads':2,'seed':845371,'maximumRssMiB':4096,'numericShapes':[[1,w] for w in [320,481,500,640,960]]+[[2,w] for w in [320,481,500,640,960]],'backwardShapes':[[1,320],[2,320],[1,960],[2,960]],'selfConsistencyGate':'Same unmodified model/direct official forward vs explicit backbone+encoder+full438linear softmax has exactFP32outputbytes at every registeredshape; repeatedinference exact; config/weights/state unchanged; no tolerance weakening'};save(RESULTS/'freeze.json',freeze);rows=[]
 with torch.inference_mode():
  for batch,width in freeze['numericShapes']:
   x=torch.randn(batch,3,48,width).clamp(-1,1);feature,logits=feature_logits(model,x);p=logits.softmax(-1);direct=model(pixel_values=x).last_hidden_state;again=model(pixel_values=x).last_hidden_state;identical=torch.equal(p,direct) and torch.equal(direct,again);assert identical;rows.append({'batch':batch,'width':width,'timeSteps':p.shape[1],'exactByteSelfConsistency':True,'finite':bool(torch.isfinite(p).all())})
 save(RESULTS/'self-consistency.json',{'scope':freeze['scope'],'numeric':rows,'allExact':True,'nativeOnnxParityClaim':False})
 target=torch.tensor([dictionary.index(c) for c in 'uIlO01_k9']);ctc=torch.nn.CTCLoss(blank=0,zero_infinity=True);budget=[]
 for batch,width in freeze['backwardShapes']:
  x=torch.randn(batch,3,48,width).clamp(-1,1)
  with torch.no_grad():teacher_feature,teacher_logits=feature_logits(model,x);teacher=teacher_logits.softmax(-1)
  timings=[]
  for repeat in range(3):
   model.zero_grad(set_to_none=True);start=time.monotonic();feature,logits=feature_logits(model,x);frames=feature.shape[1];lp=logits.log_softmax(-1);loss=ctc(lp.transpose(0,1),target.repeat(batch),torch.full((batch,),frames,dtype=torch.long),torch.full((batch,),len(target),dtype=torch.long))+.2*torch.nn.functional.kl_div(lp,teacher,reduction='batchmean')/frames+.02*torch.nn.functional.mse_loss(feature,teacher_feature);loss.backward();timings.append(time.monotonic()-start);assert torch.isfinite(loss) and all(p.grad is None or torch.isfinite(p.grad).all() for p in model.parameters());assert resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024<4096
  budget.append({'batch':batch,'width':width,'meanSeconds':sum(timings)/len(timings),'seconds':timings,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'finiteGradients':True});save(RESULTS/'partial-numeric-budget.json',{'measurements':budget,'trainingStarted':False})
 assert model.config.to_dict()==config and all(torch.equal(v,before[k]) for k,v in model.state_dict().items());save(RESULTS/'preflight.json',{'selfConsistencyPassed':True,'backwardFinite':True,'stateAndConfigUnchanged':True,'measurements':budget,'headVocabulary':438,'cnnParameters':sum(p.numel() for p in model.model.parameters()),'allParameters':sum(p.numel() for p in model.parameters()),'noOptimizerOrTraining':True,'nativeOnnxClaim':False});print(json.dumps(budget))
if __name__=='__main__':main()
