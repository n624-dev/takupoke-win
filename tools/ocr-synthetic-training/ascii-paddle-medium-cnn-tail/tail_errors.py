"""Read-only edit classes: source/decoder diagnosis, never output repair."""
from collections import Counter
import hashlib
import json
from pathlib import Path
import sys

from tail_data import ROOT,RESULTS,save
sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-foundation-ctc'))
from diagnose import edits


def main():
 manifest=json.loads((ROOT/'development/manifest.json').read_text(encoding='utf-8'));lookup={r['id']:r for r in manifest['rows']};result=[]
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'))
 for name in ['original']+[c['name'] for c in selection['candidates']]:
  raw=RESULTS/(name+'-unjudged.jsonl');records=[json.loads(line) for line in raw.read_text(encoding='utf-8').splitlines()];changes=Counter();groups={}
  for kind in ['mixed_code','mixed_confusable','ascii_short','japanese','retention_japanese']:
   group=[r for r in records if lookup[r['id']]['kind']==kind];stats={'rows':len(group),'wrongRows':0,'rowsWithAsciiMismatch':0,'rowsWithNonAsciiMismatch':0,'rowsWithSpaceMismatch':0,'asciiMismatchAccepted':0,'nonAsciiMismatchAccepted':0}
   for r in group:
    difference=edits(lookup[r['id']]['text'],r['prediction']);changes.update(difference);a=any(any(32<=ord(c)<=126 for c in left+right) for left,right in difference);j=any(any(ord(c)>126 for c in left+right) for left,right in difference);s=any(' ' in left+right for left,right in difference);stats['wrongRows']+=bool(difference);stats['rowsWithAsciiMismatch']+=a;stats['rowsWithNonAsciiMismatch']+=j;stats['rowsWithSpaceMismatch']+=s;stats['asciiMismatchAccepted']+=a and r['accepted'];stats['nonAsciiMismatchAccepted']+=j and r['accepted']
   groups[kind]=stats
  result.append({'name':name,'groups':groups,'mostCommonEdits':[{'gold':a,'prediction':b,'count':n} for (a,b),n in changes.most_common(24)],'rawSha256':hashlib.sha256(raw.read_bytes()).hexdigest()})
 save(RESULTS/'edit-diagnosis.json',{'scope':'Read-only saved-output diagnosis; minimum edit tie order can change descriptive attribution. No inference, candidate selection or output correction.','conditions':result})
 print(json.dumps(result,ensure_ascii=False))


if __name__=='__main__':main()
