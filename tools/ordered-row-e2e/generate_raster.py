#!/usr/bin/env python3
"""Lossless paired image PDFs from the newly invented source cohort only."""
import argparse
import hashlib
import json
from pathlib import Path
import fitz

p=argparse.ArgumentParser()
p.add_argument('--fixtures',type=Path,required=True)
a=p.parse_args()
assert (a.fixtures/'.ordered-row-e2e-owned').read_text().strip()=='v1'
original=json.loads((a.fixtures/'manifest.json').read_text(encoding='utf-8'))
cases=[]
for item in original['cases']:
    if item['expect']!='exact':
        continue
    source=a.fixtures/item['file']
    assert hashlib.sha256(source.read_bytes()).hexdigest()==item['sha256']
    path=a.fixtures/(item['case']+'-image.pdf')
    pages=[]
    with fitz.open(source) as pdf,fitz.open() as raster:
        for page in pdf:
            pixels=page.get_pixmap(matrix=fitz.Matrix(2,2),alpha=False)
            raw=pixels.tobytes('png')
            target=raster.new_page(width=page.rect.width,height=page.rect.height)
            target.insert_image(target.rect,stream=raw)
            pages.append({'width':pixels.width,'height':pixels.height,'rgbSha256':hashlib.sha256(pixels.samples).hexdigest()})
        raster.save(path,garbage=4,deflate=True,no_new_id=True)
    with fitz.open(path) as check:
        assert len(check)==5 and all(not page.get_text().strip() for page in check)
    cases.append({**item,'case':item['case']+'-image','file':path.name,
        'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'bytes':path.stat().st_size,
        'sourcePdfSha256':item['sha256'],'embeddedPages':pages})
manifest={'recipe':'ordered-row-image-e2e-v1','provenance':'Paired lossless image PDFs from independently invented source originals; no OCR layer or school input',
          'generatorSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'cases':cases}
(a.fixtures/'raster-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'recipe':manifest['recipe'],'cases':[{k:c[k] for k in ('case','sha256','bytes','sourcePdfSha256')} for c in cases]}))
