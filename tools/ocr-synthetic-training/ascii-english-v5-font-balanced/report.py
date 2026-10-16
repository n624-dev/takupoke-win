"""Read-only completed-output quality gate and FP32/checkpoint/source audits."""
import ast,hashlib,json
from pathlib import Path
import numpy as np,torch
from safetensors.torch import load_file
from data import ROOT,PARENT,RESULTS,save
def sha(p):
 h=hashlib.sha256()
 with Path(p).open('rb') as f:
  for chunk in iter(lambda:f.read(1048576),b''):h.update(chunk)
 return h.hexdigest()
def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));freeze=json.loads((ROOT/'freeze.json').read_text(encoding='utf-8'));source=Path(__file__).parent
 for name,h in freeze['sources'].items():assert sha(source/name)==h
 manifest=json.loads((ROOT/'development/manifest.json').read_text(encoding='utf-8'));lookup={r['id']:r for r in manifest['rows']};original=[json.loads(l) for l in (RESULTS/'official-baseline-unjudged.jsonl').read_text(encoding='utf-8').splitlines()];before={r['id']:r for r in original};conditions=[]
 for candidate in [selection['officialBaseline']]+selection['candidates']:
  p=RESULTS/(candidate['name']+'-unjudged.jsonl');assert sha(p)==candidate['metrics']['rawSha256'];records=[json.loads(l) for l in p.read_text(encoding='utf-8').splitlines()];positive=[r for r in records if lookup[r['id']]['selectionPositive']];exact=lambda r:r['prediction']==lookup[r['id']]['text'];wrong=[r for r in positive if r['accepted'] and not exact(r)];fonts={name:{'positive':len(group),'exact':sum(exact(r) for r in group),'correctAccepted':sum(exact(r) and r['accepted'] for r in group),'wrongAccepted':sum(not exact(r) and r['accepted'] for r in group)} for name in manifest['fonts'] for group in [[r for r in positive if lookup[r['id']]['font']==name]]};unsupported=[r for r in records if not r['supported']];conditions.append({'name':candidate['name'],'fonts':fonts,'improvementsVsBaseline':sum(not exact(before[r['id']]) and exact(r) for r in positive),'regressionsVsBaseline':sum(exact(before[r['id']]) and not exact(r) for r in positive),'maximumWrongAcceptedConfidence':max((r['minimumConfidence'] for r in wrong),default=None),'wrongAcceptedIds':[r['id'] for r in wrong],'allUnsupportedPaddingRefused':all(not r['accepted'] for r in unsupported),'unsupportedPaddingRows':len(unsupported)})
 selected=selection['selected'];m=selected['metrics'];d=next(c for c in conditions if c['name']==selected['name']);base=selection['officialBaseline']['metrics'];gate={'selectedTrained':selected['epoch']>0,'wholeCer10percentRelativeImprovement':m['cer']<=.9*base['cer'],'codeCer10percentRelativeImprovement':m['codeMetrics']['cer']<=.9*base['codeMetrics']['cer'],'naturalCerAtMost1ppRegression':m['groups']['natural']['cer']<=base['groups']['natural']['cer']+.01,'eachMonoCodeCerNoRegression':all(m['fontCodeMetrics'][f]['cer']<=base['fontCodeMetrics'][f]['cer'] for f in ['Cousine','UbuntuMono']),'negativeAcceptNotIncreased':m['negativeAccepted']<=base['negativeAccepted'],'allInsufficientPaddingSourceRefused':d['allUnsupportedPaddingRefused']};save(RESULTS/'development-diagnosis.json',{'conditions':conditions,'continuationGate':gate,'researchUsefulnessGatePassed':all(gate.values()),'automaticFinalOrPortableStage':False,'zeroWrongAcceptRequired':False,'wholeFieldOrDocumentQualification':False})
 caches=[]
 for split in ['train','development']:
  folder=ROOT/(split+'-cache');receipt=json.loads((folder/'cache.json').read_text(encoding='utf-8'))
  for row in receipt['rows']:
   p=folder/row['file'];assert sha(p)==row['npzSha256']
   with np.load(p,allow_pickle=False) as v:
    assert set(v.files)=={'input','teacher','probability'}
    for k,field in [('input','inputSha256'),('teacher','teacherSha256'),('probability','probabilitySha256')]:assert v[k].dtype==np.float32 and list(v[k].shape)==row['shape'][k] and hashlib.sha256(v[k].tobytes()).hexdigest()==row[field]
  caches.append({'split':split,'rows':len(receipt['rows']),'bytes':receipt['totalBytes'],'allRestoredFp32DtypeShapeBytesHashesVerified':True})
 initial=load_file(str(ROOT/'initial-adapter.safetensors'));bn=[k for k in initial if 'running_mean' in k or 'running_var' in k or 'num_batches_tracked' in k];normeps=[k for k in initial if k.endswith('epsilon')]
 for c in selection['candidates']:
  assert sha(c['path'])==c['sha256'];state=load_file(c['path']);assert set(state)==set(initial)
  for k in bn+normeps:assert torch.equal(initial[k],state[k])
  assert state['official.head.head.weight'].shape==(438,120) and state['official.head.head.bias'].shape==(438,)
 for p in source.glob('*.py'):
  for n in ast.walk(ast.parse(p.read_text(encoding='utf-8'))):
   if isinstance(n,ast.Call) and isinstance(n.func,ast.Attribute) and n.func.attr in ['read_text','write_text']:
    assert 'encoding' in {k.arg for k in n.keywords}
    if n.func.attr=='write_text':assert 'newline' in {k.arg for k in n.keywords}
 assert not (ROOT/'final').exists();assert selection['bnStatsAndConfigUnchanged'] and selection['japanesePrimaryUnchanged'];assert sha('/workspace/scratch/ocr-frozen-20261013/v6-medium.onnx')==freeze['japanesePrimarySha256'];init=json.loads((RESULTS/'newdev-initialization-self-consistency.json').read_text(encoding='utf-8'));assert init['exactFp32AndDecisions'];prior=Path('/workspace/scratch/takupoke-win/research-results/ocr-synthetic-training-20261013/ascii-english-v5-official-transfer-v1/handoff-manifest.json');prior_manifest=json.loads(prior.read_text(encoding='utf-8'));repo=prior.parents[3]
 for path,item in prior_manifest['files'].items():assert sha(repo/path)==item['sha256'] and (repo/path).stat().st_size==item['bytes']
 save(RESULTS/'verification.json',{'scope':'Completed-output read-only audit, no re-inference/retraining/selection changes','frozenSources':True,'rawAndCheckpointHashes':True,'cachePrecision':caches,'bnRunningStatsAndCountsFixed':{'tensors':len(bn),'allCheckpointsIdentical':True},'officialArchitectureConfigNormEpsilonPreserved':True,'full438HeadRetained':True,'newdevExactByteSelfConsistency':True,'japanesePrimaryUnchanged':True,'closedOldTransplantAllHandoffFilesUnchanged':True,'utf8LfSourceIoPassed':True,'finalDirectoryAbsent':True,'modelQualified':False});print(json.dumps({'selected':selected['name'],'metrics':m,'gate':gate,'conditions':conditions}))
if __name__=='__main__':main()
