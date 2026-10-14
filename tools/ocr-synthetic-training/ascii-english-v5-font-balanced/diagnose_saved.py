"""Postselection saved raw diagnostics: errors/review burden, no model calls."""
from collections import Counter
import json,sys
from pathlib import Path
from data import ROOT,RESULTS,save
sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-foundation-ctc'))
from diagnose import edits

def main():
 manifest=json.loads((ROOT/'development/manifest.json').read_text(encoding='utf-8'));lookup={r['id']:r for r in manifest['rows']};selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));original=[json.loads(l) for l in (RESULTS/'official-baseline-unjudged.jsonl').read_text(encoding='utf-8').splitlines()];before={r['id']:r for r in original};conditions=[]
 for c in [selection['officialBaseline']]+selection['candidates']:
  rows=[json.loads(l) for l in (RESULTS/(c['name']+'-unjudged.jsonl')).read_text(encoding='utf-8').splitlines()];pairs=Counter();by_kind={};by_font={};neg={}
  for r in rows:
   g=lookup[r['id']]
   if not g['selectionPositive']:
    v=neg.setdefault(g['kind'],{'rows':0,'accepted':0,'nonempty':0});v['rows']+=1;v['accepted']+=r['accepted'];v['nonempty']+=bool(r['prediction']);continue
   changes=edits(g['text'],r['prediction']);pairs.update(changes);exact=not changes;baseexact=before[r['id']]['prediction']==g['text']
   for d,k in [(by_kind,g['kind']),(by_font,g['font'])]:
    v=d.setdefault(k,{'rows':0,'characters':0,'editErrors':0,'exact':0,'improvedLines':0,'regressedLines':0,'acceptedCorrect':0,'acceptedWrong':0,'reviewRequired':0,'reviewRequiredCorrect':0,'correctLowCtc':0,'wrongHighCtc':0,'maximumWrongAcceptedConfidence':None})
    v['rows']+=1;v['characters']+=len(g['text']);v['editErrors']+=len(changes);v['exact']+=exact;v['improvedLines']+=exact and not baseexact;v['regressedLines']+=not exact and baseexact;v['acceptedCorrect']+=r['accepted'] and exact;v['acceptedWrong']+=r['accepted'] and not exact;v['reviewRequired']+=not r['accepted'];v['reviewRequiredCorrect']+=not r['accepted'] and exact;v['correctLowCtc']+=exact and r['minimumConfidence']<.8;v['wrongHighCtc']+=not exact and r['minimumConfidence']>=.8
    if r['accepted'] and not exact:v['maximumWrongAcceptedConfidence']=max(v['maximumWrongAcceptedConfidence'] or 0,r['minimumConfidence'])
  for d in [by_kind,by_font]:
   for v in d.values():v['cer']=v['editErrors']/v['characters'];v['lineErrorRate']=1-v['exact']/v['rows']
  conditions.append({'name':c['name'],'groups':by_kind,'fonts':by_font,'negatives':neg,'mostCommonEdits':[{'gold':g,'prediction':p,'count':n} for (g,p),n in pairs.most_common(30)]})
 save(RESULTS/'practical-error-review-diagnosis.json',{'scope':'Fresh development only; savedraw no modelcalls/corrections/selection changes. Review counts from frozen diagnostic .8CTC/.05reject threshold are uncalibrated; actual user correction time/full-document burden not measured. Wrongacceptzero not required. Different fonts use distinct literals, so font gaps are observational not matched-text causal contrasts.','conditions':conditions})
 print(json.dumps({'conditions':[{'name':c['name'],'groups':c['groups'],'negativeAccepted':sum(v['accepted'] for v in c['negatives'].values())} for c in conditions]}))
if __name__=='__main__':main()
