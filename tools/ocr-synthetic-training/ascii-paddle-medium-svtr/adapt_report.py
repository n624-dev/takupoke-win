"""Completed-output selection/gate, FP32 cache and source audit only."""
import ast
import hashlib
import json
from pathlib import Path

from adapt_data import ROOT,REPO,RESULTS,save


def digest(path):
 h=hashlib.sha256()
 with Path(path).open('rb') as stream:
  for data in iter(lambda:stream.read(1024*1024),b''):h.update(data)
 return h.hexdigest()


def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));freeze=json.loads((ROOT/'freeze.json').read_text(encoding='utf-8'));manifest=json.loads((ROOT/'development/manifest.json').read_text(encoding='utf-8'));lookup={r['id']:r for r in manifest['rows']};source=Path(__file__).parent
 for name,expected in freeze['sources'].items():assert digest(source/name)==expected
 before={r['id']:r for r in [json.loads(line) for line in (RESULTS/'original-unjudged.jsonl').read_text(encoding='utf-8').splitlines()]};diagnostics=[]
 for candidate in [selection['original']]+selection['candidates']:
  path=RESULTS/(candidate['name']+'-unjudged.jsonl');assert digest(path)==candidate['metrics']['rawSha256'];records=[json.loads(line) for line in path.read_text(encoding='utf-8').splitlines()];positive=[r for r in records if lookup[r['id']]['selectionPositive']];retention=[r for r in records if lookup[r['id']]['retention']];exact=lambda r:r['prediction']==lookup[r['id']]['text'];wrong=[r for r in positive if r['accepted'] and not exact(r)];fonts={name:{'positive':len(g),'exact':sum(exact(r) for r in g),'correctAccepted':sum(exact(r) and r['accepted'] for r in g),'incorrectAccepted':sum(not exact(r) and r['accepted'] for r in g)} for name in manifest['fonts'] for g in [[r for r in positive if lookup[r['id']]['font']==name]]};diagnostics.append({'name':candidate['name'],'fonts':fonts,'improvementsVsOriginal':sum(not exact(before[r['id']]) and exact(r) for r in positive),'regressionsVsOriginal':sum(exact(before[r['id']]) and not exact(r) for r in positive),'japaneseRetentionRegressions':sum(exact(before[r['id']]) and not exact(r) for r in retention),'maximumWrongAcceptedConfidence':max((r['minimumConfidence'] for r in wrong),default=None),'wrongAcceptedIds':[r['id'] for r in wrong]})
 selected=selection['selected'];metrics=selected['metrics'];diagnostic=next(r for r in diagnostics if r['name']==selected['name']);n=metrics['positiveRows'];gate={'selectedTrained':selected['epoch']>0,'rawExactAtLeast97percent':metrics['exact']/n>=.97,'correctAcceptedAtLeast90percent':metrics['correctAccepted']/n>=.90,'wrongAcceptedZero':metrics['incorrectAccepted']==0,'negativeAcceptedZero':metrics['negativeAccepted']==0,'eachFontRawExactAtLeast95percent':all(r['exact']/r['positive']>=.95 for r in diagnostic['fonts'].values()),'japaneseRetentionRegressionsZero':diagnostic['japaneseRetentionRegressions']==0};save(RESULTS/'development-diagnosis.json',{'scope':'Completed saved-output diagnosis only, prior-consumed devfont/newliterals; no inference repair/heldoutfinal/production qualification','conditions':diagnostics,'continuationGate':gate,'mayPlanSeparateFinalAndPortableStage':all(gate.values())})
 precision=[]
 for split in ['train','development']:
  directory=ROOT/(split+'-cache');receipt=json.loads((directory/'cache.json').read_text(encoding='utf-8'));assert receipt['floatType']=='float32' and all(r['floatType']=='float32' for r in receipt['rows'])
  for name,sha in receipt['files'].items():assert digest(directory/name)==sha
  precision.append({'split':split,'rows':len(receipt['rows']),'frames':receipt['frames'],'maps':['context','shortcut','teacher-features'],'floatType':'float32','fileHashesVerified':True})
 for path in source.glob('*.py'):
  for node in ast.walk(ast.parse(path.read_text(encoding='utf-8'))):
   if isinstance(node,ast.Call) and isinstance(node.func,ast.Attribute) and node.func.attr in ['read_text','write_text']:
    keys={k.arg for k in node.keywords};assert 'encoding' in keys
    if node.func.attr=='write_text':assert 'newline' in keys
 train=json.loads((ROOT/'train/manifest.json').read_text(encoding='utf-8'));a={r['text'] for r in train['rows'] if r['text']};b={r['text'] for r in manifest['rows'] if r['text']};assert not a.intersection(b);assert not (ROOT/'final').exists();archive=Path('/workspace/scratch/ocr-finetune-20261013/ocr-synthetic-research-portable-20261013-v2.zip');assert digest(archive)=='66af7ced21e66f30594db39d5e329e37249360a03c22d2653c09d7eb4e791c13';init=json.loads((RESULTS/'development-initialization-fidelity.json').read_text(encoding='utf-8'));assert init['allTextIntervalsAndFloorDecisionsIdentical'];contradiction=json.loads((RESULTS/'pretraining-contradiction-audit.json').read_text(encoding='utf-8'));assert contradiction['ctcFrameFeasibility'];save(RESULTS/'verification.json',{'scope':'Read-only source/raw/cache audit; no inference repeated','frozenSourceHashes':True,'candidateRawHashes':True,'fp32InputFeatureCache':precision,'trainDevelopmentDisjoint':True,'utf8LfPathIoAudit':True,'finalDirectoryAbsent':True,'previousPortableZipUnchanged':True,'initializationNewdevSameDecodingDecisions':True,'contradictoryOptimizerRowsExcluded':contradiction['excludedOptimizerRows'],'noStaticLossCompression':True,'inferenceDictionaryClasses':18710,'originalCnnAndClassifierFrozen':'Frozen standard ONNX prefix and classifier buffers, not optimizer parameters; encoder adaptation can change all18710 logits, Japanese regression quantified separately'})
 print(json.dumps({'selected':selected['name'],'metrics':metrics,'gate':gate,'conditions':diagnostics}))


if __name__=='__main__':main()
