"""Once-only frozen-CNN adaptation from v1; independent newdev and untouched fresh."""
from collections import defaultdict
import hashlib,json,random,resource,time
from pathlib import Path
import numpy as np,torch,yaml
from safetensors.torch import load_file,save_file
from data import ROOT,PARENT,RESULTS,save,sha
from cache import build,fetch,budget
from metrics import evaluate
from adapter import Adapter
from model import ASSETS

def state_hash(values):
 h=hashlib.sha256()
 for k,v in sorted(values.items()):h.update(k.encode());h.update(v.cpu().numpy().tobytes())
 return h.hexdigest()
def main():
 if (ROOT/'optimizer-start.json').exists() or (ROOT/'selection.json').exists():raise RuntimeError('Optimizer trial cannot repeat')
 freeze=json.loads((ROOT/'freeze.json').read_text(encoding='utf-8'));source=Path(__file__).parent;protocol=json.loads((source/'protocol.json').read_text(encoding='utf-8'));assert sha(source/'protocol.json')==freeze['protocolSha256'];assert all(sha(source/n)==h for n,h in freeze['sources'].items());spec=protocol['training'];torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.manual_seed(spec['seed']);dictionary=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' '];assert len(dictionary)==438;checkpoint=Path('/workspace/scratch/ascii-english-v5-font-balanced-20261014-v1/adaptation-v1/epoch-4.safetensors');assert sha(checkpoint)==freeze['initialStateSha256'];model=Adapter().eval();model.load_state_dict(load_file(checkpoint),strict=True);teacher=Adapter().eval();teacher.load_state_dict(load_file(checkpoint),strict=True);config=model.official.config.to_dict();cnn_hash=state_hash(model.official.model.state_dict());bn_before={k:v.clone() for k,v in model.official.state_dict().items() if any(t in k for t in ['running_mean','running_var','num_batches_tracked'])};train=json.loads((ROOT/'train/manifest.json').read_text(encoding='utf-8'));dev=json.loads((ROOT/'development/manifest.json').read_text(encoding='utf-8'));assert all(sha(ROOT/split/'manifest.json')==freeze['manifestSha256'][split] for split in ['train','development']);budget(spec)
 train_cache=build(train,teacher.official,ROOT/'train-cache',spec);dev_cache=build(dev,teacher.official,ROOT/'development-cache',spec);del teacher
 original,base_records=evaluate('active-baseline',model,dev_cache,ROOT/'development-cache',dev,dictionary,'trained');initialized,init_records=evaluate('initialized-cached',model,dev_cache,ROOT/'development-cache',dev,dictionary,'cached');assert initialized['exactFp32SelfConsistency'];assert base_records==init_records;save(RESULTS/'newdev-initialization-self-consistency.json',{'rows':len(dev['rows']),'officialDirectVsCachedFeatureProbabilityExactFp32':True,'wholeOriginalVsCachedCTCConfidenceRejectPaddingAcceptanceRecordsExact':True,'cnnFrozenStateSha256':cnn_hash,'newdevRawSha256':original['rawSha256'],'newdevManifestSha256':sha(ROOT/'development/manifest.json'),'optimizerStarted':False})
 conflicts=set();groups=[];units=defaultdict(list)
 for i,r in enumerate(train['rows']):units[r['unitId']].append(i)
 for key in ['pngSha256','inputSha256','cnnSha256']:
  by=defaultdict(list)
  for i,(row,item) in enumerate(zip(train['rows'],train_cache['rows'])):by[(row if key=='pngSha256' else item)[key]].append(i)
  for h,indices in by.items():
   labels={train['rows'][i]['text'] for i in indices if not train['rows'][i]['reject']};opposite=bool(labels) and any(train['rows'][i]['reject'] for i in indices)
   if len(labels)>1 or opposite:
    for i in indices:conflicts.update(units[train['rows'][i]['unitId']])
    groups.append({'hashType':key,'hash':h,'ids':[train['rows'][i]['id'] for i in indices],'oppositePositiveNegative':opposite,'entireUnitsExcluded':True})
 for row,item in zip(train['rows'],train_cache['rows']):
  if row['ctcEligible']:assert len(row['text'])+sum(a==b for a,b in zip(row['text'],row['text'][1:]))<=item['frames']
 save(RESULTS/'pretraining-contradiction-audit.json',{'excludedUpdateRows':len(conflicts),'groups':groups,'entireAtomicUnitsExcluded':True,'noLabelRepair':True,'allFrameLengthsFeasible':True});save(ROOT/'optimizer-start.json',{'freezeSha256':sha(ROOT/'freeze.json'),'newdevRawSha256':original['rawSha256'],'cnnFrozenStateSha256':cnn_hash,'optimizerParameters':sum(p.numel() for p in model.parameters() if p.requires_grad),'unoptimizedBackboneParameters':sum(p.numel() for p in model.official.model.parameters()),'excludedUpdateRows':len(conflicts),'freshOpened':False})
 optimizer=torch.optim.AdamW([{'params':model.official.head.encoder.parameters(),'lr':spec['encoderLearningRate']},{'params':model.official.head.head.parameters(),'lr':spec['headLearningRate']},{'params':model.reject.parameters(),'lr':spec['rejectLearningRate']}],weight_decay=spec['weightDecay']);ctc_loss=torch.nn.CTCLoss(blank=0,zero_infinity=True);index={c:i for i,c in enumerate(dictionary)};rng=random.Random(spec['seed']);buckets=defaultdict(list)
 for i,item in enumerate(train_cache['rows']):
  if i not in conflicts:buckets[item['inputWidth']].append(i)
 candidates=[];history=[];start=time.monotonic();stopped=False
 for epoch in range(1,spec['epochs']+1):
  model.eval();batches=[]
  for width,indices in sorted(buckets.items()):
   ids=list(indices);rng.shuffle(ids);batches.extend(ids[i:i+spec['batchSize']] for i in range(0,len(ids),spec['batchSize']))
  rng.shuffle(batches);values=[];completed=0
  for n,indices in enumerate(batches):
   if time.monotonic()-start>spec['watchdogTrainingSeconds']:stopped=True;break
   _,cnn,teacher_feature,teacher_probability,valid=fetch(indices,train_cache,ROOT/'train-cache');logits,feature,rejected=model.from_cnn(cnn,valid);logprob=logits.log_softmax(-1);targets=[];eligible=[];lengths=[]
   for j,i in enumerate(indices):
    row=train['rows'][i]
    if row['ctcEligible']:eligible.append(j);targets.extend(index[c] for c in row['text']);lengths.append(len(row['text']))
   frames=feature.shape[1];ctc=ctc_loss(logprob[eligible].transpose(0,1),torch.tensor(targets,dtype=torch.long),torch.full((len(eligible),),frames,dtype=torch.long),torch.tensor(lengths,dtype=torch.long)) if eligible else feature.sum()*0;kl=torch.nn.functional.kl_div(logprob,teacher_probability,reduction='batchmean')/frames;mse=torch.nn.functional.mse_loss(feature,teacher_feature);bce=torch.nn.functional.binary_cross_entropy_with_logits(rejected,torch.tensor([train['rows'][i]['reject'] for i in indices],dtype=torch.float32));loss=ctc+spec['teacherKlWeight']*kl+spec['teacherFeatureMseWeight']*mse+spec['rejectBceWeight']*bce;assert torch.isfinite(loss);optimizer.zero_grad(set_to_none=True);loss.backward();torch.nn.utils.clip_grad_norm_([p for p in model.parameters() if p.requires_grad],spec['gradientClip']);optimizer.step();values.append([float(x.detach()) for x in [ctc,kl,mse,bce]]);completed+=len(indices)
   if n%96==0:budget(spec);print(json.dumps({'stage':'training','epoch':epoch,'completed':completed,'total':len(train['rows'])-len(conflicts),'seconds':time.monotonic()-start,'ctcKlMseBce':values[-1],'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024}),flush=True)
  if stopped:break
  history.append({'epoch':epoch,'meanLosses':np.mean(values,axis=0).tolist(),'seconds':time.monotonic()-start})
  path=ROOT/f'epoch-{epoch}.safetensors';save_file(model.state_dict(),str(path));metrics,records=evaluate(f'epoch-{epoch}',model,dev_cache,ROOT/'development-cache',dev,dictionary,'trained');candidates.append({'name':f'epoch-{epoch}','epoch':epoch,'path':str(path),'sha256':sha(path),'metrics':metrics});assert state_hash(model.official.model.state_dict())==cnn_hash and all(torch.equal(v,model.official.state_dict()[k]) for k,v in bn_before.items())
 assert config==model.official.config.to_dict();base={'name':'active-baseline','epoch':0,'path':str(checkpoint),'sha256':sha(checkpoint),'metrics':original}
 def eligible(m):return m['codeMetrics']['cer']<original['codeMetrics']['cer'] and m['cer']<original['cer'] and all(m['fontMetrics'][f]['editErrors']<=original['fontMetrics'][f]['editErrors'] for f in protocol['development']['jpFonts']) and m['groups']['natural']['cer']<=original['groups']['natural']['cer']+.002 and m['reviewMetrics']['wrongHighConfidence']<=original['reviewMetrics']['wrongHighConfidence']+1 and m['incorrectAccepted']<=original['incorrectAccepted']+1 and m['negativeAccepted']<=original['negativeAccepted']+1 and m['allUnsupportedPaddingRefused']
 choices=[base]+[c for c in candidates if eligible(c['metrics'])];selected=min(choices,key=lambda c:(c['metrics']['codeMetrics']['cer'],c['metrics']['cer'],-c['metrics']['exact'],c['epoch']));assert sha('/workspace/scratch/ocr-frozen-20261013/v6-medium.onnx')==freeze['japanesePrimarySha256'];result={'selected':selected,'activeBaseline':base,'candidates':candidates,'history':history,'trainingSeconds':time.monotonic()-start,'cacheSeconds':train_cache['seconds']+dev_cache['seconds'],'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'budgetStopped':stopped,'cnnBytesAndAllBnStatsUnchanged':True,'cnnStateSha256':cnn_hash,'configUnchanged':True,'freshOpened':False,'trainingStarted':True,'status':'Independent research only; no activation/whole-document claim/iOS edits'};save(ROOT/'selection.json',result);save(RESULTS/'selection.json',result);print(json.dumps({'stage':'selection','name':selected['name'],'metrics':selected['metrics']}),flush=True)
if __name__=='__main__':main()
