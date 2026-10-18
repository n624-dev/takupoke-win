"""Saved output edit/group diagnosis; no rerun, repair or selection change."""
from collections import Counter
import json
from data import ROOT,RESULTS,save
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-foundation-ctc'))
from diagnose import edits
def main():
 manifest=json.loads((ROOT/'development/manifest.json').read_text(encoding='utf-8'));lookup={r['id']:r for r in manifest['rows']};selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));conditions=[]
 for candidate in [selection['officialBaseline']]+selection['candidates']:
  rows=[json.loads(l) for l in (RESULTS/(candidate['name']+'-unjudged.jsonl')).read_text(encoding='utf-8').splitlines()];pairs=Counter();groups={}
  for r in rows:
   g=lookup[r['id']]
   if not g['selectionPositive']:continue
   changes=edits(g['text'],r['prediction']);pairs.update(changes);v=groups.setdefault(g['kind'],{'rows':0,'exact':0,'wrongAccepted':0});v['rows']+=1;v['exact']+=not changes;v['wrongAccepted']+=bool(changes) and r['accepted']
  conditions.append({'name':candidate['name'],'groups':groups,'mostCommonEdits':[{'gold':g,'prediction':p,'count':n} for (g,p),n in pairs.most_common(30)]})
 save(RESULTS/'edit-diagnosis.json',{'scope':'Savedraw independentdevelopment diagnosis only; no inference/correction/selection changes','conditions':conditions});print(json.dumps(conditions))
if __name__=='__main__':main()
