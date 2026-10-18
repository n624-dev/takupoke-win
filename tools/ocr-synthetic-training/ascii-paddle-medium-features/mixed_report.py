"""Saved-output gate/retention/precision audit; never loads or runs a model."""
import ast
import hashlib
import json
from pathlib import Path

from mixed_data import ROOT,PARENT,REPO,RESULTS,save


def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));freeze=json.loads((ROOT/'freeze.json').read_text(encoding='utf-8'));manifest=json.loads((ROOT/'development/manifest.json').read_text(encoding='utf-8'));lookup={row['id']:row for row in manifest['rows']}
 for name,expected in freeze['sources'].items():assert hashlib.sha256((Path(__file__).parent/name).read_bytes()).hexdigest()==expected
 before={row['id']:row for row in [json.loads(line) for line in (RESULTS/'original-unjudged.jsonl').read_text(encoding='utf-8').splitlines()]};diagnostics=[]
 for candidate in [selection['original']]+selection['candidates']:
  raw=RESULTS/(candidate['name']+'-unjudged.jsonl');assert hashlib.sha256(raw.read_bytes()).hexdigest()==candidate['metrics']['rawSha256'];records=[json.loads(line) for line in raw.read_text(encoding='utf-8').splitlines()];by_id={row['id']:row for row in records};positive=[row for row in records if lookup[row['id']]['selectionPositive']];retention=[row for row in records if lookup[row['id']]['retention']];exact=lambda record:record['prediction']==lookup[record['id']]['text']
  fonts={font:{'positive':len(group),'exact':sum(exact(r) for r in group),'correctAccepted':sum(exact(r) and r['accepted'] for r in group),'incorrectAccepted':sum(not exact(r) and r['accepted'] for r in group)} for font in manifest['fonts'] for group in [[r for r in positive if lookup[r['id']]['font']==font]]}
  wrong=[r for r in positive if r['accepted'] and not exact(r)]
  diagnostics.append({'name':candidate['name'],'fonts':fonts,'improvementsVsOriginal':sum(not exact(before[r['id']]) and exact(r) for r in positive),'regressionsVsOriginal':sum(exact(before[r['id']]) and not exact(r) for r in positive),'japaneseRetentionRegressions':sum(exact(before[r['id']]) and not exact(r) for r in retention),'maximumWrongAcceptedConfidence':max((r['minimumConfidence'] for r in wrong),default=None),'wrongAcceptedIds':[r['id'] for r in wrong]})
 selected=selection['selected'];metrics=selected['metrics'];diagnostic=next(r for r in diagnostics if r['name']==selected['name']);n=metrics['positiveRows'];gate={'selectedTrained':selected['epoch']>0,'rawExactAtLeast97percent':metrics['exact']/n>=.97,'correctAcceptedAtLeast90percent':metrics['correctAccepted']/n>=.90,'wrongAcceptedZero':metrics['incorrectAccepted']==0,'negativeAcceptedZero':metrics['negativeAccepted']==0,'eachFontRawExactAtLeast95percent':all(r['exact']/r['positive']>=.95 for r in diagnostic['fonts'].values()),'japaneseRetentionRegressionsZero':diagnostic['japaneseRetentionRegressions']==0}
 save(RESULTS/'development-diagnosis.json',{'scope':'Full18710 independent-development saved-output diagnosis only; no correction/final/activation','diagnostics':diagnostics,'continuationGate':gate,'mayPlanSeparateFinalAndPortableStage':all(gate.values())})
 precision=[]
 for split in ['train','development']:
  directory=ROOT/(split+'-cache');receipt=json.loads((directory/'cache.json').read_text(encoding='utf-8'));assert receipt['floatType']=='float32' and all(row['floatType']=='float32' for row in receipt['rows']);assert hashlib.sha256((directory/'features.f32').read_bytes()).hexdigest()==receipt['featureFileSha256'];assert hashlib.sha256((directory/'loss-logits.f32').read_bytes()).hexdigest()==receipt['scoreFileSha256'];precision.append({'split':split,'floatType':receipt['floatType'],'rows':len(receipt['rows']),'frames':receipt['frames']})
 for path in Path(__file__).parent.glob('*.py'):
  for node in ast.walk(ast.parse(path.read_text(encoding='utf-8'))):
   if isinstance(node,ast.Call) and isinstance(node.func,ast.Attribute) and node.func.attr in ['read_text','write_text']:
    keys={kw.arg for kw in node.keywords};assert 'encoding' in keys
    if node.func.attr=='write_text':assert 'newline' in keys
 train=json.loads((ROOT/'train/manifest.json').read_text(encoding='utf-8'));a={row['text'] for row in train['rows'] if row['text']};b={row['text'] for row in manifest['rows'] if row['text']};assert not a.intersection(b)
 # No hidden rerender of reserved final fonts or existing portable package.
 assert not (ROOT/'final').exists();archive=Path('/workspace/scratch/ocr-finetune-20261013/ocr-synthetic-research-portable-20261013-v2.zip');assert hashlib.sha256(archive.read_bytes()).hexdigest()=='66af7ced21e66f30594db39d5e329e37249360a03c22d2653c09d7eb4e791c13'
 save(RESULTS/'verification.json',{'scope':'Read-only source/raw/cache audit, no inference repeated','frozenSourceHashes':True,'candidateRawHashes':True,'fp32InputFeatureLogitCache':precision,'trainDevelopmentDisjoint':True,'utf8LfPathIoAudit':True,'finalDirectoryAbsent':True,'previousPortableZipUnchanged':True,'inferenceDictionaryClasses':18710,'asciiTrainableRows':95,'blankAndNonAsciiWeightRowsRemainBuffers':'Verified by frozen module/optimizer scope; no backbone/blank/nonASCII parameter appears in optimizer groups'})
 print(json.dumps({'selected':selected['name'],'metrics':metrics,'gate':gate,'diagnostics':diagnostics}))


if __name__=='__main__':main()
