"""Read-only exact-feature information audit; never change optimizer or dev."""
from collections import defaultdict
import hashlib
import json

from adapt_data import ROOT,RESULTS,save


def main():
 results=[]
 for split in ['train','development']:
  manifest=json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'));cache=json.loads((ROOT/(split+'-cache')/'cache.json').read_text(encoding='utf-8'));groups={kind:defaultdict(list) for kind in ['contextAndShortcut','originalFinalFeatures']}
  for row,item in zip(manifest['rows'],cache['rows']):
   if not row['ctcEligible'] or not row['text']:continue
   groups['contextAndShortcut'][item['contextSha256']+':'+item['shortcutSha256']].append((row,item));groups['originalFinalFeatures'][item['teacherFeatureSha256']].append((row,item))
  collisions=[]
  for kind,lookup in groups.items():
   for key,group in lookup.items():
    if len({row['text'] for row,item in group})<2:continue
    collisions.append({'representation':kind,'keySha256':hashlib.sha256(key.encode('utf-8')).hexdigest(),'ids':[row['id'] for row,item in group],'nativeInputsIdentical':len({item['inputTensorSha256'] for row,item in group})==1,'providedPngsIdentical':len({row['pngSha256'] for row,item in group})==1,'limit':'Identical tensors cannot determine contradictory text labels. This is information diagnosis, no target/inference repair or development exclusion.'})
  results.append({'split':split,'collisions':collisions})
 save(RESULTS/'feature-collision-diagnosis.json',{'scope':'Read-only cached feature equality diagnosis, no inference or frozen protocol change','splits':results});print(json.dumps({r['split']:len(r['collisions']) for r in results}))


if __name__=='__main__':main()
