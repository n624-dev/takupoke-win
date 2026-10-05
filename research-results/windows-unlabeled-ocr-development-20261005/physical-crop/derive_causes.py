"""Actual per-output stage diagnosis only, no inference or evidence repair."""
from pathlib import Path
import json,hashlib
P=Path(__file__).resolve().parent
s=json.loads((P/'per-target-results.json').read_text());tr=json.loads((P/'offline-layout-trace.json').read_text());by={r['regionID']:r for r in tr['regions']};plan=json.loads((P/'regions-before-native.json').read_text());regions={r['id']:r for r in plan['regions']};raw=[json.loads(l) for l in (P/'native-raw.log').read_text().splitlines() if l.startswith('{')];final=next(r for r in raw if r['type']=='result');recognitions=[r for r in raw if r['type']=='recognition'];out=[]
for t in s['targets']:
 ids=t['plannedRegionIDs'];candidates=[];parsed=[]
 for rid in ids:
  r=regions[rid];x,y,_,_=r['box']
  for d in by[rid]['originalDetections']:
   a,b,c,e=d['box'];box=[a+x,b+y,c+x,e+y];cx=(box[0]+box[2])/2;cy=(box[1]+box[3])/2;g=t['originalSemanticBand']
   if g[0]<=cx<g[2] and g[1]<=cy<g[3] and d['class_name'].startswith('line_'):candidates.append({'regionID':rid,'box':box,'class':d['class_name'],'detectorConfidence':d['confidence']})
  for d in by[rid]['parsedLinesBeforeTrim']:
   a=int(d['X'])+x;b=int(d['Y'])+y;c=a+int(d['WIDTH']);e=b+int(d['HEIGHT']);g=t['originalSemanticBand']
   if g[0]<=(a+c)/2<g[2] and g[1]<=(b+e)/2<g[3]:parsed.append({'regionID':rid,'box':[a,b,c,e]})
 related=[r for r in recognitions if r['regionID'] in ids];rotation=[{'actualModelIndex':r['cascadeModelIndex'],'text':r['text'],'inputShape':r['cropContext']['originalInputShape'],'originalLocalRecognitionROI':r['cropContext']['localActualRecognitionROI']} for r in related if r['cropContext']['upstreamAspectRotationPredicate']]
 if not t['assessed']:stage='execution-or-serialization-unassessed';strength='confirmed'
 elif t['rawConcatExact']:
  stage='local-literal-match' if t['literalAndDiagnosticPositionExact'] else 'diagnostic-band-crossing';strength='confirmed'
 elif not t['actualNativeUnits']:
  stage='post-detection-layout-selection-omission' if candidates and not parsed else 'no-separate-retained-line-detection' if not candidates else 'recognized-output-not-associated-to-target-position';strength='confirmed retained observable stage only; latent/belowthreshold detections unknown'
 elif t['oneCandidateRawExact']:stage='duplicate-or-extra-parent-contamination';strength='confirmed'
 else:stage='raw-transcription-difference';strength='confirmed observed difference; rotation/context causal hypothesis supported only when predicate true'
 out.append({'targetID':t['id'],'kind':t['kind'],'day':t['day'],'period':t['period'],'role':t['role'],'literal':t['literal'],'output':t['centerAssociatedConcatDiagnostic'],'failureStage':stage,'evidenceStrength':strength,'nativeUnits':t['actualNativeUnits'],'specificRowDetections':candidates,'specificParsedRowsBeforeTrim':parsed,'predicateTrueRecognizerCallsInRegion':rotation,'causeLimit':'Original diagnostic role band position is not production role proof. Detect/layout/transcription/duplicate stages separate. No model intrinsic incapacity assertion.'})
report={'rawSHA256':s['rawSHA256'],'targetResultsSHA256':hashlib.sha256((P/'per-target-results.json').read_bytes()).hexdigest(),'hostReplaySHA256':hashlib.sha256((P/'host-replay.json').read_bytes()).hexdigest(),'plannedTargets':170,'targets':out,'operationalErrors':len(final['regionErrors']),'sourceGuardFailures':final['sourceGuardFailures'],'actualHost':json.loads((P/'host-replay.json').read_text()),'fullFormalAssessed':0,'scope':'Consumed development PNG. Entire input planner ownership succeeded; native OCR coverage/host source contract did not. Actual named stages, no safe-negative credit or qualification.'};(P/'per-output-causes.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n');print('170 target causes, actual host refusal/0formal')
