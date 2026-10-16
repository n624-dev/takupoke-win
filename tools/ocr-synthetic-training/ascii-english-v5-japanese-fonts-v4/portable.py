"""New-model standard ONNX FP32 export after frozen development selection."""
import contextlib,hashlib,json,resource,shutil,time,warnings
from pathlib import Path
import numpy as np,torch,yaml,onnx,onnxruntime as ort
from safetensors.torch import load_file
from data import ROOT,PARENT,RESULTS,save
from adapter import Adapter
from model import ASSETS,feature_logits
from image_tensor import greedy

class Portable(torch.nn.Module):
 def __init__(self,adapter):super().__init__();self.adapter=adapter
 def forward(self,x,valid_width):
  feature,logits=feature_logits(self.adapter.official,x);steps=torch.arange(feature.shape[1],dtype=torch.int64,device=x.device)
  mask=(2*steps[None,:]+1)*x.shape[-1]<(2*feature.shape[1])*valid_width[:,None]
  rejected=self.adapter.reject((feature*mask[...,None]).sum(1)/mask.sum(1,keepdim=True).clamp_min(1)).squeeze(-1).sigmoid()
  return logits.softmax(-1),rejected

def sha(p):
 h=hashlib.sha256()
 with Path(p).open('rb') as f:
  for b in iter(lambda:f.read(1048576),b''):h.update(b)
 return h.hexdigest()
def observation(prob,reject,width,valid,dictionary):
 pieces=greedy(prob,dictionary);text=''.join(p['Text'] for p in pieces);minimum=min((p['Confidence'] for p in pieces),default=0);supported=all(p['End']*width<=valid*len(prob) for p in pieces);domain=all(32<=ord(c)<=126 for c in text)
 return {'prediction':text,'pieces':pieces,'minimumConfidence':minimum,'rejectProbability':float(reject),'supported':supported,'printableAscii':domain,'accepted':bool(text) and minimum>=.8 and supported and domain and float(reject)<=.05,'inputWidth':width,'validWidth':valid,'timeSteps':len(prob)}
def compare(a,b):
 intervals=lambda r:[(p['Text'],p['Start'],p['End']) for p in r['pieces']]
 return {'textEqual':a['prediction']==b['prediction'],'ctcIntervalsEqual':intervals(a)==intervals(b),'paddingSupportedEqual':a['supported']==b['supported'],'acceptedEqual':a['accepted']==b['accepted'],'minimumConfidenceAbsDifference':abs(a['minimumConfidence']-b['minimumConfidence']),'rejectAbsDifference':abs(a['rejectProbability']-b['rejectProbability']),'maximumAlignedPieceConfidenceAbsDifference':max((abs(x['Confidence']-y['Confidence']) for x,y in zip(a['pieces'],b['pieces'])),default=0) if intervals(a)==intervals(b) else None}
def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));decision=json.loads((RESULTS/'fresh-comparison.json').read_text(encoding='utf-8'));assert decision['promisingForPortableExport'];c=selection['selected'];assert c['sha256']==decision['selectedSha256'];assert c['epoch']>0 and sha(c['path'])==c['sha256'];out=PARENT/'portable-v4';r=RESULTS/'portable-v4';out.mkdir();r.mkdir();torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.manual_seed(108611);dictionary=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' '];assert len(dictionary)==438;save(out/'dictionary.json',dictionary);save(r/'dictionary.json',dictionary)
 protocol={'namespace':'ascii-english-v5-japanese-fonts-v4/portable-v4','basis':'Selected continuation of active font-balanced epoch4; officialHF architecture/full438; no training or quality data change during export','selectedCheckpoint':c['path'],'selectedCheckpointSha256':c['sha256'],'selectionSha256':sha(ROOT/'selection.json'),'dictionarySha256':sha(out/'dictionary.json'),'numericSeed':108611,'numericShapes':[[b,w] for b in [1,2] for w in [320,321,481,500,640,960]],'numericInputs':'Random finite normalizedFP32; batch2 secondrow normalizedzero rightpadding validWidth=W-17. No gold/quality credit.','developmentRows':624,'developmentPrecision':'Lossless previouslyfrozen allFP32originalpixels from cache; no FP16/newresize/crop/oracle routing','inputs':{'x':'FLOAT32[B,3,48,W], BGR, W320..960','valid_width':'INT64[B], integer1..W'},'outputs':{'probabilities':'FLOAT32[B,T,438] fullCTCsoftmax','reject_probability':'FLOAT32[B] sigmoid; uncalibrated'},'mask':'(2*t+1)*inputWidth < 2*T*validWidth, exact integer originalpixel-center inequality','onnxOpset':17,'allowedNodeDomains':['','ai.onnx'],'ortProvider':'CPUExecutionProvider','ortIntraThreads':2,'ortInterThreads':1,'maximumRssMiB':4096,'maximumStageSeconds':1800,'maximumOwnScratchGiB':1.3,'minimumFreeGiB':1,'fidelityAssessment':'Numeric finite/shape/domain checks mandatory; record allprob/reject absolute differences. Compare every fixeddev and numeric raw text/interval/confidence/padding/acceptance; any mismatch retained as limitation, no repair or gate relaxation. Fixeddev Torchwrapper must reproduce priorselected Torchraw decisions/intervals and confidence exactly. ONNX not qualification/activation evenifallmatch.','freshOpened':True,'fullDocumentQualified':False,'iosEdited':False,'coreml':'Not attempted; Linux conversion is not iPhone validation','sourceSha256':sha(Path(__file__))};save(r/'protocol.json',protocol);save(out/'protocol.json',protocol);start=time.monotonic()
 def budget():
  assert time.monotonic()-start<1800 and resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024<4096 and shutil.disk_usage(PARENT).free>=1024**3
  assert sum(p.stat().st_size for p in PARENT.rglob('*') if p.is_file())<=1.3*1024**3
 adapter=Adapter().eval();adapter.load_state_dict(load_file(c['path']),strict=True);before={k:v.clone() for k,v in adapter.state_dict().items()};model=Portable(adapter).eval();sample=torch.zeros(1,3,48,320);vw=torch.tensor([320],dtype=torch.int64);path=out/'model.onnx'
 with warnings.catch_warnings(record=True) as observed:
  warnings.simplefilter('always');torch.onnx.export(model,(sample,vw),str(path),input_names=['x','valid_width'],output_names=['probabilities','reject_probability'],dynamic_axes={'x':{0:'batch',3:'input_width'},'valid_width':{0:'batch'},'probabilities':{0:'batch',1:'time_steps'},'reject_probability':{0:'batch'}},opset_version=17,dynamo=False,do_constant_folding=True,external_data=False)
 save(r/'export-warnings.json',[{'category':w.category.__name__,'message':str(w.message)} for w in observed]);graph=onnx.load(path);onnx.checker.check_model(graph);domains=sorted({n.domain for n in graph.graph.node});assert set(domains)<=set(protocol['allowedNodeDomains']);assert all(i.data_type==onnx.TensorProto.FLOAT or i.data_type in [onnx.TensorProto.INT64,onnx.TensorProto.INT32,onnx.TensorProto.BOOL] for i in graph.graph.initializer);so=ort.SessionOptions();so.intra_op_num_threads=2;so.inter_op_num_threads=1;session=ort.InferenceSession(str(path),sess_options=so,providers=['CPUExecutionProvider']);assert [(i.name,i.type) for i in session.get_inputs()]==[('x','tensor(float)'),('valid_width','tensor(int64)')];budget();numeric=[]
 with (r/'numeric-unjudged.jsonl').open('x',encoding='utf-8',newline='\n') as log,torch.inference_mode():
  for b,w in protocol['numericShapes']:
   x=torch.randn(b,3,48,w).clamp(-1,1);valid=torch.full((b,),w,dtype=torch.int64)
   if b==2:valid[1]=w-17;x[1,:,:,w-17:]=0
   tp,tr=model(x,valid);op,orr=session.run(None,{'x':x.numpy(),'valid_width':valid.numpy()});assert op.dtype==orr.dtype==np.float32 and op.shape==tuple(tp.shape) and orr.shape==tuple(tr.shape) and op.shape[-1]==438 and np.isfinite(op).all() and np.isfinite(orr).all();diff=float(np.max(np.abs(op-tp.numpy())));rdiff=float(np.max(np.abs(orr-tr.numpy())));rows=[]
   for j in range(b):
    a=observation(tp[j].numpy(),tr[j],w,int(valid[j]),dictionary);z=observation(op[j],orr[j],w,int(valid[j]),dictionary);v={'batch':b,'width':w,'row':j,'torch':a,'onnx':z,'comparison':compare(a,z)};log.write(json.dumps(v,ensure_ascii=False)+'\n');rows.append(v['comparison'])
   numeric.append({'batch':b,'width':w,'timeSteps':op.shape[1],'maximumProbabilityAbsDifference':diff,'maximumRejectAbsDifference':rdiff,'rows':rows});budget()
 manifest=json.loads((ROOT/'development/manifest.json').read_text(encoding='utf-8'));cache=json.loads((ROOT/'development-cache/cache.json').read_text(encoding='utf-8'));prior={v['id']:v for v in [json.loads(l) for l in (RESULTS/(c['name']+'-unjudged.jsonl')).read_text(encoding='utf-8').splitlines()]};comparisons=[];maximum={'probability':0.,'reject':0.,'confidence':0.};mask_equal=True
 with (r/'torch-unjudged.jsonl').open('x',encoding='utf-8',newline='\n') as tlog,(r/'onnx-unjudged.jsonl').open('x',encoding='utf-8',newline='\n') as olog,torch.inference_mode():
  for row,item in zip(manifest['rows'],cache['rows']):
   p=ROOT/'development-cache'/item['file'];assert sha(p)==item['npzSha256']
   with np.load(p,allow_pickle=False) as value:x=np.array(value['input'],copy=True)
   assert x.dtype==np.float32 and hashlib.sha256(x.tobytes()).hexdigest()==item['inputSha256'];valid=np.array([row['validWidth']],dtype=np.int64);tp,tr=model(torch.from_numpy(x[None]),torch.from_numpy(valid));op,orr=session.run(None,{'x':x[None],'valid_width':valid});a=observation(tp[0].numpy(),tr[0],x.shape[-1],int(valid[0]),dictionary);z=observation(op[0],orr[0],x.shape[-1],int(valid[0]),dictionary);same=compare(prior[row['id']],a);assert all(same[k] for k in ['textEqual','ctcIntervalsEqual','paddingSupportedEqual','acceptedEqual']) and same['minimumConfidenceAbsDifference']==same['rejectAbsDifference']==same['maximumAlignedPieceConfidenceAbsDifference']==0
   mask_numpy=(np.arange(tp.shape[1])+.5)*x.shape[-1]/tp.shape[1]<valid[0];mask_integer=(2*np.arange(tp.shape[1])+1)*x.shape[-1]<2*tp.shape[1]*valid[0];assert np.array_equal(mask_numpy,mask_integer);tpair={'id':row['id'],**a};opair={'id':row['id'],**z};tlog.write(json.dumps(tpair,ensure_ascii=False)+'\n');olog.write(json.dumps(opair,ensure_ascii=False)+'\n');comparison=compare(a,z);comparisons.append({'id':row['id'],**comparison});maximum['probability']=max(maximum['probability'],float(np.max(np.abs(op-tp.numpy()))));maximum['reject']=max(maximum['reject'],comparison['rejectAbsDifference']);maximum['confidence']=max(maximum['confidence'],comparison['minimumConfidenceAbsDifference']);budget()
 assert all(torch.equal(v,adapter.state_dict()[k]) for k,v in before.items());equal={key:sum(v[key] for v in comparisons) for key in ['textEqual','ctcIntervalsEqual','paddingSupportedEqual','acceptedEqual']};summary={'status':'Inactive new-model ONNX export; no final/document/iPhone qualification','onnxBytes':path.stat().st_size,'onnxSha256':sha(path),'opset':17,'domains':domains,'numericShapes':numeric,'developmentRows':len(comparisons),'developmentEqualCounts':equal,'allDevelopmentTextIntervalPaddingAcceptEqual':all(n==len(comparisons) for n in equal.values()),'maximumDevelopmentAbsDifferences':maximum,'priorSelectedTorchRawReproducedExactly':True,'numpyHostVsIntegerFrameCenterMaskAllEqual':True,'checkpointWeightsUnchanged':True,'stageSeconds':time.monotonic()-start,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'dictionarySha256':sha(out/'dictionary.json'),'torchRawSha256':sha(r/'torch-unjudged.jsonl'),'onnxRawSha256':sha(r/'onnx-unjudged.jsonl'),'numericRawSha256':sha(r/'numeric-unjudged.jsonl'),'onnxOutsideGit':str(path),'coremlAttempted':False,'iosRepoEdited':False,'activated':False};save(r/'comparisons.json',comparisons);save(r/'summary.json',summary);print(json.dumps(summary))
if __name__=='__main__':main()
