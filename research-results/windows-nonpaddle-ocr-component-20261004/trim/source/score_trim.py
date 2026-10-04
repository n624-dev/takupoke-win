import sys,json,hashlib
from pathlib import Path
P=Path(__file__).resolve().parent;R=P.parent;sys.path.insert(0,str(R));import score
rows=[json.loads(l) for l in (P/'trim-raw.log').read_text(encoding='utf-8').splitlines() if l.startswith('{')]
base=score.records
score.records=lambda engine:rows if engine=='ndl' else base(engine)
result=score.score();result['arms']=[a for a in result['arms'] if a['engine']=='ndl'];result['nativeRawSHA256']=hashlib.sha256((P/'trim-raw.log').read_bytes()).hexdigest();result['scoreGeometryScope']='ActualRecognitionROI derived from original raster white-only trim; NOT unchanged detector box or character geometry. OriginalDetectionBoxes and PreTrimParsedParentCrop retained separately.'
for a in result['arms']:
 a['engine']='NDL-white-only-trim';det=next(r['detections'] for r in rows if r.get('type')=='detections' and r['id']==a['condition']);proof={p['nativeIndex']:p for p in a['final']['ownership']['regions']}
 for target in a['literalTargets']:
  for u in target['nativeUnits']:
   p=proof.get(u['nativeID']);u['geometryScope']='original-raster pixel-derived white-trim recognition ROI; no character box';u['originalParsedParentCrop']=p.get('originalBox') if p else None;u['actualRecognitionROI']=p.get('trimmedBox') if p else None;u['originalDetectorBoxExactMatches']=[{'detectorNativeOrder':i,'box':d['box']} for i,d in enumerate(det) if p and list(d['box'])==p.get('originalBox')];u['parentProof']=p;u['transcriptionConfidence']=None
 a['sourceAdoptionEligible']=a['final']['sourceAdoptionEligible'];a['formalQualification']='unassessed; all retained-source guards are diagnostic and not global ink/cell-field proof'
(P/'trim-comparison-results.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
# Diagnostics preserve primary literal punctuation and ownership failures.
diags=[]
for a in result['arms']:
 fields=[t for t in a['literalTargets'] if t['kind']=='role-line'];diag={'condition':a['condition'],'fields':24,'rawConcatColonOnlyExact':sum(t['observedText'].replace(':','：')==t['literal'] for t in fields),'singleCandidateColonOnlyExact':sum(any(u['text'].replace(':','：')==t['literal'] for u in t['nativeUnits']) for t in fields),'nativePixelDerivedFieldBandContained':sum(t['nativeRegionContained'] for t in fields),'strictTuplesExact':a['strictTuplesExact'],'wholeSourceAdoptionEligible':a['sourceAdoptionEligible'],'unambiguousOwnership':'not whole formal qualification'};diags.append(diag)
(P/'trim-text-diagnostics.json').write_text(json.dumps({'diagnosticOnly':True,'primaryResultSHA256':hashlib.sha256((P/'trim-comparison-results.json').read_bytes()).hexdigest(),'summary':diags},ensure_ascii=False,indent=2)+'\n');print(json.dumps(diags))
# Locate actual native purple token scores; parent source coordinates are ROI, not token alignment.
purple=[]
for r in rows:
 if r.get('type')=='token-logit-observation':
  for t in r['tokens']:
   if t['beforeFirstEOS'] and t['actualArgmaxText'] in ['紫','業']:
    purple.append({'condition':r['id'],'cascadeModelIndex':r['cascadeModelIndex'],'actualRecognitionROI':r['cropContext']['sourceViewBox'],'tensorSHA256':r['tensorSHA256'],'token':t,'noOriginalCharacterBox':True,'softmaxIsUncalibrated':True})
(P/'purple-token-evidence.json').write_text(json.dumps(purple,ensure_ascii=False,indent=2)+'\n');print('purple',[(p['condition'],p['token']['actualArgmaxText'],p['token']['derivedArgmaxSoftmax'],p['token']['logitMargin']) for p in purple])
