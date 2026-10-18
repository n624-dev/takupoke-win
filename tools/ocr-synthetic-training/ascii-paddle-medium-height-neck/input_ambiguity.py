"""Completed corpus/hash diagnosis only; no inference, filtering or score repair."""
from collections import defaultdict
import json
from pathlib import Path

from height_data import ROOT,RESULTS,save


def main():
 summaries=[]
 for split in ['train','development']:
  manifest=json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'));cache=json.loads((ROOT/(split+'-cache')/'cache.json').read_text(encoding='utf-8'));result=[]
  for kind in ['pngSha256','inputTensorSha256']:
   groups=defaultdict(list)
   for row,item in zip(manifest['rows'],cache['rows']):
    if row['ctcEligible'] and row['text']:groups[(row if kind=='pngSha256' else item)[kind]].append(row)
   for sha,group in groups.items():
    if len({r['text'] for r in group})<2:continue
    result.append({'hashType':kind,'hash':sha,'rows':[{'id':r['id'],'font':r['font'],'kind':r['kind'],'literal':r['text'],'density':r['geometry']['density'],'framedBaseline':r['source']['framedBaseline']} for r in group],'sourceProofRequired':True,'limit':'Different literal provenance with identical provided/model input. No crop recognizer can distinguish these exact inputs. Not a target correction or permission to accept unsupported sources.'})
  summaries.append({'split':split,'scope':'Known synthetic corpus pixel/hash impossibility audit, not model selection or heldout final','collisions':result})
 save(RESULTS/'input-ambiguity-diagnosis.json',{'scope':'Read-only fixed corpus diagnosis; never removes development rows or repairs any prediction/score','splits':summaries});print(json.dumps({'trainCollisions':len(summaries[0]['collisions']),'developmentCollisions':len(summaries[1]['collisions'])}))


if __name__=='__main__':main()
