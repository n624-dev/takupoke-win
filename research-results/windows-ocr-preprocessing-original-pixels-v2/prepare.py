import hashlib,json
from pathlib import Path
import fitz
from PIL import Image
ROOT=Path(__file__).resolve().parent
SOURCE=Path('/workspace/recovery-research/windows-raster-independent-heldout-v3-20261004')
sha=lambda b:hashlib.sha256(b).hexdigest()
manifest=SOURCE/'manifest.json'
if sha(manifest.read_bytes())!='050b7640b6de309db2be78a932cf558f4d36b3b89c29b95f7737228e025079b9':raise ValueError('original manifest changed')
ids=['independent-Timetable-literal-乙','independent-Timetable-verifiedblank-丙']
records=[]
for row in json.loads(manifest.read_text(encoding='utf-8'))['fixtures']:
 if row['id'] not in ids:continue
 folder=SOURCE/row['id'];pdfpath=folder/'fictional.pdf';pdf=pdfpath.read_bytes()
 if sha(pdf)!=row['pdfSha256']:raise ValueError('original PDF changed')
 oraclebytes=(folder/'literal-oracle.json').read_bytes()
 if sha(oraclebytes)!=row['oracleSha256']:raise ValueError('original metadata pin changed')
 pagesmetadata=json.loads(oraclebytes)['pages']
 with fitz.open(stream=pdf,filetype='pdf') as document:
  if len(document)!=5:raise ValueError('expected five original pages')
  pages=[]
  for pageindex,page in enumerate(document):
   if page.get_text().strip():raise ValueError('expected image-only original')
   images=page.get_images(full=True)
   if len(images)!=1:raise ValueError('ambiguous image input')
   pix=fitz.Pixmap(document,images[0][0])
   if pix.n!=3 or pix.alpha:raise ValueError('unexpected original image channels')
   pngpath=folder/pagesmetadata[pageindex]['imageFile'];png=pngpath.read_bytes()
   if sha(png)!=pagesmetadata[pageindex]['imageSha256']:raise ValueError('original PNG pin changed')
   with Image.open(pngpath) as image:
    if image.size!=(pix.width,pix.height) or image.convert('RGB').tobytes()!=pix.samples:raise ValueError('PDF embedded pixels differ from original PNG')
    bgra=image.convert('RGBA').tobytes('raw','BGRA')
   data=ROOT/'pixels'/row['id'];data.mkdir(parents=True,exist_ok=True)
   path=data/f'page-{pageindex+1}.bgra';path.write_bytes(bgra)
   pages.append({'page':pageindex+1,'width':pix.width,'height':pix.height,'path':str(path),'bgraSha256':sha(bgra),'pngSha256':sha(png)})
  records.append({'id':row['id'],'pdfPath':str(pdfpath),'pdfSha256':sha(pdf),'pdfBytes':len(pdf),'pages':pages,'formalOraclePath':str(folder/'literal-analysis-oracle.json'),'formalOracleSha256':row['formalOracleSha256']})
if len(records)!=2:raise ValueError('expected exact two originals')
(ROOT/'inputs.json').write_text(json.dumps({'scope':'Same lossless embedded original pixels; NOT Windows PDF-rendered pixels. Input metadata only, no gold text/roles/geometry/state. Formal oracle read only after OCR.','documents':records},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'documents':len(records),'pages':sum(len(r['pages']) for r in records),'modelCalls':0,'inputsSha256':sha((ROOT/'inputs.json').read_bytes())}))
