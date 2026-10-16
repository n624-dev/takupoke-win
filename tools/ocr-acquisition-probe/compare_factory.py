import argparse,hashlib,json,math
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--actual',type=Path,required=True);p.add_argument('--expected',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
e=json.loads(a.expected.read_text());expected={(r['id'],r['page']):r for r in e['pages']};rows=[json.loads(x) for x in a.actual.read_text().splitlines()];observed={};result=[]
for r in rows:
 key=(r['id'],r['page']);assert key not in observed;observed[key]=r;ref=expected[key]
 scores=r.get('confidences') or [];glyphs=r.get('glyphs');actual_sha=hashlib.sha256(json.dumps(glyphs,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest()
 finite=len(scores)==len(ref['confidences']) and all(math.isfinite(v) and 0<=v<=1 for v in scores)
 difference=max((abs(v-w) for v,w in zip(scores,ref['confidences'])),default=0) if finite else None
 purposes={purpose:sum(o.get('Purpose','line')==purpose for o in r['observations']) for purpose in set(o.get('Purpose','line') for o in r['observations'])}
 result.append(dict(id=r['id'],page=r['page'],failure=r['failure'],sameOriginalPixels=r['sourceSha256']==ref['sourceSha256'],allLiteralSourceGeometryExact=actual_sha==ref['glyphsCanonicalSha256'],nativeProbabilitiesFinite=finite,maximumNativeProbabilityDifference=difference,sameNativeFloorDecisions=[v<.8 for v in scores]==ref['belowNativeFloor'],sameActualRecognitionPurposes=purposes==ref['recognitionPurposes']))
complete=len(rows)==len(expected)==10 and set(observed)==set(expected)
passed=complete and all(r['failure'] is None and r['sameOriginalPixels'] and r['allLiteralSourceGeometryExact'] and r['nativeProbabilitiesFinite'] and r['maximumNativeProbabilityDifference']<=e['maximumNativeProbabilityDifference'] and r['sameNativeFloorDecisions'] and r['sameActualRecognitionPurposes'] for r in result)
a.output.write_text(json.dumps(dict(passed=passed,complete=complete,referenceRun=e['referenceRun'],referenceRawSHA256=e['referenceRawSHA256'],actualRawSHA256=hashlib.sha256(a.actual.read_bytes()).hexdigest(),scope='Factory wiring equivalence to fixed already-qualified optional candidate on consumed native10pages. Raw native probabilities are compared without changes; tolerance is numerical parity only. No new independent accuracy credit.',pages=result),indent=2)+'\n')
print(json.dumps(dict(passed=passed,pages=len(result),maxNativeProbabilityDifference=max((r['maximumNativeProbabilityDifference'] or 0) for r in result))))
if not passed:raise SystemExit('Factory route differs; keep actual observations and default/release status honest')
