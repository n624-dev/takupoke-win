#!/usr/bin/env python3
"""Independent dense source-only final cases, frozen after the colour change."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import urllib.request

from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas

REVISION='295d98a7a0c17c68f1341eaeea354e7960ea70d3'
FONTS=[('kosugi','apache/kosugi/Kosugi-Regular.ttf','f5e81d6a6b865d9b88c54d2d3c16bcaa3b239dfcefaf2a62976ac9dc7574bab7',771409),
       ('sawarabi','ofl/sawarabimincho/SawarabiMincho-Regular.ttf','86373c619710d1528aac3a4d47ce96834101bb38b057235b39e47b61ba143c6d',590317)]
CLASSES=['1_1','1_2','1_3']+[f'{y}_{c}' for y in range(2,6) for c in ('CN','ES','IT')]+['AI_1','AI_2']
COLOURS=[(1,0,0),(0,0,1),(.15,.5,.2),(.4,.4,.4)]
DAYS=['月','火','水','木','金']


def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()


def draw(out,name,seed,font,variant):
    rng=random.Random(seed);classes=CLASSES[:];rng.shuffle(classes)
    days=list(range(1,6));rng.shuffle(days)
    margin,label,column,row=33,99,137,88
    width,height=margin+label+8*column+31,97+17*row+35
    path=out/(name+'.pdf');c=canvas.Canvas(str(path),pagesize=(width,height),invariant=1,pageCompression=1,initialFontName=font)
    c.setAuthor('Entirely invented final source design');c.setCreator('independent-source-colour-final-v1')
    slots=[];ordinal=0
    def text(value,x,baseline,size=10):
        nonlocal ordinal
        if pdfmetrics.stringWidth(value,font,size)>column-18:raise ValueError('Independent text would be clipped')
        c.setFont(font,size);c.setFillColorRGB(*COLOURS[ordinal%len(COLOURS)]);ordinal+=1
        c.drawString(x,height-baseline,value)
    def line(x1,y1,x2,y2):
        c.setStrokeColorRGB(.12,.2,.55);c.line(x1,height-y1,x2,height-y2)
    for day in days:
        text('令和10年度 前期',margin,26,12)
        bx,right,bottom=margin+label,margin+label+8*column,97+17*row
        c.setLineWidth(.7)
        line(bx,37,right,37);line(bx,37,bx,65);line(right,37,right,65)
        if not(variant=='missing-day' and day==4):text(DAYS[day-1],bx+3.5*column,55,12)
        for y in [65,97]+[97+n*row for n in range(1,18)]:line(margin,y,right,y)
        line(margin,65,margin,bottom)
        for col in range(9):line(bx+col*column,65,bx+col*column,bottom)
        for period in range(1,9):text(str(period),bx+(period-.5)*column,85,11)
        for index,cls in enumerate(classes):
            top=97+index*row;text(cls,margin+7,top+row/2+4,11)
            for period in range(1,9):
                serial=rng.randrange(10000,99000);marker=rng.choice(['I1l','O0','0O','lI1','B2O3'])
                gap=' ' if serial%11==0 else ''
                values=[f'架空術甲{gap}{serial}',f'仮想指南乙{serial}',f'試室丙{marker}{gap}{serial}']
                slots.append({'className':cls,'weekday':day,'period':period,
                    'lessons':[dict(zip(('subject','teacher','room'),values))]})
                # Deliberately vary paint order. Source-line IDs remain real,
                # while the physical top/middle/bottom rows establish roles.
                for role in ((2,0,1) if serial%2 else (0,1,2)):
                    if variant=='missing-body' and day==2 and index==0 and period==1 and role==1:continue
                    text(values[role],bx+(period-1)*column+7,top+18+role*26,9.5)
        c.showPage()
    c.save();slots.sort(key=lambda s:(s['className'],s['weekday'],s['period']))
    return {'case':name,'file':path.name,'sha256':digest(path),'bytes':path.stat().st_size,
        'expect':'exact' if variant=='positive' else 'refuse','seed':seed,
        'oracle':{'schoolYear':2028,'term':'前期','slots':slots},'font':font,
        'variant':variant,'pageOrder':days,'classOrder':classes}


def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);args=p.parse_args()
    out=args.output;out.mkdir(parents=True,exist_ok=False);(out/'.pdf-source-final-owned').write_bytes(b'v1\n')
    freeze={'recipe':'independent-source-colour-final-v1','candidateCommit':'c064ea2a1a0d57516baf9bdc12125a0705e7ae9f',
        'generatorSHA':digest(Path(__file__)),'fonts':FONTS,'colours':COLOURS,
        'variants':['positive','missing-day','missing-body'],'qualification':'source-only; no OCR/model activation'}
    (out/'pre-generation-freeze.json').write_text(json.dumps(freeze,indent=2)+'\n')
    cases=[]
    for name,source,sha,seed in FONTS:
        path=out/(name+'.ttf');path.write_bytes(urllib.request.urlopen(f'https://raw.githubusercontent.com/google/fonts/{REVISION}/{source}',timeout=60).read())
        if digest(path)!=sha:raise ValueError('New public source font mismatch')
        pdfmetrics.registerFont(TTFont(name,str(path)))
        for variant in freeze['variants']:cases.append(draw(out,name+'-'+variant,seed,name,variant))
    manifest={**freeze,'cases':cases,'scope':'Independent unused source contents/fonts/layout/order; full source-only final, not trained OCR final',
        'obligationsPerPositive':{'pages':5,'slots':680,'bodyValues':2040}}
    (out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'recipe':manifest['recipe'],'cases':[{k:c[k] for k in ('case','sha256','expect')} for c in cases]}))


if __name__=='__main__':main()
