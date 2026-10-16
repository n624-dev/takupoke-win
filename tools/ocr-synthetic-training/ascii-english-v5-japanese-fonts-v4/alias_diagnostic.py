"""After scoring, explain physical aliases without excluding evaluation rows."""
from collections import Counter,defaultdict
import hashlib,json
from pathlib import Path
import numpy as np
from data import ROOT,RESULTS,save,sha,prepared,alternatives
from metrics import edits

def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));protocol=json.loads((Path(__file__).parent/'protocol.json').read_text(encoding='utf-8'));selected=selection['selected'];splits=['development']
 if selected['epoch']>0:
  assert (RESULTS/'fresh-comparison.json').exists();splits.append('fresh')
 assert not (RESULTS/'alias-diagnostic.json').exists();save(RESULTS/'alias-diagnostic-freeze.json',{'sourceSha256':sha(__file__),'selectionSha256':sha(ROOT/'selection.json'),'splitNames':splits,'modelScoringAlreadyClosed':True,'allOriginalPositiveRowsRemainMainDenominator':True,'noSelectionOrOptimization':True})
 reports={}
 for split in splits:
  manifest=json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'));positive=[r for r in manifest['rows'] if r['selectionPositive']];jp=[r for r in positive if r['font'] in protocol[split]['jpFonts']];counterfactual=[];alias_ids=set()
  for row in jp:
   base=prepared(row['text'],row['font'],row['renderSeed'],protocol['sourceRendering']);assert hashlib.sha256(base[3].tobytes()).hexdigest()==row['inputSha256']
   for alternate in alternatives(row['text']):
    try:other=prepared(alternate,row['font'],row['renderSeed'],protocol['sourceRendering'])
    except ValueError:continue
    if hashlib.sha256(other[3].tobytes()).hexdigest()==row['inputSha256']:
     alias_ids.add(row['id']);counterfactual.append({'id':row['id'],'target':row['text'],'alternative':alternate,'font':row['font'],'inputSha256':row['inputSha256'],'originalRasterIdentical':base[2]['originalRgbSha256']==other[2]['originalRgbSha256'],'framedRasterIdentical':np.array_equal(np.asarray(base[0]),np.asarray(other[0])),'advanceOriginal':base[1]['advance'],'advanceAlternative':other[1]['advance'],'allOriginalInkRetained':True})
  groups=defaultdict(list)
  for row in positive:groups[row['inputSha256']].append(row)
  contradictions=[];max_exact=0
  for h,rows in groups.items():
   counts=Counter(r['text'] for r in rows);max_exact+=max(counts.values())
   if len(counts)>1:contradictions.append({'inputSha256':h,'ids':[r['id'] for r in rows],'targets':dict(counts),'maximumPossibleExactRows':max(counts.values())});alias_ids.update(r['id'] for r in rows)
  names=['active-baseline']+[v['name'] for v in selection['candidates']] if split=='development' else ['fresh-active','fresh-candidate'];scores={};lookup={r['id']:r for r in manifest['rows']}
  def characterize(records):
   chars=sum(len(lookup[r['id']]['text']) for r in records);errors=sum(len(edits(lookup[r['id']]['text'],r['prediction'])) for r in records)
   return {'rows':len(records),'exact':sum(r['prediction']==lookup[r['id']]['text'] for r in records),'characters':chars,'editErrors':errors,'cer':errors/chars if chars else None,'wrongHighCtc':sum(r['prediction']!=lookup[r['id']]['text'] and r['minimumConfidence']>=.8 for r in records),'incorrectAccepted':sum(r['prediction']!=lookup[r['id']]['text'] and r['accepted'] for r in records)}
  for name in names:
   records=[json.loads(l) for l in (RESULTS/(name+'-unjudged.jsonl')).read_text(encoding='utf-8').splitlines()];records=[r for r in records if lookup[r['id']]['selectionPositive']];scores[name]={'all':characterize(records),'counterfactualOrObservedAlias':characterize([r for r in records if r['id'] in alias_ids]),'remaining':characterize([r for r in records if r['id'] not in alias_ids])}
  reports[split]={'positiveRows':len(positive),'jpRows':len(jp),'counterfactualAliases':counterfactual,'aliasRows':len(alias_ids),'observedContradictoryGroups':contradictions,'observedInputLabelExactUpperBoundRows':max_exact,'observedInputLabelExactUpperBoundFraction':max_exact/len(positive),'upperBoundScope':'Only actual generated positive labels sharing identical normalized input; counterfactual missing labels do not reduce denominator or assert impossibility','allModelScores':scores,'removedFromMainEvaluationRows':0}
 save(RESULTS/'alias-diagnostic.json',{'splits':reports,'noSelectionChange':True,'noModelExecution':True,'allRowsRetained':True,'scope':'Post-scoring explanation, no font-domain impossibility claim'});print(json.dumps({s:{k:v for k,v in r.items() if k not in ['counterfactualAliases','observedContradictoryGroups','allModelScores']} for s,r in reports.items()}),flush=True)

if __name__=='__main__':main()
