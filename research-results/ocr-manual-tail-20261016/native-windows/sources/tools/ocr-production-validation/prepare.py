#!/usr/bin/env python3
"""Reused invented three-class regression pages; never new heldout credit."""
import argparse, hashlib, json, random, sys, shutil
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'ocr-physical-framing'))
from run import sha, asset, write
FONTS = [
 ('sawarabigothic','SawarabiGothic-Regular.ttf',1902848,'04f2f9111bb69cb8d3706c27b44879c2c0fac593a11edf5cfe8cec1efc92c789'),
 ('sawarabimincho','SawarabiMincho-Regular.ttf',1084300,'86373c619710d1528aac3a4d47ce96834101bb38b057235b39e47b61ba143c6d')]


p=argparse.ArgumentParser();p.add_argument('--fonts',type=Path,required=True);p.add_argument('--models',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
a=p.parse_args();a.output.mkdir(exist_ok=False,parents=True)
# Register all generation decisions before drawing or recognition.
fonts=[]
for family,file,size,digest in FONTS:
 path=a.fonts/file
 if path.stat().st_size!=size or sha(path)!=digest:raise ValueError('Font pin changed')
 fonts.append(path)
write(a.output/'generation-freeze.json',{'recipe':'production-complete-three-class-final-v1','sourceSHA256':sha(Path(__file__)),
 'fonts':FONTS,'seedBase':20261015017,'classes':['1_1','2_IT','AI_1'],'days':5,'periods':8,'pages':5,
 'bodyFontPixels':20,'headerFontPixels':24,'width':2237,'height':415,'variants':['positive','missing-day','missing-class'],
 'plannedArms':['v5-production','hybrid-production'], 'prospectiveCriteria':{'allPositiveSlotsAndFields':True,'maximumCERPerFont':.03,'maximumCorrectionsAndReviewUnion':128,'negativeDocumentsRefused':True,'maximumRSSBytes':6*1024**3,'zeroErrorsRequired':False},'scope':'Previously consumed fictional layout/content/fonts, repeated for regression. Historical bounds retained, not new heldout or real-school qualification. Windows-rendered PDFs required.',
 'metrics':['slot coverage','literal CER','field error count','uncertain field count','high-confidence wrong fields','runtime','RSS'],
 'zeroErrorsRequired':False,'manualTypingBudget':128,'reviewBudget':128})
for name,digest in [('det.onnx','a431985659dc921974177a95adcfbb90fd9e51989a5e04d70d0b75f597b6e61d'),('rec.onnx','da72dc72ca4dc220df0dfde68c1dedc31c58d3e76a25871122e5056227d50092'),('alphabet.json','d858428fbe0ada92b439a5f277b51bc1b318fd985d80946a3ce859d7f2446a92')]:
 path=a.models/name
 if sha(path)!=digest:raise ValueError('Production asset pin changed')
 shutil.copyfile(path,a.output/name)

documents=[];gold=[]
for ordinal,font_path in enumerate(fonts):
 for variant in ('positive','missing-day','missing-class'):
  rng=random.Random(20261015017+ordinal);pages=[];slots=[];lessons=[];id=f'final-{ordinal}-{variant}'
  for day in range(1,6):
   width,height=2237,415;label,col,top,row=100,267,108,102
   image=Image.new('RGB',(width,height),'white');pen=ImageDraw.Draw(image)
   face=ImageFont.truetype(str(font_path),20);header=ImageFont.truetype(str(font_path),24)
   def text(value,x,y,font=face):pen.text((x,y),value,font=font,fill='black')
   text('2029年度 前期',8,0,header)
   pen.rectangle((label,36,width-1,70),outline='black',width=1)
   if not(variant=='missing-day' and day==1):text(['月','火','水','木','金'][day-1],label+4*col,36,header)
   for y in [70,top]+[top+n*row for n in range(1,4)]:pen.line((0,y,width-1,y),fill='black',width=1)
   pen.line((0,70,0,height-1),fill='black',width=1)
   for n in range(9):pen.line((label+n*col,70,label+n*col,height-1),fill='black',width=1)
   for period in range(1,9):text(str(period),label+(period-1)*col+col//2,73,header)
   for idx,cls in enumerate(['1_1','2_IT','AI_1']):
    if not(variant=='missing-class' and idx==2 and day==1):text(cls,5,top+idx*row+33,header)
    for period in range(1,9):
     serial=rng.randrange(10,100)
     values=[f'架空{rng.choice(["国語","数学","理科","英語"])}{serial}',f'仮想{rng.choice(["田中","佐藤","高橋"])}{serial}',f'架空室{rng.choice(["A","B","O0","I1l"])}{serial}']
     slots.append({'className':cls,'day':str(day),'period':period})
     lessons.append({'className':cls,'weekday':day,'period':period,'names':dict(zip(['subject','teacher','room'],values))})
     for role,(prefix,value) in enumerate(zip(['科目：','担当：','教室：'],values)):
      if pen.textlength(prefix+value,font=face)>col-20:raise ValueError('Whole printed line would exceed its cell')
      text(prefix+value,label+(period-1)*col+8,top+idx*row+4+role*31)
   path=a.output/f'{id}-p{day}.bgra';path.write_bytes(image.convert('RGBA').tobytes('raw','BGRA'))
   pages.append({'page':day,'width':width,'height':height,'pixels':asset(path,a.output)})
  identity=hashlib.sha256(json.dumps(pages,sort_keys=True).encode()).hexdigest()
  documents.append({'id':id,'originalPDFSHA256':identity,'pages':pages,'plannedConditions':['production','ink-input-frame']})
  gold.append({'id':id,'expect':'exact' if variant=='positive' else 'refuse','variant':variant,'schoolYear':2029,'font':font_path.name,'allOtherSlotsBlank':True,'requiredSlotSet':slots,'timetable':{'term':'前期','lessons':lessons}})
manifest={'recipe':'physical-input-framing-fresh-development-v1','researchPracticalCohort':'production-complete-three-class-final-v1',
 'documents':documents,'researchDeadlineSeconds':3300,
 'sourceScope':'Fresh fictional final content for frozen production candidate; baseline and hybrid on identical Windows-rendered lossless PDFs; no school data'}
for key,name in [('detector','det.onnx'),('recognizer','rec.onnx'),('dictionary','alphabet.json')]:
 path=a.output/name;manifest[key]=asset(path,a.output)
write(a.output/'inputs.json',manifest);write(a.output/'oracle.json',gold)
print(json.dumps({'documents':len(documents),'positiveDocuments':2,'slotsPerDocument':120,'bodyFieldsPerDocument':360,'calls':0}))
