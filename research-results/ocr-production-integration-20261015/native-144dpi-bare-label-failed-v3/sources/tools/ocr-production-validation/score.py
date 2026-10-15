#!/usr/bin/env python3
"""Reference assessment only after both production readers have exited."""
import argparse,hashlib,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/ocr-practical-documents'))
from score import assess,slot

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,x):p.write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
p=argparse.ArgumentParser();p.add_argument('--study',type=Path,required=True);p.add_argument('--dotnet',required=True);a=p.parse_args();out=a.study
for arm in ['v5','hybrid']:
    receipt=json.loads((out/(arm+'-receipt.json')).read_text());assert receipt['exitCode']==0 and sha(out/(arm+'-observations.jsonl'))==receipt['rawSHA256']
freeze=json.loads((out/'freeze.json').read_text());assert sha(out/'oracle.json')==freeze['oracleSHA256']
subprocess.run([a.dotnet,'build',str(ROOT/'tools/ocr-production-validation/Inspect.csproj'),'-c','Release','--nologo'],check=True)
dll=ROOT/'tools/ocr-production-validation/bin/Release/net10.0/Inspect.dll'
manifest=out/('native-inputs.json' if (out/'native-inputs.json').exists() else 'inputs.json')
gold={d['id']:d for d in json.loads((out/'oracle.json').read_text())};rows=[]
for arm in ['v5','hybrid']:
    inspection=out/(arm+'-inspection.jsonl');subprocess.run([a.dotnet,str(dll),str(out/(arm+'-observations.jsonl')),str(manifest),str(inspection)],check=True)
    raw=[json.loads(l) for l in (out/(arm+'-observations.jsonl')).read_text().splitlines()];ins={r['id']:r for r in [json.loads(l) for l in inspection.read_text().splitlines()]}
    assert len(raw)==len(gold)==len(ins) and {r['id'] for r in raw}==set(gold)
    for r in raw:
        g=gold[r['id']];i=ins[r['id']];row=assess(r,i,g)
        row['model']=arm;row['asciiRecognizerCalls']=r['asciiRecognizerCalls'];row['expect']=g['expect']
        expected={(l['className'],str(l['weekday']),l['period'],role):value for l in g['timetable']['lessons'] for role,value in l['names'].items()}
        wrong={f['cellId']+'|'+str(f['lessonIndex'])+'|'+f['role'] for f in i['fields'] if (*slot(f['slot']),f['role']) in expected and f['Value']!=expected[(*slot(f['slot']),f['role'])]}
        targets=i.get('reviewTargets') or []; mandatory={t['CellId']+'|'+str(t['LessonIndex'])+'|'+str(t['Role']).lower() for t in targets}
        # Role is serialized as the underlying enum integer in these records.
        roles={0:'subject',1:'teacher',2:'room'}
        mandatory={t['CellId']+'|'+str(t['LessonIndex'])+'|'+roles.get(t['Role'],str(t['Role']).lower()) for t in targets}
        row['referenceCorrectionAndMandatoryReviewUnion']=len(wrong|mandatory)
        row['allLiteralErrorsCorrectableWithinCurrentBudget']=row['completeSlotCoverage'] and row['missingFields']==0 and row['duplicateFields']==0 and len(wrong|mandatory)<=128
        if g['expect']=='refuse':row['negativeOutcome']='refused' if r['formal'] is None and not i.get('reviewPlanEligible') else 'returned';row['refusalAttribution']='Registered missing header; no reference value is available to either reader.'
        rows.append(row)
criteria=json.loads((out/'generation-freeze.json').read_text()).get('prospectiveCriteria') if (out/'generation-freeze.json').exists() else None
quality=None
if criteria is not None:
    positives=[r for r in rows if r['model']=='hybrid' and r['expect']!='refuse'];negatives=[r for r in rows if r['model']=='hybrid' and r['expect']=='refuse']
    quality=len(positives)==2 and len(negatives)==4 and all(r['outcome'] in ('validated-preview','original-review-required') and (r['outcome']=='validated-preview' or r['reviewPlanEligible']) and r['completeSlotCoverage'] and r['missingFields']==0 and r['unexpectedFields']==0 and r['duplicateFields']==0 and r['conditionalCER']<=criteria['maximumCERPerFont'] and r['referenceCorrectionAndMandatoryReviewUnion']<=128 and r['processPeakWorkingSetBytes']<=criteria['maximumRSSBytes'] for r in positives) and all(r['negativeOutcome']=='refused' for r in negatives)
report={'observations':rows,'prospectiveCriteria':criteria,'criteriaMet':quality,'nativeWindowsRendered':(out/'native-render-receipt.json').exists(),
        'zeroErrorsRequired':False,'rawSHA256':{arm:sha(out/(arm+'-observations.jsonl')) for arm in ['v5','hybrid']},
        'limits':'Fictional documents only. CER remains literal; missing fields separate. Reference-derived edit counts are post-worker diagnosis, not actual user effort or automatic repairs. Final criteria require native rendering; original-input development does not qualify that path.'}
write(out/'report.json',report)
for r in rows:print(json.dumps(r,ensure_ascii=False),flush=True)
print(json.dumps({'criteriaMet':quality,'nativeWindowsRendered':report['nativeWindowsRendered']}),flush=True)
if criteria is not None and (not quality or not report['nativeWindowsRendered']):raise SystemExit('Production validation criteria not met')
