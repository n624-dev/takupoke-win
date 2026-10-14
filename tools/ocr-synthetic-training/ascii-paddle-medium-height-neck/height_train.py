"""Frozen natural-line protocol, FP32 CNN caches and full18710 SVTR training."""
from collections import defaultdict
import copy
import hashlib
import json
from pathlib import Path
import random
import resource
import shutil
import sys
import time

import numpy as np
import onnx
from onnx import helper,numpy_helper,TensorProto
from PIL import Image
from safetensors.torch import load_file,save_file
import torch
import torch.nn.functional as F

from height_data import ROOT,PARENT,REPO,RESULTS,FONTS,save,excluded,generate,tensor,cmap
from height_model import Adapter
from height_reference import MODEL,MODEL_SHA,DICT,PREPOOL,FEATURE,ancestor,session
sys.path.insert(0,str(Path(__file__).parents[1]));from image_tensor import greedy
sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-foundation-ctc'));from diagnose import edits


def budget(spec):
 if shutil.disk_usage(ROOT).free<spec['minimumFreeDiskGiB']*1024**3:raise OSError('Free disk below frozen reserve')
 if resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024>spec['maximumRssMiB']:raise MemoryError('Own RSS exceeded frozen budget')
 if sum(p.stat().st_size for p in ROOT.rglob('*') if p.is_file())>spec['additionalDiskBudgetGiB']*1024**3:raise OSError('Own additional disk budget exceeded')


def cache(manifest,network,directory,spec):
 directory.mkdir();rows=[];started=time.monotonic();raw_total=0;compressed_total=0
 for i,row in enumerate(manifest['rows']):
  if time.monotonic()-started>spec['maximumCacheSeconds']:raise TimeoutError('LosslessFP32 cache deadline')
  path=ROOT/manifest['split']/row['file'];assert hashlib.sha256(path.read_bytes()).hexdigest()==row['pngSha256'];x,valid=tensor(Image.open(path));assert x.dtype==np.float32 and x.shape[-1]==row['inputWidth'] and valid==row['validWidth'];cnn,teacher=network.run(None,{'x':x[None]});cnn=cnn[0];teacher=teacher[0];frames=len(teacher);assert cnn.shape[0:2]==(768,3) and cnn.shape[-1]//2==frames and teacher.shape==(frames,192) and cnn.dtype==teacher.dtype==np.float32
  valid_frames=int(np.sum((np.arange(frames)+.5)*x.shape[-1]/frames<valid));assert valid_frames>=1;file=f'{row["id"]}.npz';cache_path=directory/file;np.savez_compressed(cache_path,cnn=cnn,teacher=teacher)
  with np.load(cache_path,allow_pickle=False) as restored:
   assert set(restored.files)=={'cnn','teacher'} and restored['cnn'].dtype==restored['teacher'].dtype==np.float32;assert restored['cnn'].tobytes()==cnn.tobytes() and restored['teacher'].tobytes()==teacher.tobytes()
  raw_bytes=cnn.nbytes+teacher.nbytes;compressed_bytes=cache_path.stat().st_size;raw_total+=raw_bytes;compressed_total+=compressed_bytes;rows.append({'id':row['id'],'file':file,'frames':frames,'localWidth':cnn.shape[-1],'validFrames':valid_frames,'inputTensorSha256':hashlib.sha256(x.tobytes()).hexdigest(),'cnnSha256':hashlib.sha256(cnn.tobytes()).hexdigest(),'teacherFeatureSha256':hashlib.sha256(teacher.tobytes()).hexdigest(),'npzSha256':hashlib.sha256(cache_path.read_bytes()).hexdigest(),'rawFp32Bytes':raw_bytes,'losslessNpzBytes':compressed_bytes,'decompressedBytesIdentical':True,'floatType':'float32'})
  if i%128==0:budget(spec);print(json.dumps({'stage':'cache','split':manifest['split'],'rows':i+1,'total':len(manifest['rows']),'seconds':time.monotonic()-started,'compressionRatio':compressed_total/raw_total,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024}),flush=True)
 result={'rows':rows,'floatType':'float32','encoding':'LosslessNPZ, allow_pickle=False, exactFP32decompressedbytehash','rawFp32Bytes':raw_total,'losslessNpzBytes':compressed_total,'ratio':compressed_total/raw_total,'seconds':time.monotonic()-started};save(directory/'cache.json',result);return result


def fetch(chosen,receipt,directory):
 items=[receipt['rows'][i] for i in chosen];frames=items[0]['frames'];local_width=items[0]['localWidth'];assert all(item['frames']==frames and item['localWidth']==local_width for item in items);cnns=[];teachers=[]
 for item in items:
  with np.load(directory/item['file'],allow_pickle=False) as saved:
   assert set(saved.files)=={'cnn','teacher'} and saved['cnn'].dtype==saved['teacher'].dtype==np.float32;cnns.append(np.array(saved['cnn'],copy=True));teachers.append(np.array(saved['teacher'],copy=True))
 valid=torch.arange(frames)[None]<torch.tensor([item['validFrames'] for item in items])[:,None]
 return torch.from_numpy(np.stack(cnns)),torch.from_numpy(np.stack(teachers)),valid


def evaluate(name,model,arrays,receipt,manifest,dictionary,mode):
 raw=RESULTS/(name+'-unjudged.jsonl')
 if raw.exists():raise ValueError('Fixed evaluation cannot repeat')
 model.eval();records=[];max_difference={'feature':0.,'logit':0.,'probability':0.}
 with raw.open('x',encoding='utf-8',newline='\n') as stream,torch.inference_mode():
  for i,row in enumerate(manifest['rows']):
   cnn,teacher_feature,valid=fetch([i],receipt,arrays);reference_logits=F.linear(teacher_feature,model.originalWeight,model.originalBias)
   if mode=='original':logits=reference_logits;feature=teacher_feature;reject_probability=None
   else:
    logits,feature,reject=model(cnn,valid);reject_probability=float(reject.sigmoid()[0]) if mode=='trained' else None
    if mode=='untrained':
     max_difference['feature']=max(max_difference['feature'],float((feature-teacher_feature).abs().max()));max_difference['logit']=max(max_difference['logit'],float((logits-reference_logits).abs().max()));max_difference['probability']=max(max_difference['probability'],float((logits.softmax(-1)-reference_logits.softmax(-1)).abs().max()))
   probability=logits.softmax(-1)[0].numpy();pieces=greedy(probability,dictionary);prediction=''.join(p['Text'] for p in pieces);minimum=min((p['Confidence'] for p in pieces),default=0);supported=all(p['End']*row['inputWidth']<=row['validWidth']*len(probability) for p in pieces);accepted=bool(prediction) and minimum>=.8 and supported and (mode!='trained' or reject_probability<=.05);r={'id':row['id'],'prediction':prediction,'pieces':pieces,'minimumConfidence':minimum,'rejectProbability':reject_probability,'accepted':accepted,'supported':supported,'inputWidth':row['inputWidth'],'validWidth':row['validWidth'],'timeSteps':len(probability),'source':row['source']};records.append(r);stream.write(json.dumps(r,ensure_ascii=False)+'\n')
 lookup={r['id']:r for r in manifest['rows']};positive=[r for r in records if lookup[r['id']]['selectionPositive']];negative=[r for r in records if lookup[r['id']]['reject']];retention=[r for r in records if lookup[r['id']]['retention']];exact=lambda r:r['prediction']==lookup[r['id']]['text'];summary={'name':name,'positiveRows':len(positive),'exact':sum(exact(r) for r in positive),'correctAccepted':sum(exact(r) and r['accepted'] for r in positive),'incorrectAccepted':sum(not exact(r) and r['accepted'] for r in positive),'negativeRows':len(negative),'negativeAccepted':sum(r['accepted'] for r in negative),'retentionRows':len(retention),'retentionExact':sum(exact(r) for r in retention),'paddingRows':sum(not r['supported'] for r in records),'cer':sum(len(edits(lookup[r['id']]['text'],r['prediction'])) for r in positive)/sum(len(lookup[r['id']]['text']) for r in positive),'groups':{kind:{'rows':len(g),'exact':sum(not lookup[r['id']]['reject'] and exact(r) for r in g),'correctAccepted':sum(not lookup[r['id']]['reject'] and exact(r) and r['accepted'] for r in g),'incorrectAccepted':sum(not lookup[r['id']]['reject'] and not exact(r) and r['accepted'] for r in g),'accepted':sum(r['accepted'] for r in g),'nonempty':sum(bool(r['prediction']) for r in g)} for kind in sorted({row['kind'] for row in manifest['rows']}) for g in [[r for r in records if lookup[r['id']]['kind']==kind]]},'rawSha256':hashlib.sha256(raw.read_bytes()).hexdigest(),'scope':'New independent fictional literal development on newly reserved KosugiMaru/ZenAntique fonts; not final or production qualification'}
 if mode=='untrained':summary['reconstructionMaxDifferences']=max_difference
 save(RESULTS/(name+'-summary.json'),summary);print(json.dumps({'stage':'evaluation','metrics':summary}),flush=True);return summary,records


def main():
 ROOT.mkdir(parents=True,exist_ok=True);RESULTS.mkdir(parents=True,exist_ok=True)
 if (ROOT/'freeze.json').exists():raise ValueError('Fixed training already frozen, no repeat')
 source=Path(__file__).parent;protocol_path=source/'height_protocol.json';protocol=json.loads(protocol_path.read_text(encoding='utf-8'));spec=protocol['training'];assert hashlib.sha256(MODEL.read_bytes()).hexdigest()==MODEL_SHA
 fidelity_path=REPO/'research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-height-neck-v1/fidelity.json';assert json.loads(fidelity_path.read_text(encoding='utf-8'))['fidelityGatePassed'];fidelity_freeze=json.loads((fidelity_path.parent/'freeze.json').read_text(encoding='utf-8'))
 for name,sha in fidelity_freeze['sources'].items():assert hashlib.sha256((source/name).read_bytes()).hexdigest()==sha
 assert hashlib.sha256((source.parent/'ascii-paddle-medium-svtr/svtr_exact.py').read_bytes()).hexdigest()==fidelity_freeze['sharedSvtrSourceSha256']
 dictionary=json.loads(DICT.read_text(encoding='utf-8'));torch.set_num_threads(spec['threads']);torch.set_num_interop_threads(1);torch.manual_seed(spec['seed']);budget(spec)
 graph=onnx.load(str(MODEL));initializers={v.name:torch.from_numpy(numpy_helper.to_array(v).copy()) for v in graph.graph.initializer};model=Adapter(initializers,dictionary);state=PARENT/'original-height-neck.safetensors';model.neck.load_state_dict(load_file(str(state)),strict=True)
 observing=ancestor(graph,[PREPOOL,FEATURE]);onnx.checker.check_model(observing);observer=ROOT/'native-prepool-teacher-observer.onnx';onnx.save(observing,str(observer));del graph,observing,initializers
 prior=excluded();save(RESULTS/'exclusion-receipt.json',json.loads((ROOT/'exclusion-receipt.json').read_text(encoding='utf-8')));freeze={'protocol':protocol,'protocolSha256':hashlib.sha256(protocol_path.read_bytes()).hexdigest(),'sources':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [source/'height_protocol.json',source/'height_data.py',source/'height_model.py',source/'height_train.py',source/'neck.py',source/'reproduce.py',source/'height_reference.py']},'preprocessSha256':hashlib.sha256((source.parent/'image_tensor.py').read_bytes()).hexdigest(),'modelSha256':MODEL_SHA,'dictionarySha256':hashlib.sha256(DICT.read_bytes()).hexdigest(),'fidelitySha256':hashlib.sha256(fidelity_path.read_bytes()).hexdigest(),'originalHeightNeckStateSha256':hashlib.sha256(state.read_bytes()).hexdigest(),'observerSha256':hashlib.sha256(observer.read_bytes()).hexdigest(),'fontHashes':{name:hashlib.sha256(path.read_bytes()).hexdigest() for name,path in FONTS.items()},'exclusionSha256':hashlib.sha256((ROOT/'exclusion-receipt.json').read_bytes()).hexdigest(),'freeDiskGiBBeforeCorpus':shutil.disk_usage(ROOT).free/1024**3,'trainableParameters':sum(p.numel() for p in model.parameters() if p.requires_grad),'finalOpened':False};save(ROOT/'freeze.json',freeze);save(RESULTS/'freeze.json',freeze)
 train=generate(protocol,'train',prior);used={r['text'] for r in train['rows'] if r['text']};dev=generate(protocol,'development',prior|used);train_targets=used;dev_targets={r['text'] for r in dev['rows'] if r['text']};assert not train_targets.intersection(dev_targets) and not (train_targets|dev_targets).intersection(prior);assert all(c in dictionary for r in train['rows'] if r['text'] for c in r['text']);assert all(ord(c) in cmap(r['font']) for m in [train,dev] for r in m['rows'] if r['text'] for c in r['text']);covered={chr(code):sum((r['text'] or '').count(chr(code)) for r in train['rows']) for code in range(32,127)};assert min(covered.values())>0;damage=[r for m in [train,dev] for r in m['rows'] if r['kind'] in ['clipped','interior_damage']];assert all(r['source']['removedNonwhiteSamples']>0 for r in damage);save(RESULTS/'corpus-verification.json',{'trainRows':len(train['rows']),'developmentRows':len(dev['rows']),'trainManifestSha256':hashlib.sha256((ROOT/'train/manifest.json').read_bytes()).hexdigest(),'developmentManifestSha256':hashlib.sha256((ROOT/'development/manifest.json').read_bytes()).hexdigest(),'trainDevelopmentDisjoint':True,'priorExactLiteralDisjoint':True,'allLiteralGlyphsPresentInFontCmap':True,'allSourceInkRetained':all(r['source']['allNonwhiteRetained'] for m in [train,dev] for r in m['rows']),'actualInkRemovedDamageRows':len(damage),'printableAsciiTrainOccurrences':covered,'finalOpened':False})
 network=session(observer);train_cache=cache(train,network,ROOT/'train-cache',spec);dev_cache=cache(dev,network,ROOT/'development-cache',spec);del network;train_maps=ROOT/'train-cache';dev_maps=ROOT/'development-cache';original,original_records=evaluate('original',model,dev_maps,dev_cache,dev,dictionary,'original');untrained,untrained_records=evaluate('reconstructed-untrained',model,dev_maps,dev_cache,dev,dictionary,'untrained');intervals=lambda r:[(p['Text'],p['Start'],p['End']) for p in r['pieces']];same=all(intervals(a)==intervals(b) and a['accepted']==b['accepted'] for a,b in zip(original_records,untrained_records));save(RESULTS/'development-initialization-fidelity.json',{'scope':'Before optimizer; newdev same native original input maps, no quality selection or correction','rows':len(dev['rows']),'allTextIntervalsAndFloorDecisionsIdentical':same,'maximumDifferences':untrained['reconstructionMaxDifferences']})
 if not same:save(RESULTS/'halt.json',{'reason':'Predeclared reconstruction development gate failed','trainingStarted':False,'finalOpened':False});raise ValueError('Reconstructed newdev differs from original; stop before training')
 optimizer=torch.optim.AdamW([{'params':[model.neck.poolDelta],'lr':spec['poolLearningRate']},{'params':[model.neck.shortcut.weight,model.neck.projection.weight,model.neck.depthwise.weight],'lr':spec['neckLearningRate']},{'params':[model.deltaWeight,model.deltaBias],'lr':spec['asciiDeltaLearningRate']},{'params':model.reject.parameters(),'lr':spec['rejectLearningRate']}],weight_decay=spec['weightDecay']);ctc_loss=torch.nn.CTCLoss(blank=0,zero_infinity=True);index={c:i for i,c in enumerate(dictionary)};rng=random.Random(spec['seed']);buckets=defaultdict(list)
 for i,item in enumerate(train_cache['rows']):buckets[item['localWidth']].append(i)
 ambiguity=set();collision_groups=[]
 for key in ['pngSha256','inputTensorSha256']:
  grouping=defaultdict(list)
  for i,(row,item) in enumerate(zip(train['rows'],train_cache['rows'])):
   if row['ctcEligible'] and row['text']:grouping[(row if key=='pngSha256' else item)[key]].append(i)
  for sha,indices in grouping.items():
   if len({train['rows'][i]['text'] for i in indices})>1:ambiguity.update(indices);collision_groups.append({'hashType':key,'hash':sha,'ids':[train['rows'][i]['id'] for i in indices]})
 for i,(row,item) in enumerate(zip(train['rows'],train_cache['rows'])):
  if row['ctcEligible'] and row['text']:assert len(row['text'])+sum(a==b for a,b in zip(row['text'],row['text'][1:]))<=item['frames']
 save(RESULTS/'pretraining-contradiction-audit.json',{'scope':protocol['contradictionAudit'],'excludedOptimizerRows':len(ambiguity),'groups':collision_groups,'ctcFrameFeasibility':True,'targetsNeverRepaired':True})
 # Exact-prepool-width buckets exclude ambiguous targets without classifying valid glyphs as damaged.
 buckets=defaultdict(list)
 for i,item in enumerate(train_cache['rows']):
  if i not in ambiguity:buckets[item['localWidth']].append(i)
 original_by_id={r['id']:r for r in original_records};dev_lookup={r['id']:r for r in dev['rows']};candidates=[];history=[];started=time.monotonic();stopped=False
 for epoch in range(1,spec['epochs']+1):
  model.train();batches=[]
  for frames,group in sorted(buckets.items()):
   order=list(group);rng.shuffle(order);batches.extend(order[i:i+spec['batchSize']] for i in range(0,len(order),spec['batchSize']))
  rng.shuffle(batches);losses=[];completed=0
  for batch_number,chosen in enumerate(batches):
   if time.monotonic()-started>spec['maximumTrainingSeconds']:stopped=True;break
   cnn,teacher_features,valid=fetch(chosen,train_cache,train_maps);logits,features,reject=model(cnn,valid);log_prob=logits.log_softmax(-1);targets=[];eligible=[];lengths=[]
   for j,i in enumerate(chosen):
    row=train['rows'][i]
    if row['ctcEligible']:eligible.append(j);targets.extend(index[c] for c in row['text']);lengths.append(len(row['text']))
   frames=features.shape[1];ctc=ctc_loss(log_prob[eligible].transpose(0,1),torch.tensor(targets,dtype=torch.long),torch.full((len(eligible),),frames,dtype=torch.long),torch.tensor(lengths,dtype=torch.long)) if eligible else features.sum()*0
   with torch.no_grad():teacher=F.linear(teacher_features,model.originalWeight,model.originalBias).softmax(-1)
   kl=F.kl_div(log_prob,teacher,reduction='batchmean')/frames;mse=F.mse_loss(features,teacher_features);bce=F.binary_cross_entropy_with_logits(reject,torch.tensor([train['rows'][i]['reject'] for i in chosen],dtype=torch.float32));loss=ctc+spec['teacherKlWeight']*kl+spec['teacherFeatureMseWeight']*mse+spec['rejectBceWeight']*bce
   if not torch.isfinite(loss):raise ValueError('Nonfinite full loss')
   optimizer.zero_grad(set_to_none=True);loss.backward();torch.nn.utils.clip_grad_norm_(model.parameters(),spec['gradientClip']);optimizer.step();losses.append([float(v.detach()) for v in [ctc,kl,mse,bce]]);completed+=len(chosen)
   if batch_number%48==0:budget(spec);print(json.dumps({'stage':'training','epoch':epoch,'completed':completed,'total':len(train['rows']),'ctcKlMseBce':losses[-1],'seconds':time.monotonic()-started,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024}),flush=True)
  if stopped:break
  history.append({'epoch':epoch,'meanCtcKlMseBce':np.mean(losses,axis=0).tolist(),'seconds':time.monotonic()-started})
  if epoch in spec['evaluateEpochs']:
   path=ROOT/f'epoch-{epoch}-adapter.safetensors';save_file(model.checkpoint(),str(path));summary,records=evaluate(f'epoch-{epoch}',model,dev_maps,dev_cache,dev,dictionary,'trained');summary['retentionRegressions']=sum(dev_lookup[r['id']]['retention'] and original_by_id[r['id']]['prediction']==dev_lookup[r['id']]['text'] and r['prediction']!=dev_lookup[r['id']]['text'] for r in records);candidates.append({'name':f'epoch-{epoch}','epoch':epoch,'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'metrics':summary})
 mixed=lambda m:m['groups']['mixed_code']['exact']+m['groups']['mixed_confusable']['exact'];base={'name':'original','epoch':0,'path':str(MODEL),'sha256':MODEL_SHA,'metrics':original};eligible=[base]+[c for c in candidates if mixed(c['metrics'])>=mixed(original) and c['metrics']['incorrectAccepted']<=original['incorrectAccepted'] and c['metrics']['negativeAccepted']<=original['negativeAccepted'] and c['metrics']['retentionRegressions']==0];selected=sorted(eligible,key=lambda c:(-c['metrics']['correctAccepted'],-c['metrics']['exact'],c['metrics']['incorrectAccepted'],c['metrics']['negativeAccepted'],c['metrics']['cer'],c['epoch']))[0];selection={'selected':selected,'original':base,'candidates':candidates,'trainingSeconds':time.monotonic()-started,'cacheSeconds':train_cache['seconds']+dev_cache['seconds'],'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'history':history,'budgetStopped':stopped,'status':'Research-only/unqualified/inactive, original fallback eligible, final unopened'};save(ROOT/'selection.json',selection);save(RESULTS/'selection.json',selection);print(json.dumps({'stage':'selection','selected':selected['name'],'metrics':selected['metrics']}),flush=True)


if __name__=='__main__':main()
