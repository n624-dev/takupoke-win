"""Observed text/ownership diagnostics only; does not change primary scoring or OCR."""
import json,hashlib
from pathlib import Path
R=Path(__file__).resolve().parent
D=json.loads((R/'comparison-results.json').read_text(encoding='utf-8'))
def colon_only(s):return s.replace(':','：')
def body(s,role):
 prefix={'subject':'科目','teacher':'担当','room':'教室'}[role]
 if not s.startswith(prefix):return None
 v=s[len(prefix):]
 if v.startswith((':','：')):v=v[1:]
 return v # Diagnostic public label prefix only; no expected-value selection or role repair.
by={(a['engine'],a['condition']):a for a in D['arms']}
rows=[];dual=[]
for (engine,condition),a in by.items():
 if engine=='paddle':continue
 for t in a['literalTargets']:
  if t['kind']!='role-line':continue
  normalized=colon_only(t['observedText']);single=[u['nativeID'] for u in t['nativeUnits'] if colon_only(u['text'])==t['literal']]
  rows.append({'engine':engine,'condition':condition,'target':t['id'],'expected':t['literal'],'observedNativeConcat':t['observedText'],'colonOnlyConcatExact':normalized==t['literal'],'singleNativeCandidateColonOnlyExactIDs':single,'candidateExistenceIsOracleDiagnosticNotAdoption':True,'nativeRegionContained':t['nativeRegionContained'],'nativeUnitCount':len(t['nativeUnits']),'primaryStrictExact':t['strictLiteralAndPositionExact'],'scope':'text diagnostics leave actual colons/duplicates/native boxes unchanged, no repair/candidate choice'})
for condition in ['clear','small']:
 n={t['id']:t for t in by[('ndl',condition)]['literalTargets'] if t['kind']=='role-line'};ts={t['id']:t for t in by[('tess',condition)]['literalTargets'] if t['kind']=='role-line'}
 for id,a in n.items():
  b=ts[id];role=id.split('-')[-1];na=body(a['observedText'],role);nb=body(b['observedText'],role);expected=body(a['literal'],role)
  agreement=na is not None and nb is not None and na==nb
  dual.append({'condition':condition,'field':id,'NDLObserved':a['observedText'],'TessObserved':b['observedText'],'NDLPublicPrefixSuffixDiagnostic':na,'TessPublicPrefixSuffixDiagnostic':nb,'expectedBody':expected,'agreed':agreement,'agreedCorrect':agreement and na==expected,'agreedWrong':agreement and na!=expected,'NDLExpectedBodyExact':na==expected if na is not None else None,'TessExpectedBodyExact':nb==expected if nb is not None else None,'noTupleOrSourceGeometryCredit':True,'limitations':'No source-box split or observed label repair; agreement does not prove empty ink/coverage/semantic ownership, confidence calibration or formal qualification'})
summaries=[]
for engine in ['ndl','tess']:
 for condition in ['clear','small']:
  rs=[r for r in rows if r['engine']==engine and r['condition']==condition]
  summaries.append({'engine':engine,'condition':condition,'fields':len(rs),'colonOnlyConcatenationExact':sum(r['colonOnlyConcatExact'] for r in rs),'singleRawCandidateExistsColonOnlyExact':sum(bool(r['singleNativeCandidateColonOnlyExactIDs']) for r in rs),'containedFieldRegion':sum(r['nativeRegionContained'] is True for r in rs),'primaryStrictExact':sum(r['primaryStrictExact'] is True for r in rs)})
value={'primaryResultSHA256':hashlib.sha256((R/'comparison-results.json').read_bytes()).hexdigest(),'source':'Existing raw only; no OCR/model/API calls, no primary metric alteration','rows':rows,'summary':summaries,'dualConsensusDiagnostic':dual,'dualSummary':{k:sum(r[k] for r in dual) for k in ['agreed','agreedCorrect','agreedWrong']},'intrinsicCapacity':'undetermined'}
(R/'posthoc-text-ownership-and-dual.json').write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print(json.dumps({'summary':summaries,'dual':value['dualSummary']},ensure_ascii=False))
