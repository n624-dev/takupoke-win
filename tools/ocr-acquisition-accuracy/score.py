#!/usr/bin/env python3
"""Assess ALL closed historical/candidate documents; never supplies reader values."""
import argparse,hashlib,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/ocr-practical-documents'))
from score import assess,slot

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,x):p.write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
p=argparse.ArgumentParser();p.add_argument('--study',type=Path,required=True);p.add_argument('--dotnet',required=True);a=p.parse_args();out=a.study
freeze=json.loads((out/'freeze.json').read_text());execution=json.loads((out/'execution-freeze.json').read_text())
for arm in ['baseline','candidate']:
 receipt=json.loads((out/(arm+'-receipt.json')).read_text());assert receipt['exitCode']==0 and sha(out/(arm+'-observations.jsonl'))==receipt['rawSHA256']
 assert execution['arms'][arm]['inputSHA256']==sha(out/'native-inputs.json')
assert sha(out/'oracle.json')==freeze['oracleSHA256']
subprocess.run([a.dotnet,'build',str(ROOT/'tools/ocr-whole-model-comparison/Inspect.csproj'),'-c','Release','--nologo'],check=True)
dll=ROOT/'tools/ocr-whole-model-comparison/bin/Release/net10.0/Inspect.dll'
write(out/'inspection-freeze.json',{'sources':{str(q.relative_to(ROOT)):sha(q) for q in (ROOT/'src/Takupoke.Core/Recovery').glob('*.cs')},'files':{str(q.relative_to(dll.parent)):sha(q) for q in dll.parent.rglob('*') if q.is_file()},'oracleReadByInspector':False})
gold={g['id']:g for g in json.loads((out/'oracle.json').read_text())};native={d['id']:d for d in json.loads((out/'native-inputs.json').read_text())['documents']};rows=[];raw_by_arm={};ins_by_arm={}
for arm in ['baseline','candidate']:
 inspection=out/(arm+'-inspection.jsonl');subprocess.run([a.dotnet,str(dll),str(out/(arm+'-observations.jsonl')),str(out/'native-inputs.json'),str(inspection)],check=True)
 raw=[json.loads(line) for line in (out/(arm+'-observations.jsonl')).read_text().splitlines()];ins={r['id']:r for r in map(json.loads,inspection.read_text().splitlines())}
 assert len(raw)==len(gold)==len(ins)==18 and {r['id'] for r in raw}==set(gold)
 raw_by_arm[arm]={r['id']:r for r in raw};ins_by_arm[arm]=ins
 for r in raw:
  g=gold[r['id']];i=ins[r['id']];x=assess(r,i,g);x.update(model=arm,expect=g['expect'],font=g['font'],cohort=g['cohort'],asciiRecognizerCalls=r['asciiRecognizerCalls'])
  expected={(l['className'],str(l['weekday']),l['period'],role):value for l in g['timetable']['lessons'] for role,value in l['names'].items()}
  found={(*slot(f['slot']),f['role']):f for f in i['fields'] if f['lessonIndex']==0}
  wrong={f['cellId']+'|'+str(f['lessonIndex'])+'|'+f['role'] for k,f in found.items() if k in expected and f['Value']!=expected[k]}
  roles={0:'subject',1:'teacher',2:'room'};mandatory={t['CellId']+'|'+str(t['LessonIndex'])+'|'+roles.get(t['Role'],str(t['Role']).lower()) for t in i.get('reviewTargets') or []}
  x['referenceCorrectionAndMandatoryReviewUnion']=len(wrong|mandatory)
  x['missingCharacterEdits']=sum(len(v) for k,v in expected.items() if k not in found)
  x['coverageAwareCharacterEdits']=x['rawCharacterEdits']+x['missingCharacterEdits']
  x['allLiteralErrorsCorrectableWithinCurrentBudget']=x['completeSlotCoverage'] and x['missingFields']==0 and x['unexpectedFields']==0 and x['duplicateFields']==0 and (i['reviewPlanEligible'] or r['outcome']=='validated-preview') and len(wrong|mandatory)<=128
  x['acquisitionComplete']=len(r['pageReports'])==len(native[r['id']]['pages'])==5
  if r.get('document'):
   assert [(p['Page'],p['RasterHash']) for p in r['document']['Capture']['Pages']]==[(p['page'],p['pixels']['sha256']) for p in native[r['id']]['pages']]
  if g['expect']=='refuse':
   x['negativeOutcome']='refused' if r['formal'] is None and not i.get('reviewPlanEligible') else 'returned'
   x['refusalAttribution']='Acquisition completed before structural refusal' if x['acquisitionComplete'] else 'Acquisition failure confounds missing-header refusal; not qualification'
  rows.append(x)
identity=[];pairs=[];changes=[]
for id,g in gold.items():
 b=raw_by_arm['baseline'][id];c=raw_by_arm['candidate'][id]
 identity.append(dict(id=id,sameOriginalNativeManifest=True,baselinePages=len(b['pageReports']),candidatePages=len(c['pageReports']),primaryObservationsIdentical=len(b['pageReports'])==len(c['pageReports']) and all(bp['primaryObservations']==cp['primaryObservations'] for bp,cp in zip(b['pageReports'],c['pageReports'])),scope='Acquisition repair intentionally changes native calls/crops. Every source asset verified by both worker binaries; fully captured page hashes independently checked against shared manifest.'))
 if g['expect']=='refuse':continue
 before=next(x for x in rows if x['id']==id and x['model']=='baseline');after=next(x for x in rows if x['id']==id and x['model']=='candidate')
 pairs.append(dict(id=id,font=g['font'],cohort=g['cohort'],beforeExact=before['exactFields'],afterExact=after['exactFields'],fieldDelta=after['exactFields']-before['exactFields'],beforeMissingFields=before['missingFields'],afterMissingFields=after['missingFields'],beforeEdits=before['rawCharacterEdits'],afterEdits=after['rawCharacterEdits'],beforeCoverageAwareEdits=before['coverageAwareCharacterEdits'],afterCoverageAwareEdits=after['coverageAwareCharacterEdits']))
 expected={(l['className'],str(l['weekday']),l['period'],role):value for l in g['timetable']['lessons'] for role,value in l['names'].items()}
 observed={arm:{(*slot(f['slot']),f['role']):f for f in ins_by_arm[arm][id]['fields'] if f['lessonIndex']==0} for arm in ['baseline','candidate']}
 for k,v in expected.items():
  bv=observed['baseline'].get(k,{}).get('Value');cv=observed['candidate'].get(k,{}).get('Value')
  if bv!=cv:changes.append(dict(id=id,key=k,expected=v,before=bv,after=cv,gain=bv!=v and cv==v,loss=bv==v and cv!=v,baselineWasMissing=bv is None))
write(out/'paired-source-identity.json',identity);write(out/'field-changes.json',changes)
criteria=json.loads((out/'generation-freeze.json').read_text())['prospectiveCriteria'];positives=[x for x in rows if x['model']=='candidate' and x['expect']!='refuse'];negatives=[x for x in rows if x['model']=='candidate' and x['expect']=='refuse']
quality=len(positives)==6 and len(negatives)==12 and all(x['outcome'] in ('validated-preview','original-review-required') and x['allLiteralErrorsCorrectableWithinCurrentBudget'] and x['completeSlotCoverage'] and x['exactFields']/x['expectedFields']>=criteria['minimumExactFieldRate'] and x['conditionalCER']<=criteria['maximumCERPerFont'] and x['processPeakWorkingSetBytes']<=criteria['maximumRSSBytes'] for x in positives) and all(x['negativeOutcome']=='refused' and x['acquisitionComplete'] for x in negatives)
improved=all(x['fieldDelta']>=0 and x['afterCoverageAwareEdits']<=x['beforeCoverageAwareEdits'] for x in pairs) and sum(x['fieldDelta'] for x in pairs)>0
report=dict(recipe='paired-native-acquisition-accuracy-v1',criteriaMet=quality,pairedLiteralImprovementMet=improved,nativeWindowsRendered=(out/'native-render-receipt.json').exists(),paired=pairs,observations=rows,prospectiveCriteria=criteria,zeroErrorsRequired=False,rawSHA256={arm:sha(out/(arm+'-observations.jsonl')) for arm in ['baseline','candidate']},pairedSourceIdentitySha256=sha(out/'paired-source-identity.json'),limits='Four consumed regression fonts/corpora plus two unused fonts/new literals. Actual separate historical/current workers, original native rasters and full capture/parser/review. Conditional CER excludes explicitly reported missing fields; coverage-aware edits count each absent reference character as a deletion. Acquisition gains reported separately from literal substitutions. Reference correction union is not human time/acknowledgement. No calibrated confidence claim or unattended import qualification.')
write(out/'report.json',report);print(json.dumps(dict(criteriaMet=quality,pairedLiteralImprovementMet=improved,paired=pairs),ensure_ascii=False),flush=True)
if not quality or not improved or not report['nativeWindowsRendered']:raise SystemExit('Native whole-route accuracy criteria failed; keep all outputs, default unchanged')
