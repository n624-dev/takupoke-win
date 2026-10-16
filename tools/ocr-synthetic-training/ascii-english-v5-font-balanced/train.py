"""Once-only independent officialHF fullCNN/full438 bounded adaptation."""
from collections import defaultdict
import hashlib,json,random,resource,shutil,time
from pathlib import Path
import numpy as np,torch,yaml
from PIL import Image
from safetensors.torch import save_file
from data import ROOT,PARENT,REPO,RESULTS,FONTS,save,excluded,generate,tensor
from model import ASSETS,load,feature_logits
from adapter import Adapter
import sys
sys.path.insert(0,str(Path(__file__).parents[1]));from image_tensor import greedy
sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-foundation-ctc'));from diagnose import edits
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def budget(spec):
 if shutil.disk_usage(PARENT).free<spec['minimumFreeDiskGiB']*1024**3:raise OSError('Free reserve exceeded')
 if resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024>spec['maximumRssMiB']:raise MemoryError('Own RSS limit')
 if sum(p.stat().st_size for p in PARENT.rglob('*') if p.is_file())>spec['additionalDiskBudgetGiB']*1024**3:raise OSError('Own disk limit')
def cache(manifest,teacher,folder,spec):
 folder.mkdir();rows=[];start=time.monotonic()
 with torch.inference_mode():
  for i,row in enumerate(manifest['rows']):
   if time.monotonic()-start>spec['maximumCacheSeconds']:raise TimeoutError('Cache deadline')
   p=ROOT/manifest['split']/row['file'];assert sha(p)==row['pngSha256'];x,valid=tensor(Image.open(p));assert x.dtype==np.float32 and x.shape[-1]==row['inputWidth'] and valid==row['validWidth'];feature,logits=feature_logits(teacher,torch.from_numpy(x[None]));prob=teacher(pixel_values=torch.from_numpy(x[None])).last_hidden_state;assert torch.equal(prob,logits.softmax(-1));f=feature[0].numpy();pval=prob[0].numpy();name=row['id']+'.npz';path=folder/name;np.savez_compressed(path,input=x,teacher=f,probability=pval)
   with np.load(path,allow_pickle=False) as saved:
    assert all(saved[k].dtype==np.float32 and saved[k].tobytes()==v.tobytes() for k,v in [('input',x),('teacher',f),('probability',pval)])
   frames=len(f);valid_frames=int(np.sum((np.arange(frames)+.5)*x.shape[-1]/frames<valid));assert valid_frames>0;rows.append({'id':row['id'],'file':name,'inputWidth':x.shape[-1],'frames':frames,'validFrames':valid_frames,'inputSha256':hashlib.sha256(x.tobytes()).hexdigest(),'teacherSha256':hashlib.sha256(f.tobytes()).hexdigest(),'probabilitySha256':hashlib.sha256(pval.tobytes()).hexdigest(),'npzSha256':sha(path),'bytes':path.stat().st_size,'fp32Bytes':x.nbytes+f.nbytes+pval.nbytes,'shape':{'input':list(x.shape),'teacher':list(f.shape),'probability':list(pval.shape)}})
   if i%128==0:budget(spec);print(json.dumps({'stage':'cache','split':manifest['split'],'rows':i+1,'planned':len(manifest['rows']),'seconds':time.monotonic()-start,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024}),flush=True)
 result={'rows':rows,'seconds':time.monotonic()-start,'totalBytes':sum(r['bytes'] for r in rows),'fp32Bytes':sum(r['fp32Bytes'] for r in rows),'precision':'Lossless allFP32 original input/officialteacherfeature/directofficial438probability'};save(folder/'cache.json',result);return result
def fetch(indices,receipt,folder):
 items=[receipt['rows'][i] for i in indices];assert len({r['inputWidth'] for r in items})==len({r['frames'] for r in items})==1;values={k:[] for k in ['input','teacher','probability']}
 for row in items:
  with np.load(folder/row['file'],allow_pickle=False) as data:
   for key in values:assert data[key].dtype==np.float32;values[key].append(np.array(data[key],copy=True))
 mask=torch.arange(items[0]['frames'])[None]<torch.tensor([r['validFrames'] for r in items])[:,None]
 return *(torch.from_numpy(np.stack(values[k])) for k in ['input','teacher','probability']),mask

def evaluate(name,adapter,receipt,folder,manifest,dictionary,mode):
 path=RESULTS/(name+'-unjudged.jsonl')
 if path.exists():raise ValueError('Fixed inference cannot repeat')
 adapter.eval();records=[];maximum={'feature':0.,'probability':0.};byteequal=True
 with path.open('x',encoding='utf-8',newline='\n') as output,torch.inference_mode():
  for i,row in enumerate(manifest['rows']):
   x,teacher,baseline,valid=fetch([i],receipt,folder)
   if mode=='baseline':prob=baseline[0].numpy();reject=None
   else:
    logits,feature,rejected=adapter(x,valid);p=logits.softmax(-1);prob=p[0].numpy();reject=float(rejected.sigmoid()[0]) if mode=='trained' else None
    if mode=='untrained':byteequal=byteequal and torch.equal(p,baseline) and torch.equal(feature,teacher);maximum['feature']=max(maximum['feature'],float((feature-teacher).abs().max()));maximum['probability']=max(maximum['probability'],float((p-baseline).abs().max()))
   pieces=greedy(prob,dictionary);text=''.join(p['Text'] for p in pieces);minimum=min((p['Confidence'] for p in pieces),default=0);supported=all(p['End']*row['inputWidth']<=row['validWidth']*len(prob) for p in pieces);ascii_domain=all(32<=ord(c)<=126 for c in text);accepted=bool(text) and minimum>=.8 and supported and ascii_domain and (mode!='trained' or reject<=.05);r={'id':row['id'],'prediction':text,'pieces':pieces,'minimumConfidence':minimum,'rejectProbability':reject,'accepted':accepted,'supported':supported,'printableAscii':ascii_domain,'inputWidth':row['inputWidth'],'validWidth':row['validWidth'],'timeSteps':len(prob),'source':row['source']};records.append(r);output.write(json.dumps(r,ensure_ascii=False)+'\n')
 lookup={r['id']:r for r in manifest['rows']};positive=[r for r in records if lookup[r['id']]['selectionPositive']];negative=[r for r in records if lookup[r['id']]['reject']];exact=lambda r:r['prediction']==lookup[r['id']]['text'];groups={}
 for kind in sorted({r['kind'] for r in manifest['rows']}):
  g=[r for r in records if lookup[r['id']]['kind']==kind];groups[kind]={'rows':len(g),'exact':sum(not lookup[r['id']]['reject'] and exact(r) for r in g),'correctAccepted':sum(not lookup[r['id']]['reject'] and exact(r) and r['accepted'] for r in g),'incorrectAccepted':sum(not lookup[r['id']]['reject'] and not exact(r) and r['accepted'] for r in g),'accepted':sum(r['accepted'] for r in g),'nonempty':sum(bool(r['prediction']) for r in g)}
 result={'name':name,'positiveRows':len(positive),'exact':sum(exact(r) for r in positive),'correctAccepted':sum(exact(r) and r['accepted'] for r in positive),'incorrectAccepted':sum(not exact(r) and r['accepted'] for r in positive),'negativeRows':len(negative),'negativeAccepted':sum(r['accepted'] for r in negative),'paddingRows':sum(not r['supported'] for r in records),'cer':sum(len(edits(lookup[r['id']]['text'],r['prediction'])) for r in positive)/sum(len(lookup[r['id']]['text']) for r in positive),'groups':groups,'rawSha256':sha(path),'scope':'Fresh literals and research-new development font families; development only, independent ASCII branch; no final/wholefield/documentqualification'}
 def characterize(rows):
  rows=[r for r in rows if lookup[r['id']]['selectionPositive']];characters=sum(len(lookup[r['id']]['text']) for r in rows);errors=sum(len(edits(lookup[r['id']]['text'],r['prediction'])) for r in rows)
  return {'rows':len(rows),'characters':characters,'editErrors':errors,'cer':errors/characters if characters else None,'exact':sum(exact(r) for r in rows),'correctLowConfidence':sum(exact(r) and r['minimumConfidence']<.8 for r in rows),'wrongHighConfidence':sum(not exact(r) and r['minimumConfidence']>=.8 for r in rows),'accepted':sum(r['accepted'] for r in rows)}
 for kind in groups:groups[kind].update({k:v for k,v in characterize([r for r in records if lookup[r['id']]['kind']==kind]).items() if k!='rows'})
 result['fontMetrics']={f:characterize([r for r in positive if lookup[r['id']]['font']==f]) for f in manifest['fonts'] if any(lookup[r['id']]['font']==f for r in positive)}
 result['codeMetrics']=characterize([r for r in positive if lookup[r['id']]['kind'] in ['code','lookalike']])
 result['monoCodeMetrics']=characterize([r for r in positive if lookup[r['id']]['kind'] in ['code','lookalike'] and lookup[r['id']]['font'] in ['Cousine','UbuntuMono']])
 result['fontCodeMetrics']={f:characterize([r for r in positive if lookup[r['id']]['font']==f and lookup[r['id']]['kind'] in ['code','lookalike']]) for f in ['Cousine','UbuntuMono']}
 result['reviewMetrics']=characterize(positive)
 result['allUnsupportedPaddingRefused']=all(not r['accepted'] for r in records if not r['supported'])
 if mode=='untrained':result.update({'exactFp32SelfConsistency':byteequal,'maximumDifferences':maximum})
 save(RESULTS/(name+'-summary.json'),result);print(json.dumps({'stage':'evaluation','metrics':result}),flush=True);return result,records

def main():
 ROOT.mkdir(parents=True,exist_ok=True);RESULTS.mkdir(parents=True,exist_ok=True)
 if (ROOT/'freeze.json').exists():raise ValueError('Frozen training cannot repeat')
 source=Path(__file__).parent;protocol=json.loads((source/'protocol.json').read_text(encoding='utf-8'));spec=protocol['training'];pref=RESULTS.parent/'preflight.json';passed=json.loads(pref.read_text(encoding='utf-8'));assert passed['selfConsistencyPassed'] and passed['backwardFinite'] and passed['stateAndConfigUnchanged'];torch.set_num_threads(spec['threads']);torch.set_num_interop_threads(1);torch.manual_seed(spec['seed']);dictionary=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' '];teacher=load();model=Adapter().eval();assert len(dictionary)==438 and model.official.head.head.out_features==438
 baseline_config=model.official.config.to_dict();bn_before={k:v.clone() for k,v in model.official.state_dict().items() if 'running_mean' in k or 'running_var' in k or 'num_batches_tracked' in k};primary=Path('/workspace/scratch/ocr-frozen-20261013/v6-medium.onnx');primary_sha=sha(primary);budget(spec);prior=excluded();save(RESULTS/'exclusion-receipt.json',json.loads((ROOT/'exclusion-receipt.json').read_text(encoding='utf-8')));state=ROOT/'initial-adapter.safetensors';save_file(model.state_dict(),str(state));freeze={'protocol':protocol,'protocolSha256':sha(source/'protocol.json'),'sources':{p.name:sha(p) for p in [source/'protocol.json',source/'data.py',source/'model.py',source/'adapter.py',source/'train.py']},'assetReceipt':json.loads((ASSETS/'download-receipt.json').read_text(encoding='utf-8')),'dictionarySha256':hashlib.sha256(json.dumps(dictionary,ensure_ascii=False).encode('utf-8')).hexdigest(),'fontHashes':{n:sha(p) for n,p in FONTS.items()},'newFontReceipt':json.loads((PARENT/'assets/font-download-receipt.json').read_text(encoding='utf-8')),'preprocessSha256':sha(source.parent/'image_tensor.py'),'initialStateSha256':sha(state),'preflightSha256':sha(pref),'japanesePrimarySha256':primary_sha,'trainableParameters':sum(p.numel() for p in model.parameters() if p.requires_grad),'officialConfig':baseline_config,'selfConsistencySourceFreezeSha256':sha(RESULTS.parent/'freeze.json'),'finalOpened':False};save(ROOT/'freeze.json',freeze);save(RESULTS/'freeze.json',freeze)
 train=generate(protocol,'train',prior);used={r['renderedLiteral'] for r in train['rows']};dev=generate(protocol,'development',prior|used);assert not used.intersection(r['renderedLiteral'] for r in dev['rows']);covered={chr(c):sum((r['text'] or '').count(chr(c)) for r in train['rows']) for c in range(32,127)};assert min(covered.values())>0;balance={}
 for m in [train,dev]:
  balance[m['split']]={}
  for kind in protocol[m['split']]['positiveCounts']:
   count={font:sum(r['kind']==kind and r['font']==font for r in m['rows']) for font in protocol[m['split']]['fonts']};assert len(set(count.values()))==1;balance[m['split']][kind]=count
 save(RESULTS/'pretraining-corpus-audit.json',{'rows':{'train':len(train['rows']),'development':len(dev['rows'])},'trainDevLiteralDisjoint':True,'priorLiteralExcluded':True,'all95PrintableCovered':covered,'eachPositiveGroupFontsEqual':True,'trainMonoRatio':.5,'counts':balance,'naturalPositiveRatio':{m['split']:sum(r['kind']=='natural' for r in m['rows'])/sum(r['selectionPositive'] for r in m['rows']) for m in [train,dev]},'actualMonoPositiveRatio':{m['split']:sum(r['selectionPositive'] and r['font'] in ['IBMPlexMono','Inconsolata','FiraMono','SpaceMono','Cousine','UbuntuMono'] for r in m['rows'])/sum(r['selectionPositive'] for r in m['rows']) for m in [train,dev]},'fontsNeverMovedFromDevToTrain':not set(protocol['train']['fonts']).intersection(protocol['development']['fonts']),'allProvidedInkRetained':all(r['source']['allNonwhiteRetained'] for m in [train,dev] for r in m['rows']),'wholeUnknownCjkAndMixedNeverCut':True,'manifestSha256':{m['split']:sha(ROOT/m['split']/'manifest.json') for m in [train,dev]}})
 train_cache=cache(train,teacher,ROOT/'train-cache',spec);dev_cache=cache(dev,teacher,ROOT/'development-cache',spec);del teacher;original,original_records=evaluate('official-baseline',model,dev_cache,ROOT/'development-cache',dev,dictionary,'baseline');untrained,untrained_records=evaluate('initialized-adapter',model,dev_cache,ROOT/'development-cache',dev,dictionary,'untrained');same=untrained['exactFp32SelfConsistency'] and all(a['prediction']==b['prediction'] and [(p['Text'],p['Start'],p['End']) for p in a['pieces']]==[(p['Text'],p['Start'],p['End']) for p in b['pieces']] and a['accepted']==b['accepted'] for a,b in zip(original_records,untrained_records));save(RESULTS/'newdev-initialization-self-consistency.json',{'rows':len(dev['rows']),'exactFp32AndDecisions':same,'maximumDifferences':untrained['maximumDifferences'],'optimizerStarted':False});assert same
 conflicts=set();groups=[]
 for key in ['pngSha256','inputSha256']:
  grouped=defaultdict(list)
  for i,(row,item) in enumerate(zip(train['rows'],train_cache['rows'])):grouped[(row if key=='pngSha256' else item)[key]].append(i)
  for h,indices in grouped.items():
   positives={train['rows'][i]['text'] for i in indices if not train['rows'][i]['reject']};opposite=any(train['rows'][i]['reject'] for i in indices) and bool(positives)
   if len(positives)>1 or opposite:conflicts.update(indices);groups.append({'hashType':key,'hash':h,'ids':[train['rows'][i]['id'] for i in indices],'positiveNegativeConflict':opposite})
 for row,item in zip(train['rows'],train_cache['rows']):
  if row['ctcEligible']:assert len(row['text'])+sum(a==b for a,b in zip(row['text'],row['text'][1:]))<=item['frames']
 save(RESULTS/'pretraining-contradiction-audit.json',{'excludedUpdateRows':len(conflicts),'groups':groups,'noLabelRepair':True,'frameFeasibilityPassed':True})
 optimizer=torch.optim.AdamW([{'params':model.official.model.parameters(),'lr':spec['cnnLearningRate']},{'params':model.official.head.encoder.parameters(),'lr':spec['encoderLearningRate']},{'params':model.official.head.head.parameters(),'lr':spec['headLearningRate']},{'params':model.reject.parameters(),'lr':spec['rejectLearningRate']}],weight_decay=spec['weightDecay']);ctc_loss=torch.nn.CTCLoss(blank=0,zero_infinity=True);index={c:i for i,c in enumerate(dictionary)};rng=random.Random(spec['seed']);buckets=defaultdict(list)
 for i,row in enumerate(train_cache['rows']):
  if i not in conflicts:buckets[row['inputWidth']].append(i)
 candidates=[];history=[];start=time.monotonic();stopped=False
 for epoch in range(1,spec['epochs']+1):
  model.eval();batches=[]
  for width,indices in sorted(buckets.items()):
   chosen=list(indices);rng.shuffle(chosen);batches.extend(chosen[i:i+spec['batchSize']] for i in range(0,len(chosen),spec['batchSize']))
  rng.shuffle(batches);values=[];completed=0
  for n,indices in enumerate(batches):
   if time.monotonic()-start>spec['maximumTrainingSeconds']:stopped=True;break
   x,teacher_feature,teacher_probability,valid=fetch(indices,train_cache,ROOT/'train-cache');logits,feature,rejected=model(x,valid);logprob=logits.log_softmax(-1);targets=[];eligible=[];lengths=[]
   for j,i in enumerate(indices):
    row=train['rows'][i]
    if row['ctcEligible']:eligible.append(j);targets.extend(index[c] for c in row['text']);lengths.append(len(row['text']))
   frames=feature.shape[1];ctc=ctc_loss(logprob[eligible].transpose(0,1),torch.tensor(targets,dtype=torch.long),torch.full((len(eligible),),frames,dtype=torch.long),torch.tensor(lengths,dtype=torch.long)) if eligible else feature.sum()*0;kl=torch.nn.functional.kl_div(logprob,teacher_probability,reduction='batchmean')/frames;mse=torch.nn.functional.mse_loss(feature,teacher_feature);bce=torch.nn.functional.binary_cross_entropy_with_logits(rejected,torch.tensor([train['rows'][i]['reject'] for i in indices],dtype=torch.float32));loss=ctc+spec['teacherKlWeight']*kl+spec['teacherFeatureMseWeight']*mse+spec['rejectBceWeight']*bce;assert torch.isfinite(loss);optimizer.zero_grad(set_to_none=True);loss.backward();torch.nn.utils.clip_grad_norm_(model.parameters(),spec['gradientClip']);optimizer.step();completed+=len(indices);values.append([float(v.detach()) for v in [ctc,kl,mse,bce]])
   if n%96==0:budget(spec);print(json.dumps({'stage':'training','epoch':epoch,'completed':completed,'total':len(train['rows'])-len(conflicts),'seconds':time.monotonic()-start,'ctcKlMseBce':values[-1],'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024}),flush=True)
  if stopped:break
  history.append({'epoch':epoch,'meanLosses':np.mean(values,axis=0).tolist(),'seconds':time.monotonic()-start})
  if epoch in spec['evaluateEpochs']:
   path=ROOT/f'epoch-{epoch}.safetensors';save_file(model.state_dict(),str(path));metrics,records=evaluate(f'epoch-{epoch}',model,dev_cache,ROOT/'development-cache',dev,dictionary,'trained');candidates.append({'name':f'epoch-{epoch}','epoch':epoch,'path':str(path),'sha256':sha(path),'metrics':metrics})
 assert model.official.config.to_dict()==baseline_config and all(torch.equal(v,model.official.state_dict()[k]) for k,v in bn_before.items());assert sha(primary)==primary_sha;base={'name':'official-baseline','epoch':0,'path':str(ASSETS/'model.safetensors'),'sha256':sha(ASSETS/'model.safetensors'),'metrics':original};eligibility=lambda m: m['cer']<=.9*original['cer'] and m['codeMetrics']['cer']<=.9*original['codeMetrics']['cer'] and m['groups']['natural']['cer']<=original['groups']['natural']['cer']+.01 and all(m['fontCodeMetrics'][f]['cer']<=original['fontCodeMetrics'][f]['cer'] for f in ['Cousine','UbuntuMono']) and m['negativeAccepted']<=original['negativeAccepted'] and m['allUnsupportedPaddingRefused'];eligible=[base]+[c for c in candidates if eligibility(c['metrics'])];selected=sorted(eligible,key=lambda c:(c['metrics']['cer'],c['metrics']['codeMetrics']['cer'],c['metrics']['groups']['natural']['cer'],-c['metrics']['exact'],c['epoch']))[0];result={'selected':selected,'officialBaseline':base,'candidates':candidates,'history':history,'trainingSeconds':time.monotonic()-start,'cacheSeconds':train_cache['seconds']+dev_cache['seconds'],'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'budgetStopped':stopped,'bnStatsAndConfigUnchanged':True,'japanesePrimaryUnchanged':True,'trainingStarted':True,'status':'Researchonly/inactive/unqualified/finalunopened'};save(ROOT/'selection.json',result);save(RESULTS/'selection.json',result);print(json.dumps({'stage':'selection','selected':selected['name'],'metrics':selected['metrics']}),flush=True)
if __name__=='__main__':main()
