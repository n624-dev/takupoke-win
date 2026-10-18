"""FP32 cached-medium fullhead mixed-line bounded transfer and fixed selection."""
import hashlib
import json
from pathlib import Path
import random
import resource
import shutil
import sys
import time

import numpy as np
import onnxruntime as ort
from PIL import Image
from safetensors.torch import load_file,save_file
import torch
import torch.nn.functional as F

from mixed_data import ROOT,PARENT,REPO,RESULTS,FONTS,save,excluded,generate,tensor
from mixed_model import DeltaHead
sys.path.insert(0,str(Path(__file__).parents[1]))
from image_tensor import greedy
sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-foundation-ctc'))
from diagnose import edits

DICT=Path('/workspace/scratch/ocr-frozen-20261013/v6-medium-dictionary.json')
TRUNK=PARENT/'medium-frozen-features.onnx'
HEAD=PARENT/'original-full-ctc-head.safetensors'


def ensure_budget(spec):
 if shutil.disk_usage(ROOT).free<spec['minimumFreeDiskGiB']*1024**3:raise OSError('Free disk below prospective reserve; no foreign asset deletion')
 if resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024>spec['maximumRssMiB']:raise MemoryError('Own-process RSS budget exceeded')
 own=sum(path.stat().st_size for path in ROOT.rglob('*') if path.is_file())
 if own>spec['additionalDiskBudgetGiB']*1024**3:raise OSError('Own additional disk budget exceeded')


def cache(manifest,session,weight,bias,pool,directory,spec):
 directory.mkdir();features_path=directory/'features.f32';scores_path=directory/'loss-logits.f32';rows=[];offset=0;started=time.monotonic();pool_index=torch.tensor(pool);other=torch.ones(weight.shape[0],dtype=torch.bool);other[pool_index]=False
 with features_path.open('xb') as feature_stream,scores_path.open('xb') as score_stream,torch.inference_mode():
  for i,row in enumerate(manifest['rows']):
   if time.monotonic()-started>spec['maximumCacheSeconds']:raise TimeoutError('Feature-cache budget expired')
   path=ROOT/manifest['split']/row['file'];assert hashlib.sha256(path.read_bytes()).hexdigest()==row['pngSha256']
   value,valid=tensor(Image.open(path));assert value.dtype==np.float32 and value.shape[-1]==row['inputWidth'] and valid==row['validWidth']
   feature=session.run(None,{'x':value[None]})[0][0];assert feature.dtype==np.float32 and feature.shape[-1]==192
   full=F.linear(torch.from_numpy(feature),weight,bias);loss_scores=torch.cat([full.index_select(-1,pool_index),full[:,other].logsumexp(-1,keepdim=True)],-1).numpy()
   frames=len(feature);centers=(np.arange(frames,dtype=np.float64)+.5)*value.shape[-1]/frames;valid_frames=int(np.sum(centers<valid));assert valid_frames>=1
   feature_stream.write(feature.tobytes());score_stream.write(loss_scores.tobytes())
   rows.append({'id':row['id'],'offset':offset,'frames':frames,'validFrames':valid_frames,'inputTensorSha256':hashlib.sha256(value.tobytes()).hexdigest(),'featureSha256':hashlib.sha256(feature.tobytes()).hexdigest(),'lossLogitsSha256':hashlib.sha256(loss_scores.tobytes()).hexdigest(),'floatType':'float32'});offset+=frames
   if i%512==0:ensure_budget(spec);print(json.dumps({'stage':'cache','split':manifest['split'],'rows':i+1,'total':len(manifest['rows']),'seconds':time.monotonic()-started,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024}),flush=True)
 receipt={'rows':rows,'frames':offset,'featureChannels':192,'lossClasses':len(pool)+1,'floatType':'float32','featureFileSha256':hashlib.sha256(features_path.read_bytes()).hexdigest(),'scoreFileSha256':hashlib.sha256(scores_path.read_bytes()).hexdigest(),'seconds':time.monotonic()-started};save(directory/'cache.json',receipt)
 return receipt


def load_cache(directory,receipt):
 return np.memmap(directory/'features.f32',dtype=np.float32,mode='r',shape=(receipt['frames'],192)),np.memmap(directory/'loss-logits.f32',dtype=np.float32,mode='r',shape=(receipt['frames'],receipt['lossClasses']))


def evaluate(name,model,features,cache_receipt,manifest,dictionary,trained):
 raw=RESULTS/f'{name}-unjudged.jsonl'
 if raw.exists():raise ValueError('Fixed development already consumed')
 records=[];model.eval()
 with raw.open('x',encoding='utf-8',newline='\n') as stream,torch.inference_mode():
  for row,item in zip(manifest['rows'],cache_receipt['rows']):
   feature=torch.from_numpy(np.array(features[item['offset']:item['offset']+item['frames']],copy=True)[None]);prob=model.full_logits(feature).softmax(-1)[0].numpy();valid_mask=torch.arange(item['frames'])[None]<item['validFrames'];reject=float(model.rejection(feature,valid_mask)[0].sigmoid())
   pieces=greedy(prob,dictionary);prediction=''.join(piece['Text'] for piece in pieces);minimum=min((piece['Confidence'] for piece in pieces),default=0);supported=all(piece['End']*row['inputWidth']<=row['validWidth']*item['frames'] for piece in pieces);accepted=bool(prediction) and minimum>=.8 and supported and (not trained or reject<=.05)
   record={'id':row['id'],'prediction':prediction,'pieces':pieces,'minimumConfidence':minimum,'rejectProbability':reject if trained else None,'accepted':accepted,'supported':supported,'inputWidth':row['inputWidth'],'validWidth':row['validWidth'],'timeSteps':item['frames'],'source':row['source']};records.append(record);stream.write(json.dumps(record,ensure_ascii=False)+'\n')
 lookup={row['id']:row for row in manifest['rows']};positive=[r for r in records if lookup[r['id']]['selectionPositive']];negative=[r for r in records if lookup[r['id']]['reject']];retention=[r for r in records if lookup[r['id']]['retention']];exact=lambda r:r['prediction']==lookup[r['id']]['text']
 summary={'name':name,'positiveRows':len(positive),'exact':sum(exact(r) for r in positive),'correctAccepted':sum(exact(r) and r['accepted'] for r in positive),'incorrectAccepted':sum(not exact(r) and r['accepted'] for r in positive),'negativeRows':len(negative),'negativeAccepted':sum(r['accepted'] for r in negative),'cer':sum(len(edits(lookup[r['id']]['text'],r['prediction'])) for r in positive)/sum(len(lookup[r['id']]['text']) for r in positive),'retentionRows':len(retention),'retentionExact':sum(exact(r) for r in retention),'paddingRows':sum(not r['supported'] for r in records),'groups':{kind:{'rows':len(group),'exact':sum(not lookup[r['id']]['reject'] and exact(r) for r in group),'correctAccepted':sum(not lookup[r['id']]['reject'] and exact(r) and r['accepted'] for r in group),'incorrectAccepted':sum(not lookup[r['id']]['reject'] and not exact(r) and r['accepted'] for r in group),'accepted':sum(r['accepted'] for r in group),'nonempty':sum(bool(r['prediction']) for r in group)} for kind in sorted({row['kind'] for row in manifest['rows']}) for group in [[r for r in records if lookup[r['id']]['kind']==kind]]},'rawSha256':hashlib.sha256(raw.read_bytes()).hexdigest(),'scope':'New independent fictional development only; full18710 decoding unchanged, no correction/final/activation'}
 save(RESULTS/f'{name}-summary.json',summary);print(json.dumps({'stage':'development','summary':summary}),flush=True)
 return summary,records


def main():
 protocol=json.loads((Path(__file__).parent/'mixed_protocol.json').read_text(encoding='utf-8'));spec=protocol['training'];RESULTS.mkdir(parents=True,exist_ok=True)
 if (ROOT/'freeze.json').exists():raise ValueError('Frozen mixed run already started')
 ensure_budget(spec);torch.set_num_threads(spec['threads']);torch.set_num_interop_threads(1);torch.manual_seed(spec['seed']);np.random.seed(spec['seed']);random.seed(spec['seed'])
 fidelity=json.loads((RESULTS.parent/'fidelity.json').read_text(encoding='utf-8'));assert fidelity['matches']['torchTextIntervals']==48 and fidelity['maximumDifferences']['torchVsOriginalLogits']==0
 equivalence=json.loads((RESULTS.parent/'loss-equivalence.json').read_text(encoding='utf-8'));assert max(equivalence['differences'].values())<1e-5
 dictionary=json.loads(DICT.read_text(encoding='utf-8'));index={c:i for i,c in enumerate(dictionary)};state=load_file(str(HEAD));weight,bias=state['weight'],state['bias'];forbidden=excluded()
 freeze={'protocol':protocol,'protocolSha256':hashlib.sha256(json.dumps(protocol,sort_keys=True).encode('utf-8')).hexdigest(),'sources':{name:hashlib.sha256((Path(__file__).parent/name).read_bytes()).hexdigest() for name in ['mixed_protocol.json','mixed_data.py','mixed_model.py','mixed_train.py','loss_equivalence.py']},'nativePreprocessingSha256':hashlib.sha256((Path(__file__).parents[1]/'image_tensor.py').read_bytes()).hexdigest(),'trunkSha256':hashlib.sha256(TRUNK.read_bytes()).hexdigest(),'headSha256':hashlib.sha256(HEAD.read_bytes()).hexdigest(),'dictionarySha256':hashlib.sha256(DICT.read_bytes()).hexdigest(),'fontHashes':{name:hashlib.sha256(path.read_bytes()).hexdigest() for name,path in FONTS.items()},'exclusionReceiptSha256':hashlib.sha256((ROOT/'exclusion-receipt.json').read_bytes()).hexdigest(),'devFontExposure':'BIZUDGothic/KaiseiTokumin parent confirmed unconsumed and reserved; downloaded pinned/OFL. Prior fonts train only. Sawarabi final not accessed.','diskFreeGiB':shutil.disk_usage(ROOT).free/1024**3,'status':'Research-only; protocol freeze before corpus/model selection/training'}
 save(ROOT/'freeze.json',freeze);save(RESULTS/'freeze.json',freeze);save(RESULTS/'exclusion-receipt.json',json.loads((ROOT/'exclusion-receipt.json').read_text(encoding='utf-8')))
 train=generate(protocol,'train',forbidden);forbidden.update(row['text'] for row in train['rows'] if row['text']);dev=generate(protocol,'development',forbidden)
 characters={c for row in train['rows'] if row['ctcEligible'] for c in row['text']};assert all(c in index for c in characters);pool=sorted({0,*(index[chr(c)] for c in range(32,127)),*(index[c] for c in characters)})
 save(ROOT/'loss-pool.json',{'indices':pool,'classes':len(pool)+1,'otherIsLossOnly':True,'inferenceDictionaryClasses':18710})
 options=ort.SessionOptions();options.intra_op_num_threads=2;options.inter_op_num_threads=1;session=ort.InferenceSession(str(TRUNK),sess_options=options,providers=['CPUExecutionProvider'])
 train_cache=cache(train,session,weight,bias,pool,ROOT/'train-cache',spec);dev_cache=cache(dev,session,weight,bias,pool,ROOT/'development-cache',spec);del session
 train_features,train_scores=load_cache(ROOT/'train-cache',train_cache);dev_features,dev_scores=load_cache(ROOT/'development-cache',dev_cache)
 model=DeltaHead(weight,bias,dictionary,pool);original,original_records=evaluate('original',model,dev_features,dev_cache,dev,dictionary,False);original_by_id={r['id']:r for r in original_records};dev_lookup={row['id']:row for row in dev['rows']}
 optimizer=torch.optim.AdamW([{'params':[model.deltaWeight,model.deltaBias],'lr':spec['asciiDeltaLearningRate']},{'params':model.reject.parameters(),'lr':spec['rejectLearningRate']}],weight_decay=spec['weightDecay']);loss_fn=torch.nn.CTCLoss(blank=0,zero_infinity=False);pool_mapping={full:i for i,full in enumerate(pool)};start=time.monotonic();history=[];candidates=[];budget_stopped=False
 for epoch in range(1,spec['epochs']+1):
  order=list(range(len(train['rows'])));random.Random(spec['seed']+epoch).shuffle(order);losses=[]
  for offset in range(0,len(order),spec['batchSize']):
   if time.monotonic()-start>spec['maximumTrainingSeconds']:budget_stopped=True;break
   chosen=order[offset:offset+spec['batchSize']];max_frames=max(train_cache['rows'][i]['frames'] for i in chosen);feature=np.zeros((len(chosen),max_frames,192),np.float32);scores=np.zeros((len(chosen),max_frames,len(pool)+1),np.float32);real=np.zeros((len(chosen),max_frames),bool);valid=np.zeros_like(real);negative=[];eligible=[];targets=[];target_lengths=[];input_lengths=[]
   for j,i in enumerate(chosen):
    item=train_cache['rows'][i];begin=item['offset'];end=begin+item['frames'];feature[j,:item['frames']]=train_features[begin:end];scores[j,:item['frames']]=train_scores[begin:end];real[j,:item['frames']]=True;valid[j,:item['validFrames']]=True;row=train['rows'][i];negative.append(row['reject'])
    if row['ctcEligible']:
     eligible.append(j);targets.extend(pool_mapping[index[c]] for c in row['text']);target_lengths.append(len(row['text']));input_lengths.append(item['frames'])
   feature_t=torch.from_numpy(feature);scores_t=torch.from_numpy(scores);log_prob=model.loss_logits(feature_t,scores_t).log_softmax(-1);reject=model.rejection(feature_t,torch.from_numpy(valid));teacher=scores_t.softmax(-1)
   ctc=loss_fn(log_prob[eligible].transpose(0,1),torch.tensor(targets,dtype=torch.long),torch.tensor(input_lengths,dtype=torch.long),torch.tensor(target_lengths,dtype=torch.long)) if eligible else log_prob.sum()*0
   kl=(F.kl_div(log_prob,teacher,reduction='none').sum(-1)*torch.from_numpy(real)).sum()/torch.from_numpy(real).sum();bce=F.binary_cross_entropy_with_logits(reject,torch.tensor(negative,dtype=torch.float32));l2=model.deltaWeight.square().mean()+model.deltaBias.square().mean();loss=ctc+spec['teacherKlWeight']*kl+spec['rejectBceWeight']*bce+spec['deltaL2Weight']*l2
   if not torch.isfinite(loss):raise ValueError('Nonfinite loss')
   optimizer.zero_grad(set_to_none=True);loss.backward();torch.nn.utils.clip_grad_norm_(model.parameters(),spec['gradientClip']);optimizer.step();losses.append([float(ctc.detach()),float(kl.detach()),float(bce.detach())])
   if offset%1024==0:ensure_budget(spec);print(json.dumps({'stage':'training','epoch':epoch,'completed':offset+len(chosen),'ctc':losses[-1][0],'teacherKl':losses[-1][1],'rejectBce':losses[-1][2],'seconds':time.monotonic()-start,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024}),flush=True)
  if budget_stopped:break
  history.append({'epoch':epoch,'meanCtcTeacherKlRejectBce':np.mean(losses,axis=0).tolist()})
  if epoch in spec['evaluateEpochs']:
   path=ROOT/f'epoch-{epoch}-delta.safetensors';save_file(model.checkpoint(),str(path));summary,records=evaluate(f'epoch-{epoch}',model,dev_features,dev_cache,dev,dictionary,True)
   summary['retentionRegressions']=sum(dev_lookup[r['id']]['retention'] and original_by_id[r['id']]['prediction']==dev_lookup[r['id']]['text'] and r['prediction']!=dev_lookup[r['id']]['text'] for r in records)
   candidates.append({'name':f'epoch-{epoch}','epoch':epoch,'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'metrics':summary})
 mixed=lambda metrics:metrics['groups']['mixed_code']['exact']+metrics['groups']['mixed_confusable']['exact']
 original_candidate={'name':'original','epoch':0,'path':str(HEAD),'sha256':hashlib.sha256(HEAD.read_bytes()).hexdigest(),'metrics':original};eligible=[original_candidate]+[c for c in candidates if mixed(c['metrics'])>=mixed(original) and c['metrics']['incorrectAccepted']<=original['incorrectAccepted'] and c['metrics']['negativeAccepted']<=original['negativeAccepted'] and c['metrics']['retentionRegressions']==0]
 selected=sorted(eligible,key=lambda c:(-c['metrics']['correctAccepted'],-c['metrics']['exact'],c['metrics']['incorrectAccepted'],c['metrics']['negativeAccepted'],c['metrics']['cer'],c['epoch']))[0]
 selection={'selected':selected,'original':original_candidate,'candidates':candidates,'trainingSeconds':time.monotonic()-start,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'history':history,'budgetStopped':budget_stopped,'cacheSeconds':train_cache['seconds']+dev_cache['seconds'],'status':'Research-only/unqualified/inactive; final untouched'};save(ROOT/'selection.json',selection);save(RESULTS/'selection.json',selection);print(json.dumps({'stage':'selection','selected':selected['name'],'metrics':selected['metrics']}),flush=True)


if __name__=='__main__':main()
