#!/usr/bin/env python3
"""Closed full-document development metrics; missing fields stay explicit."""
import argparse, json, hashlib, subprocess, sys
from collections import Counter
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'ocr-model-comparison'))
from audit_practical_accuracy import distance
ROOT=Path(__file__).resolve().parents[2]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,x):p.write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def slot(s):return s['ClassName'],str(s['Day']),s['Period']
def assess(raw,inspection,gold):
 expected={(l['className'],str(l['weekday']),l['period'],role):value for l in gold['timetable']['lessons'] for role,value in l['names'].items()}
 observed={};duplicates=0
 for f in inspection['fields']:
  key=(*slot(f['slot']),f['role'])
  if f['lessonIndex']!=0 or key in observed:duplicates+=1;continue
  observed[key]=f
 keys=set(expected)&set(observed);errors=sum(expected[k]!=observed[k]['Value'] for k in keys)
 edits=sum(distance(expected[k],observed[k]['Value']) for k in keys);characters=sum(len(expected[k]) for k in keys)
 flags=sum(observed[k]['minimumConfidence'] is None or observed[k]['minimumConfidence']<.8 for k in keys)
 wronghigh=sum(expected[k]!=observed[k]['Value'] and observed[k]['minimumConfidence'] is not None and observed[k]['minimumConfidence']>=.8 for k in keys)
 d=raw.get('document');wanted=Counter((s['className'],s['day'],s['period']) for s in gold['requiredSlotSet']);actual=Counter(slot(s) for s in d['RequiredSlots']) if d else Counter()
 coverage=actual==wanted and all(n==1 for n in actual.values())
 result={k:raw[k] for k in ['id','condition','stage','outcome','failure','requiredSlots','recognizerCalls','detectorCalls','milliseconds','processPeakWorkingSetBytes']}
 result.update(expectedFields=len(expected),scoredFields=len(keys),missingFields=len(set(expected)-set(observed)),unexpectedFields=len(set(observed)-set(expected)),duplicateFields=duplicates,
  exactFields=len(keys)-errors,wrongFields=errors,rawCharacterEdits=edits,sourceCharactersScored=characters,conditionalCER=edits/characters if characters else None,
  confidenceFlaggedFields=flags,highConfidenceWrongFields=wronghigh,completeSlotCoverage=coverage,
  reviewPlanEligible=inspection.get('reviewPlanEligible',False),reviewTargets=len(inspection.get('reviewTargets') or []),
  actualTypingWithinProductBudget=coverage and not duplicates and len(keys)==len(expected) and errors<=3,
  sourceOriginal=True)
 if gold['expect']=='refuse':result['negativeOutcome']='refused' if raw['formal'] is None else 'returned';result['refusalAttribution']='missing-day invalidity plus unrelated OCR failures; no sole-cause qualification'
 return result

def main():
 p=argparse.ArgumentParser();p.add_argument('--cohort',type=Path,required=True);p.add_argument('--dotnet',default='dotnet');a=p.parse_args();out=a.cohort
 # Require both closed receipts before touching gold.
 for arm in ['v5','v6']:
  receipt=json.loads((out/(arm+'-receipt.json')).read_text());assert receipt['exitCode']==0 and sha(out/(arm+'-observations.jsonl'))==receipt['rawSHA256']
 freeze=json.loads((out/'reader-freeze.json').read_text());assert sha(out/'oracle.json')==freeze['oracleSHA256']
 dotnet=a.dotnet
 subprocess.run([dotnet,'build',str(ROOT/'tools/ocr-practical-documents/Inspect.csproj'),'-c','Release','--nologo'],check=True)
 inspector=ROOT/'tools/ocr-practical-documents/bin/Release/net10.0/Inspect.dll'
 for arm in ['v5','v6']:
  subprocess.run([dotnet,str(inspector),str(out/(arm+'-observations.jsonl')),str(out/(arm+'-inputs.json')),str(out/(arm+'-inspection.jsonl'))],check=True)
 gold={d['id']:d for d in json.loads((out/'oracle.json').read_text())};rows=[]
 for arm in ['v5','v6']:
  raw=[json.loads(l) for l in (out/(arm+'-observations.jsonl')).read_text().splitlines()];inspected=[json.loads(l) for l in (out/(arm+'-inspection.jsonl')).read_text().splitlines()]
  expected={(id,c) for id in gold for c in (['production','ink-input-frame'] if arm=='v5' else ['ink-input-frame'])}
  assert len(raw)==len(expected) and {(r['id'],r['condition']) for r in raw}==expected
  bykey={(r['id'],r['condition']):r for r in inspected};assert set(bykey)==expected
  for r in raw:rows.append({'model':arm,**assess(r,bykey[r['id'],r['condition']],gold[r['id']])})
 report={'recipe':'practical-complete-three-class-v1','observations':rows,'qualification':False,'zeroErrorsRequired':False,
  'limits':'Development invented 3-class readable original-image PDFs; fonts consumed. Conditional CER excludes explicitly reported missing fields; flags are not calibrated error probabilities. Actual typing count is post-worker reference diagnosis, not user time or acceptance. No simulated human acknowledgements, output repair, or model activation.',
  'rawSHA256':{arm:sha(out/(arm+'-observations.jsonl')) for arm in ['v5','v6']},'scoreSourceSHA256':sha(Path(__file__)),'inspectorSourceSHA256':sha(Path(__file__).with_name('Program.cs'))}
 write(out/'report.json',report)
 for r in rows:print(json.dumps(r,ensure_ascii=False),flush=True)
if __name__=='__main__':main()
