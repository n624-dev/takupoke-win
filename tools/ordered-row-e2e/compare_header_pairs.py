#!/usr/bin/env python3
"""Blind isolated I/l header probe. No school input or adoption path.

The independent generator knows its invented literals; the reader receives only
ordinal-named pixels. One fixed reader recipe is measured once per image.
"""
import argparse
import base64
import csv
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import random
import shutil
import subprocess

import fitz
from reportlab.pdfgen import canvas


def digest(data):
    return hashlib.sha256(data).hexdigest()


def run(owned, tessdata, english_dll=None, english_model=None, dotnet='dotnet', ink_crop=False):
    owned.mkdir(exist_ok=False)
    try:
        (owned/'.header-pair-owned').write_text('v1\n')
        root = Path(__file__).resolve().parents[2]
        spec = importlib.util.spec_from_file_location('public_font', root/'tools/independent-wide-timetable/generate.py')
        font = importlib.util.module_from_spec(spec); spec.loader.exec_module(font)
        font.install_font(owned)
        model = english_model if english_dll else tessdata/'eng.traineddata'
        model_hash = digest(model.read_bytes())
        designs = [(size, phase, color, digit, letter) for size in (9, 10)
                   for phase in (0, .125, .25, .375) for color in ((0,0,0), (.05,.1,.3))
                   for digit in (1, 2) for letter in ('I', 'l')]
        random.Random(32452843).shuffle(designs)
        records = []
        pixels_only = []
        # Generate independently first; no expected content enters a reader call.
        for index, (size, phase, color, digit, letter) in enumerate(designs):
            text = f'A{letter}_{digit}'
            pdf = owned/f'{index:03}.pdf'
            c = canvas.Canvas(str(pdf), pagesize=(40,26), invariant=1, initialFontName='IndependentJP')
            c.setFont('IndependentJP', size); c.setFillColorRGB(*color)
            c.drawString(3+phase,11+phase,text); c.save()
            with fitz.open(pdf) as source:
                image = source[0].get_pixmap(matrix=fitz.Matrix(2,2), alpha=False)
                pixel_hash = digest(image.samples)
                original = image.samples
                points = [(i//3 % image.width,i//3//image.width) for i in range(0,len(original),3)
                          if min(original[i:i+3])<255]
                ink_height=max(y for x,y in points)-min(y for x,y in points)+1
                if ink_crop:
                    # Source-pixel operation only: include every nonwhite
                    # painted pixel, plus fixed4px. No reader answer is used.
                    x1=max(0,min(x for x,y in points)-4);x2=min(image.width,max(x for x,y in points)+5)
                    y1=max(0,min(y for x,y in points)-4);y2=min(image.height,max(y for x,y in points)+5)
                    stride=image.width*3
                    cropped=b''.join(original[y*stride+x1*3:y*stride+x2*3] for y in range(y1,y2))
                    image = fitz.Pixmap(fitz.csRGB,x2-x1,y2-y1,cropped,False)
                image.save(str(owned/f'{index:03}.png'))
                rgb = image.samples
                bgra = bytearray(image.width*image.height*4)
                for channel in range(3):bgra[channel::4]=rgb[2-channel::3]
                bgra[3::4]=bytes([255])*(image.width*image.height)
                pixels_only.append(dict(Index=index,Width=image.width,Height=image.height,Bgra=base64.b64encode(bgra).decode()))
            records.append(dict(index=index, expected=text, letter=letter, pixels=size*2,
                                phase=phase, color=color, rgbSHA256=pixel_hash,
                                originalInkHeight=ink_height,normalizedInkHeight48=ink_height*48/image.height,
                                inputWidth=image.width,inputHeight=image.height,inputRGBSHA256=digest(image.samples)))
        outputs = []
        if english_dll:
            (owned/'pixels-only.json').write_text(json.dumps(pixels_only))
            child=subprocess.run([dotnet,str(english_dll),str(english_model),str(owned/'pixels-only.json')],capture_output=True,text=True,timeout=120)
            assert child.returncode==0, child.stderr[:2000]
            native=json.loads(child.stdout)
            outputs=[dict(index=r.pop('Index'),error=None,**r) for r in native['outputs']]
            model=english_model;model_hash=digest(model.read_bytes())
        for record in ([] if english_dll else records):
            result = subprocess.run(['tesseract', str(owned/f"{record['index']:03}.png"), 'stdout',
                                     '--tessdata-dir',str(tessdata),'-l','eng','--oem','1','--psm','7','tsv'],
                                    capture_output=True, text=True, encoding='utf-8', timeout=20,
                                    env={**__import__('os').environ, 'OMP_THREAD_LIMIT':'1'})
            if result.returncode:
                outputs.append(dict(index=record['index'], error=result.returncode, text=None, minimumConfidence=None))
                continue
            words = [r for r in csv.DictReader(io.StringIO(result.stdout), delimiter='\t')
                     if r['level']=='5' and r['text']]
            outputs.append(dict(index=record['index'], text=''.join(r['text'] for r in words),
                                minimumConfidence=min((float(r['conf']) for r in words),default=None),
                                wordCount=len(words), error=None))
        # Inspect literal answers only after all planned reader calls returned.
        rows = [{**r, **o, 'exact':r['expected']==o['text']} for r,o in zip(records,outputs)]
        assert all(r['index']==o['index'] for r,o in zip(records,outputs))
        positives = [r for r in rows if r['letter']=='I']
        negatives = [r for r in rows if r['letter']=='l']
        pairs = {}
        for r in records:pairs.setdefault((r['pixels'],r['phase'],tuple(r['color']),r['expected'][-1]),{})[r['letter']]=r['rgbSHA256']
        report = dict(recipe='isolated-header-pair-English-v3-native-normalization-v1' if english_dll else 'isolated-header-pair-eng-psm7-v1', scope='isolated invented original glyph pairs; not native table recovery',
                      engine=native['runtime'] if english_dll else subprocess.check_output(['tesseract','--version'],text=True).splitlines()[0],
                      engineSHA256=digest(english_dll.read_bytes()) if english_dll else digest(Path(shutil.which('tesseract')).read_bytes()),
                      modelBytes=model.stat().st_size,modelSHA256=model_hash,publicFontSHA256=font.FONT_SHA,
                      calls=len(rows),correctCanonical=sum(r['exact'] for r in positives),canonicalObligations=len(positives),
                      inputRegion='all original painted pixels plus fixed4px margin' if ink_crop else 'original80x52 frame',
                      exactNoncanonical=sum(r['exact'] for r in negatives),noncanonicalObligations=len(negatives),
                      invalidPrintedHeaderReadAsCanonical=sum(r['text'] in ('AI_1','AI_2') for r in negatives),
                      executionErrors=sum(r['error'] is not None for r in rows),qualified=False,
                      sourcePairs=len(pairs),sourceDistinctPairs=sum(pair['I']!=pair['l'] for pair in pairs.values()),
                      paddingUnsupportedRows=sum(r.get('paddingSupported') is False for r in rows),
                      incorrectAboveNativeThreshold=sum(not r['exact'] and r.get('minimumConfidence') is not None and r['minimumConfidence'] >= (.8 if english_dll else 80) for r in rows),
                      alignedLetterPosition=sum(len(r['text'] or '')==4 and r['text'][0]=='A' and r['text'][2:]==r['expected'][2:] for r in rows),
                      alignedCanonicalReadAsEll=sum(r['text']==r['expected'].replace('I','l') for r in positives),
                      alignedNoncanonicalReadAsI=sum(r['text']==r['expected'].replace('l','I') for r in negatives),
                      outputsSHA256=digest(json.dumps(rows,sort_keys=True,ensure_ascii=False).encode()),
                      mismatches=[r for r in rows if not r['exact']])
        print(json.dumps(report, ensure_ascii=False))
    finally:
        shutil.rmtree(owned)


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--owned',required=True,type=Path)
    p.add_argument('--tessdata',type=Path,default=Path('/usr/share/tesseract-ocr/5/tessdata'))
    p.add_argument('--english-dll',type=Path);p.add_argument('--english-model',type=Path);p.add_argument('--dotnet',default='dotnet')
    p.add_argument('--ink-crop',action='store_true')
    args=p.parse_args()
    if bool(args.english_dll)!=bool(args.english_model):p.error('Both English runtime and pinned model are required')
    run(args.owned,args.tessdata,args.english_dll,args.english_model,args.dotnet,args.ink_crop)
