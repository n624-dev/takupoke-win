"""All fixed independent-reader arms, assessed after closed inference."""
import argparse
import collections
import json
import re
from pathlib import Path
from prepare import sha,write
from score import distance

p=argparse.ArgumentParser();p.add_argument('--engine',choices=['tesseract','glm','qwen'],required=True);a=p.parse_args()
study=Path('/workspace/scratch/glm-ocr-native-tails-20261016-v1');folder=study/('tesseract-v2' if a.engine=='tesseract' else a.engine)
receipt=json.loads((folder/('receipt.json' if a.engine=='tesseract' else 'raw-receipt.json')).read_text());assert receipt['exitCode']==0
assert sha(folder/'observations.jsonl')==receipt['rawSha256' if a.engine=='tesseract' else 'sha256']
baseline={r['id']:r for r in map(json.loads,(study/'baseline.jsonl').read_text().splitlines())};gold={r['id']:r for r in json.loads((study/'oracle.json').read_text())}
packet=json.loads((study/'packet.json').read_text());ids={r['id'] for r in packet['rows']}
observations=list(map(json.loads,(folder/'observations.jsonl').read_text().splitlines()))
methods=[7,13] if a.engine=='tesseract' else ['raw','transport']
code=lambda text:bool(re.fullmatch(r'[A-Za-z0-9_-]{2,32}',text)) and any(c.isascii() and c.isdigit() for c in text)
def text_of(r,method):
 text=r['text']
 if a.engine!='tesseract' and method=='transport':
  # Fixed complete framing only. No stripping arbitrary whitespace or repairs.
  fenced=re.fullmatch(r'\n?```(?:text)?\n([A-Za-z0-9_-]{2,32})\n```\n?',text)
  if fenced:return fenced[1]
  text=text[1:] if text.startswith('\n') else text
  text=text[:-1] if text.endswith('\n') else text
 return text
reports=[];changes=[]
for method in methods:
 obs={r['id']:r for r in observations if a.engine!='tesseract' or r['psm']==method};assert set(obs)==ids
 for policy in ['same-length','bounded-code']:
  c=collections.Counter();edits_before=edits_after=characters=0
  for id,b in baseline.items():
   g=gold[id];assert b['baselineText']==g['beforeLine'];new=b['baselineText']
   if id in obs:
    text=text_of(obs[id],method);original=b['original'];split=max((i+1 for i,char in enumerate(original) if ord(char)>127),default=0);primarycode=original[split:]
    if code(text) and (policy=='bounded-code' or len(text)==len(primarycode)):
     new=original[:split]+text;c['selectedCrops']+=1
   for f in g['fields']:
    old=f['before'];assert b['baselineText'].endswith(old)
    prefix=b['baselineText'][:-len(old)] if old else b['baselineText'];after=new[len(prefix):] if new.startswith(prefix) else new;expected=f['expected']
    c['fields']+=1;c['beforeExact']+=old==expected;c['afterExact']+=after==expected;c['gains']+=old!=expected and after==expected;c['losses']+=old==expected and after!=expected;c['changed']+=old!=after
    edits_before+=distance(old,expected);edits_after+=distance(after,expected);characters+=len(expected)
    if old!=after:changes.append(dict(arm=[a.engine,method,policy],id=id,role=f['role'],before=old,after=after,expected=expected))
  reports.append(dict(arm=[a.engine,method,policy],**dict(c),beforeEdits=edits_before,afterEdits=edits_after,characters=characters))
write(folder/'report.json',dict(scores=reports,rawSha256=sha(folder/'observations.jsonl'),calls=len(observations),scope='Consumed fictional source-bound native firstpage only. All72bodyfields scored, no gold given to either reader. Research proposals are not full parser reruns or calibrated confidence/CTC evidence; no activation.'))
write(folder/'changes.json',changes)
for r in reports:print(json.dumps(r,ensure_ascii=False))
