"""Post-selection descriptive reporting only: all gains, regressions, confidence and font effects."""
from collections import Counter
import json,sys
from pathlib import Path
from data import ROOT,RESULTS,save
sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-foundation-ctc'))
from diagnose import edits

def read(name):return [json.loads(line) for line in (RESULTS/(name+'-unjudged.jsonl')).read_text(encoding='utf-8').splitlines()]
def comparison(split,left,right):
 m=json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'));lookup={r['id']:r for r in m['rows']};aa=read(left);bb=read(right);gains=[];regressions=[];changes=[];confusions={}
 for r in [left,right]:
  confusion=Counter()
  for item in read(r):
   gold=lookup[item['id']]
   if gold['selectionPositive']:
    for a,b in edits(gold['text'],item['prediction']):confusion[(a,b)]+=1
  confusions[r]=[{'source':a,'prediction':b,'count':n} for (a,b),n in confusion.most_common()]
 for a,b in zip(aa,bb):
  assert a['id']==b['id'];gold=lookup[a['id']]
  if not gold['selectionPositive']:continue
  item={'id':a['id'],'font':gold['font'],'kind':gold['kind'],'gold':gold['text'],'active':a['prediction'],'candidate':b['prediction'],'activeMinimumConfidence':a['minimumConfidence'],'candidateMinimumConfidence':b['minimumConfidence'],'activeReject':a['rejectProbability'],'candidateReject':b['rejectProbability'],'activeAccepted':a['accepted'],'candidateAccepted':b['accepted']}
  if a['prediction']!=b['prediction']:changes.append(item)
  if a['prediction']!=gold['text'] and b['prediction']==gold['text']:gains.append(item)
  elif a['prediction']==gold['text'] and b['prediction']!=gold['text']:regressions.append(item)
 return {'split':split,'left':left,'right':right,'gains':gains,'regressions':regressions,'allChangedPositivePredictions':changes,'editConfusions':confusions,'noFurtherSelectionOrOptimization':True,'confidenceUncalibrated':True}
def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));c=selection['selected']
 if c['epoch']>0:
  save(RESULTS/'development-diagnosis.json',comparison('development','active-baseline',c['name']))
  if (RESULTS/'fresh-candidate-unjudged.jsonl').exists():save(RESULTS/'fresh-diagnosis.json',comparison('fresh','fresh-active','fresh-candidate'))
  if (RESULTS/'distortion-candidate-unjudged.jsonl').exists():save(RESULTS/'distortion-diagnosis.json',comparison('distortion','distortion-active','distortion-candidate'))
 else:
  diagnostic=next(x for x in selection['candidates'] if x['epoch']==3)
  save(RESULTS/'failed-final-development-diagnosis.json',comparison('development','active-baseline',diagnostic['name']))
 if (RESULTS/'trainfit-candidate-unjudged.jsonl').exists():save(RESULTS/'trainfit-diagnosis.json',comparison('train','trainfit-active','trainfit-candidate'))
 if (RESULTS/'distortion-candidate-unjudged.jsonl').exists():save(RESULTS/'distortion-diagnosis.json',comparison('distortion','distortion-active','distortion-candidate'))
 print(json.dumps({'selected':c['name'],'descriptiveOnly':True}))
if __name__=='__main__':main()
