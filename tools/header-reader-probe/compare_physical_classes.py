#!/usr/bin/env python3
"""Nonadoptable comparison on every physical class-column cell of two invented pages.

Only original embedded pixels and ordinal IDs enter the pinned reader. Answers
are read after all calls; no class vocabulary, OCR answer or painted text bounds
select crops. Prior full-document failures and blind I/l controls stay unchanged.
"""
import argparse
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import urllib.request
import fitz


def sha(data):
    return hashlib.sha256(data).hexdigest()


def longest_run(values):
    best = start = length = 0
    best_start = 0
    for index, dark in enumerate(values):
        if dark:
            if length == 0: start = index
            length += 1
            if length > best: best, best_start = length, start
        else: length = 0
    return best_start, best_start + best - 1, best


def observed_axes(lanes):
    groups = []
    for axis, start, end in lanes:
        if groups and axis == groups[-1][-1][0] + 1 and abs(start-groups[-1][-1][1]) <= 2 and abs(end-groups[-1][-1][2]) <= 2:
            groups[-1].append((axis,start,end))
        else: groups.append([(axis,start,end)])
    return [(sum(x[0] for x in group)/len(group), *max(group,key=lambda x:x[2]-x[1])[1:]) for group in groups]


def main(args):
    args.owned.mkdir(exist_ok=False)
    try:
        root=Path(__file__).resolve().parents[2]
        spec=importlib.util.spec_from_file_location('invented_readable',root/'tools/ordered-row-e2e/generate_readable.py')
        generator=importlib.util.module_from_spec(spec);spec.loader.exec_module(generator)
        if args.font_family=='serif':
            # New font condition, pinned before evaluating its recognition.
            generator.public_font.FONT_URL='https://raw.githubusercontent.com/google/fonts/295d98a7a0c17c68f1341eaeea354e7960ea70d3/ofl/notoserifjp/NotoSerifJP%5Bwght%5D.ttf'
            generator.public_font.FONT_SHA='4c6b4670b73d0843c7b2d30b9e2fbcfa596aef6fd3937f894929ab0b8d659d1e'
            license_bytes=urllib.request.urlopen('https://raw.githubusercontent.com/google/fonts/295d98a7a0c17c68f1341eaeea354e7960ea70d3/ofl/notoserifjp/OFL.txt',timeout=60).read()
            assert sha(license_bytes)=='5e0da210fb04058a8c0087985d2d456b931c2579811a49655721d3cf0c36b6d6'
            (args.owned/'serif-OFL.txt').write_bytes(license_bytes)
        if args.printed_ell_control:
            # A separate deliberately noncanonical glyph control. Neither the
            # class list nor its expected answers enters the recognition child.
            generator.CLASSES=[value.replace('AI_','Al_') for value in generator.CLASSES]
        manifest=generator.generate(args.owned/'cohort')
        inputs=[];inventory=[]
        for case in manifest['cases']:
            path=args.owned/'cohort'/case['file']
            assert sha(path.read_bytes())==case['sha256']
            with fitz.open(path) as pdf:
                images=pdf[0].get_images(full=True)
                assert len(images)==1
                image=fitz.Pixmap(pdf,images[0][0])
                assert image.n==3 and not image.alpha
                rgb=image.samples;w,h=image.width,image.height
                assert sha(rgb)==case['embeddedPages'][0]['rgbSha256']
                def dark(x,y): return sum(rgb[(y*w+x)*3:(y*w+x)*3+3]) < 450
                vs=[]
                for x in range(w):
                    start,end,length=longest_run(dark(x,y) for y in range(h))
                    if length>h/2: vs.append((x,start,end))
                vertical=observed_axes(vs)
                assert len(vertical)>=2
                left,right=vertical[0][0],vertical[1][0]
                hs=[]
                for y in range(h):
                    start,end,length=longest_run(dark(x,y) for x in range(w))
                    if start<=left+.3 and end>=right-.3: hs.append((y,start,end))
                horizontal=[x[0] for x in observed_axes(hs)]
                captures=[]
                import math
                for top,bottom in zip(horizontal,horizontal[1:]):
                    x1,y1=math.ceil(left)+2,math.ceil(top)+2
                    x2,y2=math.floor(right)-2,math.floor(bottom)-2
                    if x2-x1<=8 or y2-y1<=8: continue
                    pixels=b''.join(rgb[(y*w+x1)*3:(y*w+x2)*3] for y in range(y1,y2))
                    if min(pixels)==255: continue
                    physical_box=[x1,y1,x2-x1,y2-y1]
                    iw,ih=x2-x1,y2-y1
                    painted=[(i//3 % iw,i//3//iw) for i in range(0,len(pixels),3) if min(pixels[i:i+3])<255]
                    original_ink_height=max(y for x,y in painted)-min(y for x,y in painted)+1
                    if args.ink_crop:
                        # Fixed source-pixel margin, all painted pixels retained.
                        # No recognition answer, character correction or resampling.
                        a=max(0,min(x for x,y in painted)-4);b=min(iw,max(x for x,y in painted)+5)
                        c=max(0,min(y for x,y in painted)-4);d=min(ih,max(y for x,y in painted)+5)
                        pixels=b''.join(pixels[(y*iw+a)*3:(y*iw+b)*3] for y in range(c,d))
                        x1+=a;y1+=c;iw=b-a;ih=d-c
                    bgra=bytearray(iw*ih*4)
                    for channel in range(3): bgra[channel::4]=pixels[2-channel::3]
                    bgra[3::4]=bytes([255])*(iw*ih)
                    index=len(inputs)
                    inputs.append(dict(Index=index,Width=iw,Height=ih,Bgra=base64.b64encode(bgra).decode()))
                    captures.append(dict(index=index,cropBox=[x1,y1,iw,ih],physicalCellInterior=physical_box,originalInkHeight=original_ink_height,normalizedInkHeight48=original_ink_height*48/ih,sourceRGBSHA256=sha(rgb),inputRGBSHA256=sha(pixels)))
                inventory.append((case,captures))
        assert len(inputs)==18, 'Physical inventory differs; never fill missing crops'
        pixels=args.owned/'pixels-only.json';pixels.write_text(json.dumps(inputs))
        command=[args.dotnet,str(args.dll),str(args.model),str(pixels),str(args.alphabet),'physical-class-crops-v1']
        child=subprocess.run(command,capture_output=True,text=True,timeout=120)
        assert child.returncode==0,child.stderr[:2000]
        native=json.loads(child.stdout)
        assert native['calls']==18 and len(native['outputs'])==18 and not native['qualified']
        # Only now read the generator's order for assertion-only evaluation.
        rows=[]
        for case,captures in inventory:
            expected=case['layout']['classOrder'][:9]
            assert len(captures)==len(expected)
            for original,value in zip(captures,expected):
                result=native['outputs'][original['index']]
                assert result['Index']==original['index']
                rows.append(dict(case=case['case'],**original,**result,expected=value,exact=result['text']==value))
        report=dict(recipe='physical-class-column-English-v5-original-pixels-v1',inputRegion='all nonwhite pixels plus fixed4px margin' if args.ink_crop else 'physical cell interior',
            printedEllControl=args.printed_ell_control,
            fontFamily=args.font_family,publicFontSHA256=generator.public_font.FONT_SHA,
            fontLicenseSHA256='5e0da210fb04058a8c0087985d2d456b931c2579811a49655721d3cf0c36b6d6' if args.font_family=='serif' else generator.public_font.LICENSE_SHA,
            calls=18,correct=sum(r['exact'] for r in rows),obligations=18,
            incorrectAboveNativeThreshold=sum(not r['exact'] and r['minimumConfidence'] is not None and r['minimumConfidence']>=.8 for r in rows),
            paddingUnsupported=sum(not r['paddingSupported'] for r in rows),
            modelBytes=args.model.stat().st_size,modelSHA256=sha(args.model.read_bytes()),
            runtime=native['runtime'],qualified=False,adoptionCalls=0,
            scope='Two first-page original class columns only; no full-document success or correction',outputs=rows)
        if args.printed_ell_control:
            controls=[row for row in rows if row['expected'].startswith('Al_')]
            assert len(controls)==4
            report['controlObligations']=len(controls)
            report['correctPrintedEll']=sum(row['exact'] for row in controls)
            report['printedEllReadAsCanonicalI']=sum(row['text']==row['expected'].replace('Al_','AI_') for row in controls)
        print('PHYSICAL_CLASS_READER '+json.dumps(report,ensure_ascii=False))
    finally:
        shutil.rmtree(args.owned)


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--owned',required=True,type=Path)
    parser.add_argument('--dll',required=True,type=Path)
    parser.add_argument('--model',required=True,type=Path)
    parser.add_argument('--alphabet',required=True,type=Path)
    parser.add_argument('--ink-crop',action='store_true')
    parser.add_argument('--printed-ell-control',action='store_true')
    parser.add_argument('--font-family',choices=['sans','serif'],default='sans')
    parser.add_argument('--dotnet',default='dotnet')
    main(parser.parse_args())
