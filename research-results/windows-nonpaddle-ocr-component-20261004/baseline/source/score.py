"""Offline independent literal/position comparison; gold never passed to native OCR."""
import json,csv,io,sys,hashlib
from pathlib import Path
R=Path(__file__).resolve().parent
G=Path('/workspace/recovery-research/ocr-non-paddle-fresh-source-high-20261004/literal-position-oracle.json')
def records(engine):
 return [json.loads(l) for l in (R/(engine+'-raw.log')).read_text(encoding='utf-8').splitlines() if l.startswith('{')]
def box(b):return [b['X'],b['Y'],b['X']+b['Width'],b['Y']+b['Height']]
def contained(a,b):return b[0]<=a[0] and b[1]<=a[1] and a[2]<=b[2] and a[3]<=b[3]
def centered(a,b):x=(a[0]+a[2])/2;y=(a[1]+a[3])/2;return b[0]<x<b[2] and b[1]<y<b[3]
def whitespace(s):return ''.join(c for c in s if c not in ' \t\r\n')
def units(engine,rows,id):
 final=next((r for r in rows if r.get('type')=='result' and r.get('id')==id),None)
 if engine=='ndl':
  return ([{'text':l['text'],'box':[min(p[0] for p in l['boundingBox']),min(p[1] for p in l['boundingBox']),max(p[0] for p in l['boundingBox']),max(p[1] for p in l['boundingBox'])],'confidence':l['confidence'],'confidenceScope':'detector','geometryScope':'native line region','nativeID':l['id']} for l in final['result']['json_lines']] if final and final.get('returned') else []),final
 if engine=='tess':
  output=[]
  if final and final.get('returned'):
   for i,l in enumerate(csv.DictReader(io.StringIO(final['outputs']['tsv']),delimiter='\t')):
    if l['level']=='5' and l['text']:
     x,y,w,h=[int(l[k]) for k in ['left','top','width','height']]
     output.append({'text':l['text'],'box':[x,y,x+w,y+h],'confidence':float(l['conf']),'confidenceScope':'native word confidence (0..100)','geometryScope':'native word box','nativeID':i})
  return output,final
 output=[]
 for r in rows:
  if r.get('type')=='observation' and r.get('id')==id:
   o=r['observation'];output.append({'text':''.join(p['Text'] for p in o['Pieces']),'box':box(o['RecognitionCrop']),'confidence':[p['Confidence'] for p in o['Pieces']],'confidenceScope':'CTC token probabilities before unchanged .8 guard','geometryScope':'native crop region; timestep character mapping is an estimate','nativeID':len(output),'belowGuard':o['BelowConfidence'],'zeroPieces':o['ZeroPieces']})
 return output,final

def score():
 gold=json.loads(G.read_text(encoding='utf-8'));out=[]
 for engine in ['ndl','tess','paddle']:
  rows=records(engine)
  for condition in gold['conditions']:
   id=condition['condition'];us,final=units(engine,rows,id)
   returned=bool(final and final.get('returned',final.get('readReturned',False)))
   targets=[{'id':p['id'],'literal':p['literal'],'region':p['semanticCell'],'kind':p['kind']} for p in condition['sourcePaintRegions'] if p['kind'] not in ['role-label','role-value']]
   targets += [{'id':p['id'],'literal':p['wholeLineLiteral'],'region':p['semanticFieldBand'],'kind':'role-line'} for p in condition['wholeFieldLines']]
   assigned={t['id']:[] for t in targets};unassigned=[]
   for u in us:
    matching=[t for t in targets if centered(u['box'],t['region'])]
    if len(matching)==1:assigned[matching[0]['id']].append(u)
    else:unassigned.append({**u,'centerMatchedTargetCount':len(matching)})
   assessments=[]
   for t in targets:
    selected=sorted(assigned[t['id']],key=lambda u:(u['box'][0],u['box'][1],u['nativeID']))
    text=''.join(u['text'] for u in selected);exact=text==t['literal'];ws=whitespace(text)==whitespace(t['literal'])
    geometry=bool(selected) and all(contained(u['box'],t['region']) for u in selected)
    assess=bool(selected) or returned
    stage='exact-contained' if exact and geometry else 'text-exact-boundary-crossing' if exact else 'missing' if not selected and returned else 'unassessed-after-no-return' if not selected else 'wrong-or-extra-text'
    assessments.append({**t,'observedText':text,'rawNativeUnitIDs':[u['nativeID'] for u in selected],'nativeUnits':selected,'assessed':assess,'nativeConcatenationExact':exact if assess else None,'whitespaceOnlyDiagnosticExact':ws if assess else None,'nativeRegionContained':geometry if assess else None,'strictLiteralAndPositionExact':exact and geometry if assess else None,'stage':stage})
   by={v['id']:v for v in assessments};tuples=[]
   for p in condition['tupleBindings']:
    refs=[p['classHeaderID'],p['weekdayHeaderID'],p['periodHeaderID']]+p['fieldLineIDs'];a=[by[k] for k in refs];assessed=all(v['assessed'] for v in a)
    tuples.append({'index':p['index'],'expected':gold['expectedEightTuples'][p['index']],'evidenceTargetIDs':refs,'fullyAssessed':assessed,'strictTupleExact':all(v['strictLiteralAndPositionExact'] for v in a) if assessed else None,'whitespaceDiagnosticTupleExact':all(v['whitespaceOnlyDiagnosticExact'] and v['nativeRegionContained'] for v in a) if assessed else None,'failedStages':{v['id']:v['stage'] for v in a if not v['strictLiteralAndPositionExact']}})
   out.append({'engine':engine,'condition':id,'readReturned':returned,'final':final,'literalTargets':assessments,'unassignedNativeUnits':unassigned,'tuples':tuples,'strictTuplesExact':sum(v['strictTupleExact'] is True for v in tuples),'fullyAssessedTuples':sum(v['fullyAssessed'] for v in tuples),'plannedTuples':8,'strictTargetsExact':sum(v['strictLiteralAndPositionExact'] is True for v in assessments),'assessedTargets':sum(v['assessed'] for v in assessments),'plannedTargets':len(assessments),'characterAccuracy':'Not a full CER estimate; native line/word/crop units have different segmentation','formalRecovery':'unassessed; component-only8slots, no Builder/Validator/full40','goldUsedOnlyPosthoc':True})
 return {'scope':'2 paired font conditions ×8 same invented tuples; not independent documents/holdout/fullformal','oracleSHA256':hashlib.sha256(G.read_bytes()).hexdigest(),'ordering':'native units concatenated left-to-right within each horizontal single-line region within unique evaluator-only semantic regions; no NFKC/character substitution/substrings or box clipping','arms':out}
if __name__=='__main__':
 result=score();(R/'comparison-results.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print(json.dumps([{k:a[k] for k in ['engine','condition','readReturned','strictTuplesExact','fullyAssessedTuples','strictTargetsExact','assessedTargets','plannedTargets']} for a in result['arms']],ensure_ascii=False))
