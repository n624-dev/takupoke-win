"""Hash every previous candidate literal possible under this generator's alphabet."""
import argparse
import hashlib
import json
import re
from pathlib import Path

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def walk(x):
 if isinstance(x,dict):
  for key,value in x.items():
   if key in ['text','renderedLiteral','value','literal'] and isinstance(value,str):yield value
   elif isinstance(value,(dict,list)):yield from walk(value)
 elif isinstance(x,list):
  for value in x:yield from walk(value)

def main():
 p=argparse.ArgumentParser();p.add_argument('--peer-study',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--fresh-state',choices=['closed','unopened'],default='closed');a=p.parse_args();sources=[]
 path=a.peer_study/'excluded-values.json';values=set(json.loads(path.read_text()));sources.append(dict(file=path.name,bytes=path.stat().st_size,sha256=sha(path)))
 if a.fresh_state=='unopened' and (a.peer_study/'fresh/manifest.json').exists():raise ValueError('Cannot claim unopened fresh after its manifest exists')
 for split in ['train','development']+(['fresh'] if a.fresh_state=='closed' else []):
  path=a.peer_study/split/'manifest.json'
  values.update(walk(json.loads(path.read_text())))
  sources.append(dict(file=split+'/manifest.json',bytes=path.stat().st_size,sha256=sha(path)))
 # Nonmatching strings cannot equal any generated body field or ASCII tail.
 relevant={v for v in values if (re.fullmatch(r'[A-Za-z0-9_-]{2,8}',v) and any(c.isdigit() for c in v)) or v.startswith(('架空','仮想','仮設'))}
 result=dict(recipe='whole-v3-exact-sha256-literal-exclusions-v1',sources=sources,peerFreshState=a.fresh_state,totalPreviousLiterals=len(values),possibleGeneratedLiteralMatches=len(relevant),filter='Exact generator language: ASCII 2..8 with digit, or full field beginning 架空/仮想/仮設. Every generated numeric tail is independently checked. Structural titles/class/weekday labels intentionally shared controls, not claimed unseen.',literalSha256=sorted(hashlib.sha256(v.encode()).hexdigest() for v in relevant))
 with a.output.open('x',encoding='utf-8') as stream:json.dump(result,stream,ensure_ascii=False,indent=2);stream.write('\n')
 print(json.dumps(dict(output=str(a.output),allPreviousLiterals=len(values),relevant=len(relevant),sha256=sha(a.output))))

if __name__=='__main__':main()
