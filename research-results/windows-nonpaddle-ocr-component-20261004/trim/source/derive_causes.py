import json,hashlib
from pathlib import Path
P=Path(__file__).resolve().parent;R=P.parent
load=lambda p:json.loads(p.read_text(encoding='utf-8'))
h=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
base=load(R/'comparison-results.json');new=load(P/'trim-comparison-results.json');out=[]
def colon(s):return s.replace(':','：')
for a in new['arms']:
 b=next(x for x in base['arms'] if x['engine']=='ndl' and x['condition']==a['condition'])
 for t in a['literalTargets']:
  prior=next(x for x in b['literalTargets'] if x['id']==t['id'])
  reasons=[]
  units=t['nativeUnits'];priorunits=prior['nativeUnits']
  literal=t.get('expectedLiteral',t.get('literal',''))
  # Copy scoring fields as observations rather than inventing alternative candidates.
  if not units:reasons.append('no retained native unit uniquely center-associated to this printed region')
  if t.get('nativeRegionContained') is False and units:reasons.append('derived recognition ROI crosses target region; position contract unresolved')
  if t.get('kind')=='role-line' and len(units)>1:
   if any(u.get('parentProof',{}).get('conflictingOwner') for u in units):reasons.append('original nonwhite ink has multiple retained parent owners; no text selection or dedup applied')
  if units and not t.get('nativeConcatenationExact'):
   reasons.append('literal differs: colon-only representation' if colon(t.get('observedText',''))==colon(literal) else 'retained raw text differs from literal; includes duplication/segmentation effects, not necessarily recognition character error')
  out.append({'condition':a['condition'],'targetID':t['id'],'kind':t['kind'],'expectedOriginal':literal,'baseline':prior,'trim':t,'confirmedDifferences':reasons,'causalScope':'white-only recognition ROI changed; detector/model/settings fixed. Native source segmentation and actual metadata constraints remain independent. Token probabilities uncalibrated; intrinsic capacity undetermined.'})
report={'scope':'paired labeled8-slot component, secondary; unlabeled production-like full40 remains unassessed','baselineResultsSHA256':h(R/'comparison-results.json'),'trimResultsSHA256':h(P/'trim-comparison-results.json'),'rawSHA256':h(P/'trim-raw.log'),'geometryScope':'OriginalDetectionBox and pretrim parsed parent retained; scoring uses pixel-derived ActualRecognitionROI, never synthetic character boxes','rows':out,'purpleEvidence':load(P/'purple-token-evidence.json'),'conclusions':{'strictTuples':{'baseline':0,'trim':0,'planned':16},'colonOnlyTextDiagnostic':load(P/'trim-text-diagnostics.json')['summary'],'baselinePurpleWrongBoth':True,'trimPurpleCorrectBoth':True,'baselineTokenLogitsRecorded':False,'causeStrength':'recognition change after white-only trim supported; no baseline logits or calibrated correctness probability, no model general-ability conclusion','ownershipNotProven':True,'globalInkNotProven':True,'formalUnassessed':True,'noMaskOnlyInference':True,'currentMaskTopKPrefix':True}}
(P/'trim-per-target-causes.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(len(out),'target rows saved')
