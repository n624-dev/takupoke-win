"""Assess every preregistered arm only after successful closed outputs."""
import argparse
import collections
import json
from pathlib import Path
from prepare import sha, write

def distance(a,b):
    previous=list(range(len(b)+1))
    for n,x in enumerate(a,1):
        current=[n]
        for m,y in enumerate(b,1):current.append(min(current[-1]+1,previous[m]+1,previous[m-1]+(x!=y)))
        previous=current
    return previous[-1]

def main():
    p=argparse.ArgumentParser();p.add_argument('--study',type=Path,required=True);a=p.parse_args();out=a.study
    receipt=json.loads((out/'receipt.json').read_text());assert receipt['exitCode']==0 and sha(out/'raw.jsonl')==receipt['raw']['sha256']
    freeze=json.loads((out/'freeze.json').read_text());assert sha(out/'oracle.json')==freeze['oracle']['sha256']
    inputs=json.loads((out/'inputs.json').read_text());gold={r['id']:r for r in json.loads((out/'oracle.json').read_text())}
    rows={r['id']:r for r in map(json.loads,(out/'raw.jsonl').read_text().splitlines())};assert set(rows)==set(gold)
    protocol=json.loads((out/'protocol.json').read_text())
    framings=protocol.get('armFramings',['original','ink2'] if any('cropPolicy' in v for r in rows.values() for v in r.get('variants',[])) else ['original'])
    keys=[(model,decoder,policy,framing) for model in protocol.get('armModels',['v1','v2','mean-v1-v2']) for decoder in protocol.get('armDecoders',['greedy','beam8']) for policy in ['same-length','bounded-code'] for framing in framings]
    scored=[];examples=[]
    for key in keys:
        counters=collections.Counter();before_edits=after_edits=characters=0
        for id,r in rows.items():
            g=gold[id];variants={(v['name'],v['decoder'],v['lengthPolicy'],v.get('cropPolicy','original')):v for v in r.get('variants',[])}
            v=variants.get(key);new=r.get('baselineText','') if v is None else v['text'];old=r.get('baselineText','')
            if inputs['kind']=='native':
                assert old==g['beforeLine'],(id,old,g['beforeLine'])
                fields=[]
                for f in g['fields']:
                    assert old.endswith(f['before']),(id,old,f)
                    prefix=old[:-len(f['before'])] if f['before'] else old
                    # Prefix is derived from the OLD reading's provenance, not gold.
                    changed=new[len(prefix):] if new.startswith(prefix) else new
                    fields.append((f['before'],changed,f['expected'],f['role']))
            else:fields=[(old,new,g['value'],g.get('group','blank'))]
            for before,after,expected,role in fields:
                counters['fields']+=1;characters+=len(expected)
                before_edits+=distance(before,expected);after_edits+=distance(after,expected)
                correct_before=before==expected;correct_after=after==expected
                counters['beforeExact']+=correct_before;counters['afterExact']+=correct_after
                counters['gains']+=not correct_before and correct_after;counters['losses']+=correct_before and not correct_after
                counters['changed']+=before!=after
                if role=='room':counters['roomFields']+=1;counters['roomBeforeExact']+=correct_before;counters['roomAfterExact']+=correct_after
                if not expected and after:counters['nonemptyBlank']+=1
                if before!=after:examples.append(dict(arm=key,id=id,role=role,before=before,after=after,expected=expected,gain=not correct_before and correct_after,loss=correct_before and not correct_after))
            if v is not None and v['selected']:
                counters['selectedCrops']+=1;counters['selectedCodeBelowNativeFloor']+=any(p['Confidence']<.8 for p in v['pieces'])
        scored.append(dict(arm=key,**dict(counters),characters=characters,beforeEdits=before_edits,afterEdits=after_edits))
    write(out/'report.json',dict(scope=inputs['scope'],kind=inputs['kind'],rawSHA256=sha(out/'raw.jsonl'),frozenArms=keys,scores=scored,eligibleCrops=sum(r.get('eligible',False) for r in rows.values()),baselineNativeIdentity=inputs['kind']=='native',limits='Consumed component development, not new accuracy/whole-document credit. Native page outputs preserve all fields but do not rerun full acquisition/parser for each proposal. Confidence is uncalibrated; below-floor counts concern selected code pieces, not document review plans.'))
    write(out/'changes.json',examples)
    for row in scored:print(json.dumps(row,ensure_ascii=False))

if __name__=='__main__':main()
