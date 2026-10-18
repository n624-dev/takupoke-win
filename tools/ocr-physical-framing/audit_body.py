#!/usr/bin/env python3
"""Post-worker literal field diagnostics; never supply gold to acquisition."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

ROLES=['subject','teacher','room']


def contains(a,b):
    return all(b[k]>=a[k] for k in ['X','Y']) and all(b[k]+b[w]<=a[k]+a[w] for k,w in [('X','Width'),('Y','Height')])


def key(s,native):
    return (s['ClassName' if native else 'className'],str(s['Day' if native else 'weekday']),s['Period' if native else 'period'])


def audit(observation,gold):
    result={'id':observation['id'],'condition':observation['condition'],'nativeCalls':0,'formalReturned':observation.get('formal') is not None,
        'quality':'unassessed','qualification':False,'scope':'Literal diagnostics of already consumed development; no repair, adoption or full-document qualification'}
    document=observation.get('document')
    if observation['outcome']=='execution-error-unassessed' or document is None:
        result['failure']='Incomplete native source execution/document';return result
    expected={key(l,False):l['names'] for l in gold['timetable']['lessons']}
    if len(expected)!=len(gold['timetable']['lessons']):raise ValueError('Ambiguous oracle lessons')
    actual=Counter(key(s,True) for c in document['Cells'] for s in c['Slots'])
    required=Counter((s['className'],str(s['day']),s['period']) for s in gold['requiredSlotSet'])
    result['completeUniqueSlotSet']=actual==required and all(n==1 for n in actual.values())
    # Do not force unknown/wrong headers or omitted cells into oracle slots.
    if not result['completeUniqueSlotSet']:
        result['failure']='Actual source slots differ from complete required slots';return result
    sources={s['Id']:s for s in document['Sources']}
    if len(sources)!=len(document['Sources']):raise ValueError('Duplicate native source IDs')
    values=[];unassessed=0
    for cell in document['Cells']:
        slots=[key(s,True) for s in cell['Slots']]
        if not all(s in expected for s in slots):
            # Empty/scope diagnostics are separate; this tool scores body only.
            unassessed+=sum(3 for s in slots if s in expected);continue
        scopes=cell['RoleScopes']
        if len(scopes)!=3 or {s['Role'] for s in scopes}!={0,1,2} or any(s['LessonIndex']!=0 for s in scopes):
            unassessed+=3*len(slots);continue
        body=[sources[id] for id in cell['SourceIds']]
        labels=[id for s in scopes for id in s['LabelSourceIds']]
        if len(set(labels))!=len(labels) or not set(labels)<=set(cell['SourceIds']):
            unassessed+=3*len(slots);continue
        assigned={s['Role']:[a for a in body if a['Id'] not in labels and a['Page']==s['Page'] and contains(s['Box'],a['Box'])] for s in scopes}
        assigned_ids=[a['Id'] for atoms in assigned.values() for a in atoms]
        # Every original nonlabel source atom must belong to exactly one field.
        if len(assigned_ids)!=len(set(assigned_ids)) or set(assigned_ids)!=set(cell['SourceIds'])-set(labels):
            unassessed+=3*len(slots);continue
        for slot in slots:
            for role,atoms in assigned.items():
                if any(a.get('SourceOrder') is None or a.get('NativeConfidence') is None for a in atoms):
                    unassessed+=1;continue
                ordered=sorted(atoms,key=lambda a:a['SourceOrder'])
                literal=''.join(a['Text'] for a in ordered)
                confidence=min((a['NativeConfidence'] for a in atoms),default=None)
                values.append({'slot':list(slot),'role':ROLES[role],'actual':literal,'expected':expected[slot][ROLES[role]],
                    'exact':literal==expected[slot][ROLES[role]],'minimumNativeConfidence':confidence,
                    'aboveDiagnosticFloor':confidence is not None and confidence>=.8,'originalSourceIds':[a['Id'] for a in ordered]})
    aggregates=[]
    for role in ROLES:
        rows=[v for v in values if v['role']==role]
        aggregates.append({'role':role,'assessed':len(rows),'exact':sum(v['exact'] for v in rows),
            'incorrectAboveDiagnosticFloor':sum(not v['exact'] and v['aboveDiagnosticFloor'] for v in rows),
            'belowFloorOrEmpty':sum(not v['aboveDiagnosticFloor'] for v in rows)})
    result.update({'quality':'posthoc-body-diagnostic','expectedBodyFields':len(expected)*3,'assessedFields':len(values),'unassessedFields':unassessed,
        'aggregates':aggregates,'wrongFields':sum(not v['exact'] for v in values),'belowFloorFields':sum(not v['aboveDiagnosticFloor'] for v in values),
        'allBodyLiteralExact':len(values)==len(expected)*3 and unassessed==0 and all(v['exact'] for v in values),'fields':values})
    return result


def main():
    p=argparse.ArgumentParser();p.add_argument('--closed-raw',type=Path,required=True)
    p.add_argument('--oracle',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    raw=a.closed_raw.read_bytes();oracle=a.oracle.read_bytes()
    gold={g['id']:g for g in json.loads(oracle.decode('utf-8'))}
    observations=[json.loads(l) for l in raw.decode('utf-8').splitlines()]
    results=[audit(o,gold[o['id']]) for o in observations if gold[o['id']]['expect']=='exact']
    report={'recipe':'closed-native-literal-body-diagnostic-v1','rawSHA256':hashlib.sha256(raw).hexdigest(),
        'oracleSHA256':hashlib.sha256(oracle).hexdigest(),'sourceSHA256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'nativeCalls':0,'qualification':False,'observations':results}
    a.output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps([{k:v for k,v in r.items() if k!='fields'} for r in results],ensure_ascii=False))


if __name__=='__main__':main()
