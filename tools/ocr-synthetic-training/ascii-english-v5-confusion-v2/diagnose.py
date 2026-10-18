"""Post-inference descriptive audit only; no tuning or decoding/model calls."""
from collections import Counter
import json,sys
from pathlib import Path
from data import ROOT,RESULTS,save
import importlib.util
_spec=importlib.util.spec_from_file_location("prior_edit_alignment",Path(__file__).parents[1]/"ascii-foundation-ctc/diagnose.py");_module=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(_module);edits=_module.edits

def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));conditions=[('development','active-baseline')]+[('development',c['name']) for c in selection['candidates']]
 if (RESULTS/'fresh-comparison.json').exists():conditions.extend([('fresh','fresh-active'),('fresh','fresh-candidate')])
 summaries={};raw={}
 for split,name in conditions:
  oracle={r['id']:r for r in json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'))['rows']};records=[json.loads(l) for l in (RESULTS/(name+'-unjudged.jsonl')).read_text(encoding='utf-8').splitlines()];assert len(oracle)==len(records) and {r['id'] for r in records}==set(oracle);counts=Counter();wrongaccepted=[];correctreview=[];negative={};perfont={};confusablerows=0;confusableaccepted=0
  for r in records:
   o=oracle[r['id']]
   if o['selectionPositive']:
    errors=edits(o['text'],r['prediction']);counts.update(errors);stats=perfont.setdefault(o['font'],{'rows':0,'characters':0,'editErrors':0,'exact':0,'correctAccepted':0,'wrongAccepted':0,'codeCharacters':0,'codeErrors':0});stats['rows']+=1;stats['characters']+=len(o['text']);stats['editErrors']+=len(errors);stats['exact']+=not errors;stats['correctAccepted']+=not errors and r['accepted'];stats['wrongAccepted']+=bool(errors) and r['accepted']
    if o['kind'] in ['code','lookalike']:stats['codeCharacters']+=len(o['text']);stats['codeErrors']+=len(errors)
    confusion=any(a!=b and ((a in 'Il1' and b in 'Il1') or (a in 'O0' and b in 'O0')) for a,b in errors if a and b);confusablerows+=confusion;confusableaccepted+=confusion and r['accepted']
    if errors and r['accepted']:wrongaccepted.append({'id':r['id'],'minimumConfidence':r['minimumConfidence'],'rejectProbability':r['rejectProbability']})
    if not errors and not r['accepted']:correctreview.append({'id':r['id'],'lowCtc':r['minimumConfidence']<.8,'reject':r['rejectProbability']>.05,'unsupportedPadding':not r['supported']})
   else:
    stats=negative.setdefault(o['kind'],{'rows':0,'accepted':0,'nonempty':0});stats['rows']+=1;stats['accepted']+=r['accepted'];stats['nonempty']+=bool(r['prediction'])
  for f,s in perfont.items():s['cer']=s['editErrors']/s['characters'];s['codeCer']=s['codeErrors']/s['codeCharacters']
  summaries[name]={'perFont':perfont,'negativeKinds':negative,'confusableMismatchRows':confusablerows,'confusableWrongAccepted':confusableaccepted,'wrongAccepted':wrongaccepted,'correctNeedsReview':correctreview,'maximumWrongAcceptedMinimumConfidence':max((r['minimumConfidence'] for r in wrongaccepted),default=None),'editCounts':[{'gold':a,'prediction':b,'count':n} for (a,b),n in counts.most_common()]};raw[name]=(oracle,{r['id']:r for r in records})
 pairs=[]
 for before,after in [('active-baseline',c['name']) for c in selection['candidates']]+([('fresh-active','fresh-candidate')] if 'fresh-active' in raw else []):
  o,a=raw[before];_,b=raw[after];positive=[i for i in o if o[i]['selectionPositive']];gain=[i for i in positive if a[i]['prediction']!=o[i]['text'] and b[i]['prediction']==o[i]['text']];reg=[i for i in positive if a[i]['prediction']==o[i]['text'] and b[i]['prediction']!=o[i]['text']];pairs.append({'before':before,'after':after,'gainIds':gain,'regressionIds':reg,'naturalGains':[i for i in gain if o[i]['kind']=='natural'],'naturalRegressions':[i for i in reg if o[i]['kind']=='natural']})
 save(RESULTS/'diagnosis.json',{'scope':'Read-only descriptive synthetic-crop diagnostic; edit alignment is not glyph localization; no calibration, training, selection, or output repair','conditions':summaries,'paired':pairs,'actualNativeCombinedRouteNotMeasured':True,'japanesePrimaryUnchanged':True})
 print(json.dumps({'conditions':list(summaries),'pairs':[{k:v for k,v in p.items() if k in ['before','after']}|{'gains':len(p['gainIds']),'regressions':len(p['regressionIds'])} for p in pairs]}))
if __name__=='__main__':main()
