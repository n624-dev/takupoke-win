"""Offline literal/position diagnostics ONLY. No native invocation or repair."""
from pathlib import Path
import json,hashlib
P=Path(__file__).resolve().parent;F=Path('/workspace/recovery-research/ocr-unlabeled-fresh-source-high-20261004')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def inside(a,b):return b[0]<=a[0] and b[1]<=a[1] and a[2]<=b[2] and a[3]<=b[3]
rows=[]
for l in (P/'native-raw.log').read_text(encoding='utf-8').splitlines():
 if l.startswith('{'):
  try:rows.append(json.loads(l))
  except json.JSONDecodeError:pass
finals=[r for r in rows if r.get('type')=='result']
if len(finals)>1:raise ValueError('Multiple final native results')
final=finals[0] if finals else None
units=[]
for u in (final or {}).get('result',{}).get('json_lines',[]):
 xy=u['boundingBox'];box=[min(p[0] for p in xy),min(p[1] for p in xy),max(p[0] for p in xy),max(p[1] for p in xy)]
 units.append({'adapterID':u['id'],'originalNativeID':u['originalNativeID'],'regionID':u['regionID'],'text':u['text'],'box':box,'center':[(box[0]+box[2])/2,(box[1]+box[3])/2],'detectorConfidence':u.get('confidence'),'transcriptionConfidence':None,'geometryScope':'Original-raster white-trim recognition ROI translated by EXACT integer origin; original local box and native detections retained in raw'})
# Incomplete global serialization retains per-region records, but is NOT a complete source.
returned={r['regionID']:r for r in rows if r.get('type')=='region-result' and r['returned']}
plan=json.loads((P/'regions-before-native.json').read_text());gold=json.loads((F/'drawing-glyph-preflight.json').read_text());oracle=json.loads((F/'literal-formal-oracle.json').read_text())
targets=[]
for t in gold['paint']:
 box=t['semanticBox'];ink=t['actualInkBox'];center=[(ink[0]+ink[2])/2,(ink[1]+ink[3])/2]
 owners=[r for r in plan['regions'] if r['box'][0]<=center[0]<r['box'][2] and r['box'][1]<=center[1]<r['box'][3]]
 assessed=bool(final) and len(owners)==1 and owners[0]['id'] in returned and not any(e['regionID']==owners[0]['id'] for e in final['regionErrors'])
 assigned=[u for u in units if box[0]<=u['center'][0]<box[2] and box[1]<=u['center'][1]<box[3]]
 ordered=sorted(assigned,key=lambda u:(u['box'][0],u['adapterID']));text=''.join(u['text'] for u in ordered);exact=text==t['literal'];one=any(u['text']==t['literal'] for u in assigned);contained=bool(assigned) and all(inside(u['box'],box) for u in assigned)
 targets.append({'id':t['id'],'kind':t['kind'],'day':t.get('day'),'period':t.get('period'),'role':t.get('role'),'literal':t['literal'],'originalPrintedInkBox':ink,'originalSemanticBand':box,'plannedRegionIDs':[r['id'] for r in owners],'assessed':assessed,'actualNativeUnits':assigned,'centerAssociatedConcatDiagnostic':text,'rawConcatExact':exact if assessed else None,'oneCandidateRawExact':one if assessed else None,'actualBoxesContainedInDiagnosticBand':contained if assessed else None,'literalAndDiagnosticPositionExact':exact and contained if assessed else None,'failureStage':'execution-or-serialization-unassessed' if not assessed else 'not-detected' if not assigned else 'raw-text-or-duplicate-difference' if not exact else 'box-crosses-diagnostic-band' if not contained else 'local-text-position-exact','causeScope':'Confirmed actual observed difference only. Diagnostic role bands are NOT physical role-proof; actual host Strict/Builder decides. Intrinsic model capacity undetermined.'})
body=[t for t in targets if t['kind']=='body'];headers=[t for t in targets if t['kind']!='body']
tuples=[]
for lesson in oracle['lessons']:
 fields=[t for t in body if t['day']==lesson['day'] and t['period']==lesson['period']]
 tuples.append({'className':lesson['className'],'day':lesson['day'],'period':lesson['period'],'fieldTargets':[t['id'] for t in fields],'assessed':all(t['assessed'] for t in fields),'rawThreeValuesExact':all(t['rawConcatExact'] for t in fields) if all(t['assessed'] for t in fields) else None,'threeValuesAndDiagnosticPositionsExact':all(t['literalAndDiagnosticPositionExact'] for t in fields) if all(t['assessed'] for t in fields) else None,'fullTupleExact':None,'scope':'Headers/physical ownership/current host formal route must independently succeed; local 3 values are insufficient.'})
recognitions=[r for r in rows if r.get('type')=='recognition'];byregion=[]
for r in plan['regions']:
 rec=[v for v in recognitions if v['regionID']==r['id']]
 byregion.append({'regionID':r['id'],'kind':r['kind'],'box':r['box'],'readReturned':r['id'] in returned,'actualRecognitionCalls':len(rec),'upstreamAutoCCWPredicateCalls':sum(bool(v['cropContext']['upstreamAspectRotationPredicate']) for v in rec),'recognitions':rec})
report={'recipeSHA256':sha(P/'recipe-frozen.json'),'rawSHA256':sha(P/'native-raw.log'),'sourceGlyphSHA256':sha(F/'drawing-glyph-preflight.json'),'formalOracleSHA256':sha(F/'literal-formal-oracle.json'),'scope':'One consumed-development image, physical crop+unchanged default recognizer. Posthoc gold only; no oracle input/character box fabrication/repair. Actual formal route separately measured.','nativeReturned':bool(final and final['returned']),'nativeCounters':(final or {}).get('nativeCallCounters'),'plannedRegions':len(plan['regions']),'regionReadReturns':len(returned),'regionSerializationCompleted':(final or {}).get('serializationCompletedRegions'),'targets':targets,'cells':tuples,'regions':byregion,'counts':{'plannedBodyFields':len(body),'assessedBodyFields':sum(t['assessed'] for t in body),'rawBodyConcatExact':sum(t['rawConcatExact'] is True for t in body),'oneBodyCandidateRawExact':sum(t['oneCandidateRawExact'] is True for t in body),'bodyLiteralAndDiagnosticPositionExact':sum(t['literalAndDiagnosticPositionExact'] is True for t in body),'plannedHeaderTargets':len(headers),'assessedHeaderTargets':sum(t['assessed'] for t in headers),'rawHeaderConcatExact':sum(t['rawConcatExact'] is True for t in headers),'headerLiteralAndDiagnosticPositionExact':sum(t['literalAndDiagnosticPositionExact'] is True for t in headers),'rawThreeValueCellsExact':sum(t['rawThreeValuesExact'] is True for t in tuples),'threeValueDiagnosticPositionsExact':sum(t['threeValuesAndDiagnosticPositionsExact'] is True for t in tuples),'fullFormalExact':None}}
(P/'per-target-results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n');print(json.dumps(report['counts']))
