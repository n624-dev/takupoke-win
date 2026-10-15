"""Once-only independent fresh synthetic assessment after immutable dev selection."""
import hashlib,json,resource,time
from pathlib import Path
import numpy as np,torch,yaml,onnxruntime as ort
from safetensors.torch import load_file
from data import ROOT,PARENT,RESULTS,FONTS,save,generate
from model import ASSETS
from adapter import Adapter
import importlib.util
_spec=importlib.util.spec_from_file_location("continuation_training",Path(__file__).parent/"train.py");_module=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(_module);sha=_module.sha;cache=_module.cache;evaluate=_module.evaluate
from image_tensor import greedy

def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));candidate=selection['selected']
 if candidate['epoch']==0:raise SystemExit('No eligible development candidate; fresh remains unopened')
 assert sha(candidate['path'])==candidate['sha256'];protocol=json.loads((Path(__file__).parent/'protocol.json').read_text(encoding='utf-8'));stage=RESULTS/'fresh-freeze.json'
 if stage.exists():raise RuntimeError('Fresh stage cannot repeat')
 torch.set_num_threads(2);torch.set_num_interop_threads(1);active=selection['activeBaseline'];assert sha(active['path'])==active['sha256'];active_onnx=Path('/workspace/scratch/ocr-production-development-20261015-v1/models/ascii-rec.onnx');assert sha(active_onnx)=='9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703'
 frozen={'protocol':protocol['fresh'],'selected':{'path':candidate['path'],'sha256':candidate['sha256']},'activeCheckpointSha256':active['sha256'],'activeOnnxSha256':sha(active_onnx),'selectionSha256':sha(ROOT/'selection.json'),'sourceSha256':sha(Path(__file__)),'fontHashes':{f:sha(FONTS[f]) for f in protocol['fresh']['fonts']},'evaluationOnly':True,'noPostFreshReselectionOrTraining':True,'acceptance':'Unchanged minCTC.8/reject.05/printableASCII/sourcePadding refusal; no confidence calibration','scope':'Synthetic crop assessment, no real-world/full-document accuracy claim'};save(stage,frozen)
 excluded=set()
 def walk(v):
  if isinstance(v,str) and len(v)<=256:excluded.add(v)
  elif isinstance(v,list):
   for x in v:walk(x)
  elif isinstance(v,dict):
   for x in v.values():walk(x)
 receipts=json.loads((ROOT/'exclusion-receipt.json').read_text(encoding='utf-8'))['files']
 for item in receipts:
  p=Path(item['path']);content=p.read_text(encoding='utf-8');assert sha(p)==item['sha256']
  if p.suffix=='.jsonl':
   for line in content.splitlines():
    if line.strip():walk(json.loads(line))
  else:walk(json.loads(content))
 manual_oracle=Path('/workspace/scratch/ocr-manual-capture-20261016-v1/oracle.json')
 if manual_oracle.exists():
  walk(json.loads(manual_oracle.read_text(encoding='utf-8')));save(RESULTS/'fresh-extra-exclusion-receipt.json',{'path':str(manual_oracle),'sha256':sha(manual_oracle),'purpose':'Literal/image/hash exclusion only; no model choice or training'})
 for split in ['train','development']:
  walk(json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8')));walk(json.loads((ROOT/(split+'-cache')/'cache.json').read_text(encoding='utf-8')))
 manifest=generate(protocol,'fresh',excluded);assert len(manifest['rows'])==496
 baseline=Adapter().eval();baseline.load_state_dict(load_file(active['path']),strict=True);teacher=baseline.official;dictionary=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' '];receipt=cache(manifest,teacher,ROOT/'fresh-cache',protocol['training']);bm,br=evaluate('fresh-active',baseline,receipt,ROOT/'fresh-cache',manifest,dictionary,'trained')
 model=Adapter().eval();model.load_state_dict(load_file(candidate['path']),strict=True);cm,cr=evaluate('fresh-candidate',model,receipt,ROOT/'fresh-cache',manifest,dictionary,'trained')
 so=ort.SessionOptions();so.intra_op_num_threads=2;so.inter_op_num_threads=1;session=ort.InferenceSession(str(active_onnx),sess_options=so,providers=['CPUExecutionProvider']);parity=[]
 with (RESULTS/'fresh-original-active-onnx-unjudged.jsonl').open('x',encoding='utf-8',newline='\n') as output:
  for row,item,prior in zip(manifest['rows'],receipt['rows'],br):
   with np.load(ROOT/'fresh-cache'/item['file'],allow_pickle=False) as data:x=np.array(data['input'],copy=True)
   prob,reject=session.run(None,{'x':x[None],'valid_width':np.array([row['validWidth']],dtype=np.int64)});pieces=greedy(prob[0],dictionary);text=''.join(p['Text'] for p in pieces);support=all(p['End']*row['inputWidth']<=row['validWidth']*len(prob[0]) for p in pieces);conf=min((p['Confidence'] for p in pieces),default=0);accepted=bool(text) and conf>=.8 and support and all(32<=ord(c)<=126 for c in text) and float(reject[0])<=.05;raw={'id':row['id'],'prediction':text,'pieces':pieces,'minimumConfidence':conf,'rejectProbability':float(reject[0]),'supported':support,'accepted':accepted};output.write(json.dumps(raw,ensure_ascii=False)+'\n');ints=lambda p:[(v['Text'],v['Start'],v['End']) for v in p];parity.append({'id':row['id'],'textEqual':text==prior['prediction'],'ctcIntervalsEqual':ints(pieces)==ints(prior['pieces']),'acceptedEqual':accepted==prior['accepted'],'paddingEqual':support==prior['supported'],'confidenceDifference':abs(conf-prior['minimumConfidence']),'rejectDifference':abs(float(reject[0])-prior['rejectProbability'])})
 lookup={r['id']:r for r in manifest['rows']};positives=[(a,b) for a,b in zip(br,cr) if lookup[a['id']]['selectionPositive']];gains=[a['id'] for a,b in positives if a['prediction']!=lookup[a['id']]['text'] and b['prediction']==lookup[b['id']]['text']];regressions=[a['id'] for a,b in positives if a['prediction']==lookup[a['id']]['text'] and b['prediction']!=lookup[b['id']]['text']];allparity=all(all(r[k] for k in ['textEqual','ctcIntervalsEqual','acceptedEqual','paddingEqual']) for r in parity)
 useful=allparity and cm['cer']<bm['cer'] and cm['codeMetrics']['cer']<bm['codeMetrics']['cer'] and cm['groups']['natural']['cer']<=bm['groups']['natural']['cer']+.002 and cm['incorrectAccepted']<=bm['incorrectAccepted']+2 and cm['negativeAccepted']<=bm['negativeAccepted']+2
 result={'status':'Independent fresh synthetic comparison after frozen selection; parent decides adoption','active':bm,'candidate':cm,'gains':gains,'regressions':regressions,'activeOriginalOnnxTorchDecisionParity':allparity,'activeParityRows':len(parity),'activeParity':parity,'promisingForPortableExport':useful,'selectedSha256':candidate['sha256'],'freshManifestSha256':sha(ROOT/'fresh/manifest.json'),'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'noFurtherTrainingOrReselection':True,'japanesePrimaryUnchanged':sha('/workspace/scratch/ocr-frozen-20261013/v6-medium.onnx')==json.loads((ROOT/'freeze.json').read_text(encoding='utf-8'))['japanesePrimarySha256'],'iosEdited':False,'activated':False};save(RESULTS/'fresh-comparison.json',result);print(json.dumps({k:v for k,v in result.items() if k not in ['active','candidate','activeParity']}),flush=True)
if __name__=='__main__':main()
