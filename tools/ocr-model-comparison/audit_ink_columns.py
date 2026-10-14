#!/usr/bin/env python3
"""Pixel-only split proposals; diagnose why ink coverage is not glyph proof."""
import argparse
import hashlib
import json
from pathlib import Path


def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


def main():
    from PIL import Image
    import numpy as np
    p=argparse.ArgumentParser();p.add_argument('--inputs',type=Path,required=True);p.add_argument('--oracle',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=False)
    rows=json.loads(a.inputs.read_text());root=a.inputs.parent
    freeze={'recipe':'physical-white-column-proposals-consumed-v1','inputsSha256':sha(a.inputs),'oracleSha256':sha(a.oracle),
        'sourceSha256':sha(Path(__file__)),'rule':'Every RGB component unequal255 is visible ink. Propose full-height groups separated by completely white columns. Keep gaps, absolute dimensions, baseline and all original ink. No character/space inference.',
        'scope':'Source-shape diagnosis only, consumed development, no recognition/model/pair selection','modelCalls':0,'qualification':False}
    write(a.output/'freeze.json',freeze)
    observations=[]
    with (a.output/'unjudged.jsonl').open('x',encoding='utf-8',newline='\n') as stream:
        for r in rows:
            path=root/r['file']
            if sha(path)!=r['sha256']:raise ValueError('Original frame changed')
            with Image.open(path) as image:rgb=np.asarray(image.convert('RGB'));height,width=rgb.shape[:2]
            ink=np.any(rgb!=255,axis=2);columns=ink.any(axis=0);groups=[];start=None
            for x,marked in enumerate(columns.tolist()+[False]):
                if marked and start is None:start=x
                if not marked and start is not None:groups.append([start,0,x-start,height]);start=None
            covered=np.zeros_like(ink)
            for x,y,w,h in groups:covered[y:y+h,x:x+w]=True
            if np.any(ink & ~covered):raise ValueError('Proposal would lose actual ink')
            record={'id':r['id'],'width':width,'height':height,'groups':groups,
                'gaps':[[left[0]+left[2],right[0]-left[0]-left[2]] for left,right in zip(groups,groups[1:])],
                'allVisibleInkCovered':True,'visiblePixels':int(ink.sum()),'sourceRect':r['sourceRect'],
                'limits':'A group may be a glyph part or multiple glyphs. A white gap does not prove a Unicode space.'}
            observations.append(record);stream.write(json.dumps(record,ensure_ascii=False,separators=(',',':'))+'\n')
    # Expected strings are consulted only after closed opaque split proposals.
    gold={r['id']:r for r in json.loads(a.oracle.read_text())['rows']};groups={};examples=[]
    for r in observations:
        g=gold[r['id']]
        if g['reject']:continue
        count=sum(c!=' ' for c in g['text']);v=groups.setdefault(g['kind'],{'rows':0,'groupCountEqualsPrintedCharacters':0,'moreGroupsThanCharacters':0,'fewerGroupsThanCharacters':0})
        v['rows']+=1;v['groupCountEqualsPrintedCharacters']+=len(r['groups'])==count
        v['moreGroupsThanCharacters']+=len(r['groups'])>count;v['fewerGroupsThanCharacters']+=len(r['groups'])<count
        if len(r['groups'])!=count and len(examples)<20:examples.append({'id':r['id'],'kind':g['kind'],'literal':g['text'],'printedCharacters':count,'proposedGroups':len(r['groups'])})
    report={'rows':len(observations),'allVisibleInkCovered':sum(r['allVisibleInkCovered'] for r in observations),
        'positiveGroups':groups,'countMismatchExamples':examples,'modelCalls':0,'qualification':False,
        'conclusion':'Ink conservation alone cannot justify treating groups as complete characters or moving an ASCII boundary; semantic crop/repair is not activated.'}
    if sha(a.oracle)!=freeze['oracleSha256'] or sha(a.inputs)!=freeze['inputsSha256']:raise ValueError('Audit identity changed')
    write(a.output/'report.json',report);print(json.dumps(report,ensure_ascii=False),flush=True)


if __name__=='__main__':main()
