"""Correct negative subgroup formatting from immutable raw, not model/selection."""
import json,hashlib
from data import ROOT,RESULTS,save

def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));manifest=json.loads((ROOT/'development/manifest.json').read_text(encoding='utf-8'));gold={r['id']:r for r in manifest['rows']};conditions=[]
 for c in [selection['officialBaseline']]+selection['candidates']:
  path=RESULTS/(c['name']+'-unjudged.jsonl');rows=[json.loads(l) for l in path.read_text(encoding='utf-8').splitlines()];counts={}
  for r in rows:
   g=gold[r['id']]
   if g['reject']:v=counts.setdefault(g['kind'],{'rows':0,'accepted':0,'nonempty':0});v['rows']+=1;v['accepted']+=r['accepted'];v['nonempty']+=bool(r['prediction'])
  assert sum(v['accepted'] for v in counts.values())==c['metrics']['negativeAccepted'];conditions.append({'name':c['name'],'canonicalNegativeGroupsFromRaw':counts,'overallNegativeAcceptedUnchanged':c['metrics']['negativeAccepted'],'rawSha256':hashlib.sha256(path.read_bytes()).hexdigest()})
 save(RESULTS/'negative-group-summary-metadata-addendum.json',{'scope':'Read-only formatting correction from immutable outputs. Frozen evaluate() positive-only characterize update overwrote summary.groups[negative].accepted with0. Overall negativeAccepted and every raw.accepted remain correct; selection uses overall, so no selection/inference/training change. Original frozen source/summary/raw/protocol retained. Use canonical groups here or practical-error-review-diagnosis for negative subgroups.','conditions':conditions,'modelRerun':False,'selectionChanged':False})
 print(json.dumps(conditions))
if __name__=='__main__':main()
