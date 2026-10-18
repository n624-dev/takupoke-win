"""Open independent fresh once, only after immutable full-dev selection."""
import json,resource,unicodedata
from pathlib import Path
import numpy as np,torch,yaml,onnxruntime as ort
from safetensors.torch import load_file
from data import ROOT,RESULTS,FONTS,save,generate,sha
from model import ASSETS
from adapter import Adapter
from cache import build,budget
from metrics import evaluate
from image_tensor import greedy

def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));candidate=selection['selected']
 if candidate['epoch']==0:raise SystemExit('No eligible full-development candidate; fresh remains unopened')
 assert sha(candidate['path'])==candidate['sha256'];source=Path(__file__).parent;protocol=json.loads((source/'protocol.json').read_text(encoding='utf-8'));stage=RESULTS/'fresh-freeze.json'
 if stage.exists() or (ROOT/'fresh').exists():raise RuntimeError('Fresh cannot repeat')
 assert not Path('/workspace/scratch/ascii-english-v5-japanese-fonts-v3/adaptation-v3/fresh').exists()
 active=selection['activeBaseline'];assert sha(active['path'])==active['sha256'];onnx=Path('/workspace/scratch/ocr-production-development-20261015-v1/models/ascii-rec.onnx');assert sha(onnx)==protocol['initializerAndTeacher']['activeOnnxSha256']
 save(stage,{'selectedCheckpointSha256':candidate['sha256'],'selectionSha256':sha(ROOT/'selection.json'),'protocolSha256':sha(source/'protocol.json'),'sourceSha256':sha(__file__),'fontSha256':{f:sha(FONTS[f]) for f in protocol['fresh']['fonts']},'allPositiveRowsRemainMainDenominator':True,'aliasDiagnosticAfterScoringOnly':True,'noPostFreshReselectionOrOptimization':True,'guard':'Same relative conditions as full-dev eligibility; descriptive qualification, no model selection','scope':'Synthetic crops only; parent independently qualifies C# and full documents'})
 excluded=set(json.loads((ROOT/'excluded-values.json').read_text(encoding='utf-8')))
 def walk(v):
  if isinstance(v,str) and len(v)<=256:excluded.update([v,unicodedata.normalize('NFKC',v).strip()])
  elif isinstance(v,list):
   for x in v:walk(x)
  elif isinstance(v,dict):
   for x in v.values():walk(x)
 for split in ['train','development']:
  walk(json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8')));walk(json.loads((ROOT/(split+'-cache')/'cache.json').read_text(encoding='utf-8')))
 manifest=generate(protocol,'fresh',excluded);assert len(manifest['rows'])==624;torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.manual_seed(108591);budget(protocol['training']);dictionary=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' '];baseline=Adapter().eval();baseline.load_state_dict(load_file(active['path']),strict=True);receipt=build(manifest,baseline.official,ROOT/'fresh-cache',protocol['training']);bm,br=evaluate('fresh-active',baseline,receipt,ROOT/'fresh-cache',manifest,dictionary,'trained');del baseline;model=Adapter().eval();model.load_state_dict(load_file(candidate['path']),strict=True);cm,cr=evaluate('fresh-candidate',model,receipt,ROOT/'fresh-cache',manifest,dictionary,'trained');del model
 so=ort.SessionOptions();so.intra_op_num_threads=2;so.inter_op_num_threads=1;session=ort.InferenceSession(str(onnx),sess_options=so,providers=['CPUExecutionProvider']);parity=[]
 with (RESULTS/'fresh-original-active-onnx-unjudged.jsonl').open('x',encoding='utf-8',newline='\n') as output:
  for row,item,prior in zip(manifest['rows'],receipt['rows'],br):
   with np.load(ROOT/'fresh-cache'/item['file'],allow_pickle=False) as d:x=np.array(d['input'],copy=True)
   prob,reject=session.run(None,{'x':x[None],'valid_width':np.array([row['validWidth']],dtype=np.int64)});pieces=greedy(prob[0],dictionary);text=''.join(p['Text'] for p in pieces);support=all(p['End']*row['inputWidth']<=row['validWidth']*len(prob[0]) for p in pieces);confidence=min((p['Confidence'] for p in pieces),default=0);accepted=bool(text) and confidence>=.8 and support and all(32<=ord(c)<=126 for c in text) and float(reject[0])<=.05;raw={'id':row['id'],'prediction':text,'pieces':pieces,'minimumConfidence':confidence,'rejectProbability':float(reject[0]),'supported':support,'accepted':accepted};output.write(json.dumps(raw,ensure_ascii=False)+'\n');intervals=lambda p:[(v['Text'],v['Start'],v['End']) for v in p];parity.append({'id':row['id'],'textEqual':text==prior['prediction'],'ctcIntervalsEqual':intervals(pieces)==intervals(prior['pieces']),'acceptedEqual':accepted==prior['accepted'],'paddingEqual':support==prior['supported'],'confidenceDifference':abs(confidence-prior['minimumConfidence']),'rejectDifference':abs(float(reject[0])-prior['rejectProbability'])})
 lookup={r['id']:r for r in manifest['rows']};positive=[(a,b) for a,b in zip(br,cr) if lookup[a['id']]['selectionPositive']];gains=[a['id'] for a,b in positive if a['prediction']!=lookup[a['id']]['text'] and b['prediction']==lookup[b['id']]['text']];losses=[a['id'] for a,b in positive if a['prediction']==lookup[a['id']]['text'] and b['prediction']!=lookup[b['id']]['text']];allparity=all(all(p[k] for k in ['textEqual','ctcIntervalsEqual','acceptedEqual','paddingEqual']) for p in parity)
 guards={'codeCerImproves':cm['codeMetrics']['cer']<bm['codeMetrics']['cer'],'wholeCerImproves':cm['cer']<bm['cer'],'eachJpFontEditsNonincrease':all(cm['fontMetrics'][f]['editErrors']<=bm['fontMetrics'][f]['editErrors'] for f in protocol['fresh']['jpFonts']),'naturalRetention':cm['groups']['natural']['cer']<=bm['groups']['natural']['cer']+.002,'rawHighCtcWrong':cm['reviewMetrics']['wrongHighConfidence']<=bm['reviewMetrics']['wrongHighConfidence']+1,'incorrectAccepted':cm['incorrectAccepted']<=bm['incorrectAccepted']+1,'negativeAccepted':cm['negativeAccepted']<=bm['negativeAccepted']+1,'unsupportedPaddingRefused':cm['allUnsupportedPaddingRefused'],'activeTorchOnnxDecisionsEqual':allparity}
 result={'status':'Once-only fixed fresh assessment; no further selection or training','active':bm,'candidate':cm,'gains':gains,'regressions':losses,'guards':guards,'promisingForPortableExport':all(guards.values()),'selectedSha256':candidate['sha256'],'freshManifestSha256':sha(ROOT/'fresh/manifest.json'),'activeParity':parity,'activeOriginalOnnxTorchDecisionParity':allparity,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'noFurtherTrainingOrReselection':True,'japanesePrimaryUnchanged':sha('/workspace/scratch/ocr-frozen-20261013/v6-medium.onnx')==json.loads((ROOT/'freeze.json').read_text(encoding='utf-8'))['japanesePrimarySha256'],'activated':False,'iosEdited':False};save(RESULTS/'fresh-comparison.json',result);print(json.dumps({k:v for k,v in result.items() if k not in ['active','candidate','activeParity']}),flush=True)

if __name__=='__main__':main()
