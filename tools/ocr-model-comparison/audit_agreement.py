#!/usr/bin/env python3
"""All fixed CTC-reader pairs on closed consumed outputs; never new inference."""
import argparse
import hashlib
import itertools
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FILES = [
    'ocr-model-color-frozen-20261013/linux-observations.jsonl',
    'ocr-v6-small-frozen-20261013/linux-observations.jsonl',
    'ocr-v5-server-frozen-20261013/observations.jsonl',
    'ocr-japanese-v3-clean-lines-20261013/observations.jsonl',
    'ocr-english-v5-clean-lines-20261014/observations.jsonl',
    'ocr-easyocr-japanese-g1-20261014/observations.jsonl',
    'ocr-easyocr-japanese-g2-20261014/observations.jsonl',
]
MODELS = {'v5-mobile','v6-medium','v6-small','v5-server','japanese-v3','english-v5',
          'easyocr-japanese-g1','easyocr-japanese-g2'}


def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()


def write(p,v): p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


def above(r):
    score = r['minimumConfidence']
    return bool(r['text']) and score is not None and score >= .8 and not r['unsupportedPadding']


def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    a.output.mkdir(parents=True,exist_ok=False)
    goldfile=ROOT/'tools/vision-ocr-comparison/oracle.json'
    freeze={'recipe':'all-fixed-ctc-pair-agreement-consumed-audit-v1','files':{n:sha(ROOT/'research-results'/n) for n in FILES},
        'oracleSha256':sha(goldfile),'sourceSha256':sha(Path(__file__)), 'models':sorted(MODELS),
        'rule':'All28 unordered pairs; entire raw literal equality, nonempty, both own minCTC>=.8, both supported intervals; no correction/normalization/role/oracle routing',
        'condition':'Paddle BGR arm and EasyOCR own Gray64 firstpass from identical original192PNG corpus',
        'scope':'Posthoc diagnosis of consumed development only. No model/pair selection, new model calls, independent qualification or production activation',
        'additionalModelCalls':0,'qualification':False}
    write(a.output/'freeze.json',freeze)
    readers={}
    for name in FILES:
        for line in (ROOT/'research-results'/name).read_text().splitlines():
            r=json.loads(line)
            if 'condition' in r and r['condition']!='bgr': continue
            model=r.get('model') or ('easyocr-japanese-g1' if 'japanese-g1' in name else 'easyocr-japanese-g2')
            rows=readers.setdefault(model,{})
            if r['id'] in rows: raise ValueError('Duplicate reader ordinal')
            rows[r['id']]=r
    if set(readers)!=MODELS or any(len(rows)!=192 for rows in readers.values()):raise ValueError('Incomplete readers')
    # All raw readers closed before this audit; no literal enters pair equality.
    gold={r['id']:r for r in json.loads(goldfile.read_text())}
    if len(gold)!=192 or any(set(rows)!=set(gold) for rows in readers.values()):raise ValueError('Ordinal mismatch')
    pairs=[]
    for left,right in itertools.combinations(sorted(readers),2):
        groups={};wrong=[]
        for id,g in gold.items():
            l,r=readers[left][id],readers[right][id];same=bool(l['text']) and l['text']==r['text'];accepted=same and above(l) and above(r)
            exact=l['text']==g['literal'];v=groups.setdefault(g['cohort']+'/'+g['kind'],{'rows':0,'nonemptyAgreements':0,'agreementCorrect':0,'agreementWrong':0,'correctBothAbove':0,'wrongBothAbove':0})
            v['rows']+=1;v['nonemptyAgreements']+=same;v['agreementCorrect']+=same and exact;v['agreementWrong']+=same and not exact
            v['correctBothAbove']+=accepted and exact;v['wrongBothAbove']+=accepted and not exact
            if accepted and not exact:wrong.append({'id':id,'cohort':g['cohort'],'kind':g['kind'],'literal':g['literal'],
                'sharedRawText':l['text'],'leftOwnMinimum':l['minimumConfidence'],'rightOwnMinimum':r['minimumConfidence']})
        pairs.append({'left':left,'right':right,'groups':groups,'wrongBothAbove':wrong})
    if sha(goldfile)!=freeze['oracleSha256'] or any(sha(ROOT/'research-results'/n)!=h for n,h in freeze['files'].items()):raise ValueError('Frozen audit inputs changed')
    write(a.output/'report.json',{'pairs':pairs,'additionalModelCalls':0,'qualification':False,'modelActivation':False,
        'limits':'Agreement of correlated OCR readers does not establish original text or ink ownership. MinCTC scores are uncalibrated and not transferred between readers.'})
    for pair in pairs:
        print(json.dumps({'left':pair['left'],'right':pair['right'],'correctBothAbove':sum(v['correctBothAbove'] for v in pair['groups'].values()),'wrongBothAbove':len(pair['wrongBothAbove'])}),flush=True)


if __name__=='__main__':main()
