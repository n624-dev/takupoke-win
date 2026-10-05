from pathlib import Path
import json,hashlib
D=Path(__file__).resolve().parent;F=Path('/workspace/recovery-research/ocr-unlabeled-fresh-source-high-20261004');gold=json.loads((F/'drawing-glyph-preflight.json').read_text());oracle=json.loads((F/'literal-formal-oracle.json').read_text())
def h(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def inside(a,b):return b[0]<=a[0] and b[1]<=a[1] and a[2]<=b[2] and a[3]<=b[3]
outputs=[]
for arm in ['baseline','trim']:
 p=D/(arm+'-raw.log');rows=[json.loads(l) for l in p.read_text(encoding='utf-8').splitlines() if l.startswith('{')];final=next(r for r in rows if r['type']=='result');units=[]
 for u in final.get('result',{}).get('json_lines',[]):
  xy=u['boundingBox'];box=[min(v[0] for v in xy),min(v[1] for v in xy),max(v[0] for v in xy),max(v[1] for v in xy)]
  units.append({'nativeID':u['id'],'text':u['text'],'box':box,'detectorConfidence':u.get('confidence'),'transcriptionConfidence':None,'geometryScope':'pixel-white-trim derived ActualRecognitionROI' if arm=='trim' else 'native exported line box (parsed layout, original detections separate)','center':[(box[0]+box[2])/2,(box[1]+box[3])/2]})
 targets=[]
 for t in gold['paint']:
  box=t['semanticBox'];assigned=[u for u in units if box[0]<=u['center'][0]<box[2] and box[1]<=u['center'][1]<box[3]];ordered=sorted(assigned,key=lambda u:(u['box'][0],u['nativeID']));text=''.join(u['text'] for u in ordered);contained=bool(assigned) and all(inside(u['box'],box) for u in assigned)
  exact=text==t['literal'];single=any(u['text']==t['literal'] for u in assigned)
  targets.append({'id':t['id'],'kind':t['kind'],'className':t.get('className'),'day':t.get('day'),'period':t.get('period'),'role':t.get('role'),'literal':t['literal'],'originalSemanticBand':box,'originalPrintedInkBox':t['actualInkBox'],'nativeUnits':assigned,'centerAssociatedConcatDiagnostic':text,'rawConcatExact':exact,'oneCandidateRawExact':single,'allAssignedActualBoxesContained':contained,'strictLiteralAndPosition':exact and contained,'failureStage':'not-returned' if not final['returned'] else 'undetected' if not assigned else 'native-text-or-overlap-difference' if not exact else 'region-crossing' if not contained else 'local-literal-position-exact','causalCertainty':'actual raw difference confirmed; intrinsic model capacity undetermined'})
 tuples=[]
 for lesson in oracle['lessons']:
  fields=[t for t in targets if t['kind']=='body' and t['day']==lesson['day'] and t['period']==lesson['period']]
  tuples.append({'className':lesson['className'],'day':lesson['day'],'period':lesson['period'],'fieldTargets':[t['id'] for t in fields],'rawThreeValuesExact':len(fields)==3 and all(t['rawConcatExact'] for t in fields),'threeFieldBoxesExact':len(fields)==3 and all(t['strictLiteralAndPosition'] for t in fields),'fullTupleExact':None,'fullTupleReason':'class/day/period headers and all ownership require unchanged host pipeline; local fields alone are not complete tuple/formal proof'})
 body=[t for t in targets if t['kind']=='body'];headers=[t for t in targets if t['kind']!='body']
 outputs.append({'arm':arm,'rawSHA256':h(p),'readReturned':final['returned'],'nativeLines':len(units),'targets':targets,'tuples':tuples,'counts':{'plannedBodyFields':120,'assessedBodyFields':120 if final['returned'] else 0,'rawBodyConcatExact':sum(t['rawConcatExact'] for t in body),'oneBodyCandidateRawExact':sum(t['oneCandidateRawExact'] for t in body),'bodyLiteralAndPositionExact':sum(t['strictLiteralAndPosition'] for t in body),'plannedHeaderTargets':len(headers),'rawHeaderConcatExact':sum(t['rawConcatExact'] for t in headers),'headerLiteralAndPositionExact':sum(t['strictLiteralAndPosition'] for t in headers),'rawThreeValueCellsExact':sum(t['rawThreeValuesExact'] for t in tuples),'threeValuePositionsExact':sum(t['threeFieldBoxesExact'] for t in tuples),'formalExact':None},'noNativeCharacterBoxes':True,'noOracleFedToNative':True,'centerAssociationOnlyOffline':True})
v={'recipe':'NDL-unlabeled-full40-fullframe-v1','sourceGlyphSHA256':h(F/'drawing-glyph-preflight.json'),'formalOracleSHA256':h(F/'literal-formal-oracle.json'),'scope':'actual native original pixel boxes/strings, posthoc only; local field diagnostics do not assign safe roles or prove full tuple','arms':outputs};(D/'per-target-fullframe-results.json').write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n');print(json.dumps([{'arm':a['arm'],**a['counts']} for a in outputs],ensure_ascii=False))
