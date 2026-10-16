"""Reviewable full failure ledger and fixed-trial report, never a selector."""
from collections import Counter,defaultdict
import json,shutil
from pathlib import Path
from data import ROOT,PARENT,RESULTS,save,sha
from metrics import edits

def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));assert (RESULTS/'alias-diagnostic.json').exists();assert (RESULTS/'trainfit-comparison.json').exists();selected=selection['selected'];fresh_path=RESULTS/'fresh-comparison.json';fresh=json.loads(fresh_path.read_text(encoding='utf-8')) if fresh_path.exists() else None;names={'development':['active-baseline']+[r['name'] for r in selection['candidates']],'train':['trainfit-active','trainfit-candidate']}
 if fresh:names['fresh']=['fresh-active','fresh-candidate']
 balance={}
 for split in names:
  manifest=json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'));units=defaultdict(list);charsets={}
  for row in manifest['rows']:
   if row['kind']=='lookalike':units[row['unitId']].append(row['text'])
  checked=Counter()
  for values in units.values():
   assert len({len(v) for v in values})==1;positions=[i for i in range(len(values[0])) if len({v[i] for v in values})>1];assert len(positions)==1;chars={v[positions[0]] for v in values};assert chars in [set('Il1'),set('O0')];assert len(values)==len(chars);checked['Il1' if chars==set('Il1') else 'O0']+=1
  for scope in ['code','allPositive']:
   counts=Counter(''.join(r['text'] for r in manifest['rows'] if r['selectionPositive'] and (scope=='allPositive' or r['kind'] in ['code','lookalike'])));charsets[scope]={ch:counts[ch] for ch in 'Il1O0'}
  balance[split]={'completeBalancedMatchedContextUnits':dict(checked),'allUnitsBalancedAtVaryingPosition':True,'wholeCorpusAmbiguousCharacterCounts':charsets,'interpretation':'Matched varying position is exactly balanced; arbitrary surrounding context and natural English are not artificially forced to equal class frequencies. Descriptive only, no causal prior claim or selection change.'}
 save(RESULTS/'pair-balance-diagnostic.json',balance)
 path=RESULTS/'all-failures.jsonl';assert not path.exists();confusions={};counts={};paired={}
 with path.open('x',encoding='utf-8',newline='\n') as output:
  for split,models in names.items():
   manifest=json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'));lookup={r['id']:r for r in manifest['rows']}
   for name in models:
    records=[json.loads(l) for l in (RESULTS/(name+'-unjudged.jsonl')).read_text(encoding='utf-8').splitlines()];assert {r['id'] for r in records}==set(lookup);counter=Counter();failures=0
    for r in records:
     row=lookup[r['id']];wrong=row['selectionPositive'] and r['prediction']!=row['text'];false_accept=row['reject'] and r['accepted'];positive_rejected=row['selectionPositive'] and not r['accepted']
     if row['selectionPositive']:counter.update(edits(row['text'],r['prediction']))
     if wrong or false_accept or positive_rejected:
      output.write(json.dumps({'split':split,'model':name,'target':row['text'],'renderedLiteral':row['renderedLiteral'],'font':row['font'],'kind':row['kind'],'positiveMisread':wrong,'positiveRejected':positive_rejected,'negativeFalseAccept':false_accept,'pngSha256':row['pngSha256'],'inputSha256':row['inputSha256'],'unitId':row['unitId'],'raw':r},ensure_ascii=False)+'\n');failures+=1
    confusions[name]=[{'target':a,'prediction':b,'count':n} for (a,b),n in counter.most_common()];counts[name]=failures;raw_lookup={r['id']:r for r in records};by_unit=defaultdict(list)
    for row in manifest['rows']:
     if row['kind']=='lookalike':by_unit[row['unitId']].append(row)
    groups={}
    for mode,size in [('Il1',3),('O0',2)]:
     units=[u for u in by_unit.values() if len(u)==size];groups[mode]={'units':len(units),'allMembersCorrect':sum(all(raw_lookup[r['id']]['prediction']==r['text'] for r in u) for u in units),'allMembersCorrectAndAccepted':sum(all(raw_lookup[r['id']]['prediction']==r['text'] and raw_lookup[r['id']]['accepted'] for r in u) for u in units),'unitsWithPhysicalInputAliases':sum(len({r['inputSha256'] for r in u})<len(u) for u in units),'unitsWithDistinctInputsButPredictionCollision':sum(len({r['inputSha256'] for r in u})==len(u) and len({raw_lookup[r['id']]['prediction'] for r in u})<len(u) for u in units)}
    paired[name]={'split':split,'sameContextBalancedUnits':groups,'allMembersRetained':True,'noSelectionOrModelExecution':True}
 save(RESULTS/'confusions.json',confusions);save(RESULTS/'paired-unit-diagnostic.json',paired);save(RESULTS/'all-failures-receipt.json',{'sha256':sha(path),'counts':counts,'allScoredModelsAndAllMisreadPositivesOrRejectedPositivesOrAcceptedNegativesIncluded':True,'correctButRejectedPositivesIncludedForCoverageReview':True,'rejectedNegativeRawAlsoRetainedInCompleteUnjudgedLogs':True})
 protocol=json.loads(Path(__file__).with_name('protocol.json').read_text(encoding='utf-8'));baseline=selection['activeBaseline']['metrics'];gate_audit={}
 for c in selection['candidates']:
  m=c['metrics'];g={'codeCerImproves':m['codeMetrics']['cer']<baseline['codeMetrics']['cer'],'wholeCerImproves':m['cer']<baseline['cer'],'eachJpFontCodeEditsNonincrease':all(m['fontMetrics'][f]['editErrors']<=baseline['fontMetrics'][f]['editErrors'] for f in protocol['development']['jpFonts']),'naturalRetention':m['groups']['natural']['cer']<=baseline['groups']['natural']['cer']+.002,'rawHighCtcWrong':m['reviewMetrics']['wrongHighConfidence']<=baseline['reviewMetrics']['wrongHighConfidence']+1,'incorrectAccepted':m['incorrectAccepted']<=baseline['incorrectAccepted']+1,'negativeAccepted':m['negativeAccepted']<=baseline['negativeAccepted']+1,'unsupportedPaddingRefused':m['allUnsupportedPaddingRefused']};gate_audit[c['name']]={'guards':g,'eligible':all(g.values()),'checkpointSha256':c['sha256']}
 save(RESULTS/'selection-guard-audit.json',{'fixedGateRetrospectiveDisplayOnly':True,'noReselection':True,'candidates':gate_audit,'selected':selected['name']})
 rows=[selection['activeBaseline']]+selection['candidates'];table=['| Model | Exact /512 | Code exact /256 | Code edits | Whole CER | High CTC wrong | Wrong accepted | Neg accepted |','|---|---:|---:|---:|---:|---:|---:|---:|']
 for row in rows:
  m=row['metrics'];table.append(f"| {row['name']} | {m['exact']} | {m['codeMetrics']['exact']} | {m['codeMetrics']['editErrors']} | {m['cer']:.4%} | {m['reviewMetrics']['wrongHighConfidence']} | {m['incorrectAccepted']} | {m['negativeAccepted']} |")
 qualified=bool(fresh and fresh['promisingForPortableExport']);save(RESULTS/'qualification.json',{'originalTrial':'PASS synthetic component guards only' if qualified else 'FAIL; active baseline fallback','developmentSelected':selected['name'],'developmentSelectedSha256':selected['sha256'],'freshOpened':bool(fresh),'freshGuards':fresh['guards'] if fresh else None,'activeBaselineFallback':not qualified,'activeBaselineOnnxSha256':protocol['initializerAndTeacher']['activeOnnxSha256'],'standardPortableAuthorizedByOriginalGate':qualified,'wholeDocumentQualified':False,'defaultSwitched':False,'postFreshReselectionOrTraining':False,'separateExploratoryRouteAssessmentCannotChangeThisResult':True})
 text='# Japanese-font ASCII V4 fixed trial\n\n'+('Development selected **'+selected['name']+'**; fresh was opened once after its immutable checkpoint hash. ' if selected['epoch']>0 else '**No eligible development checkpoint. Baseline retained; fresh remains ungenerated and unopened.** ')+('**Original trial FAIL; active baseline fallback.** ' if not qualified else '')+'This is an independent synthetic crop study; no product default, Windows document qualification or iOS change is implied.\n\n'+'\n'.join(table)+'\n\n'
 text+='All 512 positive development rows, including physically aliased labels, participate in selection. The 112 negative controls retain separate counts. Eligibility requires code/whole CER improvement, no JP font edit regression, natural English retention and the fixed raw-confidence/false-accept/padding guards; no zero-error condition is used. Every epoch, complete raw CTC intervals/confidence/reject/source checks and every failure are retained.\n\n'
 text+='The architecture/full438 dictionary come from pinned official PP-OCRv5 English. Actual initializer and teacher are font-balanced-v1 epoch4 (`7313bee5…`), rather than unadapted official, V2 or V3 weights. CNN weights and all batch-normalization statistics remained byte-identical; only encoder, full classifier and reject head were updated. All cached activations and teacher outputs are lossless FP32. Newdev original/cached records matched exactly before optimizer start.\n\n'
 text+='Training JP families are disclosed consumed fonts, with new literals/images/normalized inputs. JP development Murecho/NewTegomin are separate; foundation pretraining exposure is unknown. Latin dev/fresh are consumed retention families with new literals. Root BIZUDPGothic/ZenMaruGothic/BIZUDPMincho are never loaded or rendered. The root oracle and legacy 21,201 hashes are used only for complete-string/component exclusion. No real-user document or derivative is used.\n\n'
 fit=json.loads((RESULTS/'trainfit-comparison.json').read_text(encoding='utf-8'));a=fit['active']['codeMetrics'];b=fit['candidate']['codeMetrics'];text+=f"Post-selection train resubstitution code exact {a['exact']}→{b['exact']}/{a['rows']}, edit errors {a['editErrors']}→{b['editErrors']}. This diagnoses fit and receives no generalization credit. `alias-diagnostic.json` reports the all-row main denominator, alias subsets and actual-label upper bounds; no rows are removed.\n\n"
 if fresh:
  a=fresh['active'];b=fresh['candidate'];text+=f"Fixed fresh exact {a['exact']}→{b['exact']}/512, code edits {a['codeMetrics']['editErrors']}→{b['codeMetrics']['editErrors']}, gains {len(fresh['gains'])}, regressions {len(fresh['regressions'])}. Fresh guards {'PASS' if fresh['promisingForPortableExport'] else 'FAIL'}; no checkpoint or threshold was changed after fresh. Detailed font/retention/confidence/negative guards are preserved in `fresh-comparison.json`.\n\n"
 text+=f"CPU2 trial: training {selection['trainingSeconds']:.1f}s, teacher cache {selection['cacheSeconds']:.1f}s, peak RSS {selection['peakRssMiB']:.1f}MiB. Source/protocol/font/license/manifest/exclusion hashes are in registration/freeze receipts; raw failure ledger SHA256 `{sha(path)}`.\n"
 (RESULTS/'README.md').write_text(text,encoding='utf-8',newline='\n')
 for name in ['preparation','training','fresh','postfit','alias','portable','package']:
  log=PARENT/(name+'.log')
  if log.exists():shutil.copyfile(log,RESULTS/(name+'-stdout.log'))
 print(json.dumps({'selected':selected['name'],'freshOpened':bool(fresh),'failureCounts':counts,'failureLedgerSha256':sha(path)}),flush=True)

if __name__=='__main__':main()
