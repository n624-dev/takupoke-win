#!/usr/bin/env python3
"""Prospective fictional paired whole documents; no inference or private material."""
import argparse
import hashlib
import json
import random
import shutil
import string
import subprocess
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[2]
FRESH_FONTS=[('ZenKurenaido-Regular.ttf', 4303112, '58b8d930d9fc10c8a5810c085bae378dacb98d0779073ee6d53d919f19ee6a4f'), ('KaiseiDecol-Regular.ttf', 4503152, 'fb9202b97944fa00b5f145ad4701def5c306bcd0d26407917baef9e01b042056')]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,x):p.write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def asset(p):return dict(file=p.name,bytes=p.stat().st_size,sha256=sha(p))
def literal_sha(s):return hashlib.sha256(s.encode()).hexdigest()

def main():
 p=argparse.ArgumentParser();p.add_argument('--fonts',type=Path,required=True);p.add_argument('--models',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--exclusions',type=Path,required=True);a=p.parse_args();out=a.output.resolve()
 exclusions=json.loads(a.exclusions.read_text());base=exclusions['base'];base_path=ROOT/base['file']
 if base_path.stat().st_size!=base['bytes'] or sha(base_path)!=base['sha256']:raise ValueError('Base exclusion ledger changed')
 excluded=set(json.loads(base_path.read_text())['literalSha256'])
 for part in exclusions['parts']:
  part_path=ROOT/part['file']
  if part_path.stat().st_size!=part['bytes'] or sha(part_path)!=part['sha256']:raise ValueError('Additional exclusion ledger changed')
  excluded.update(json.loads(part_path.read_text()))
 if len(excluded)!=exclusions['totalExcludedLiterals']:raise ValueError('Incomplete exclusion ledger')
 seen=set()
 out.mkdir(parents=True,exist_ok=False)
 m=dict(documents=[],recipe='source-order-native-fresh-v1',researchDeadlineSeconds=1250);gold=[]
 for file,size,digest in FRESH_FONTS:
  path=a.fonts/file
  if path.stat().st_size!=size or sha(path)!=digest:raise ValueError('Fresh font changed')
 write(out/'generation-freeze.json',dict(recipe='source-order-native-fresh-v1',sourceSHA256=sha(Path(__file__)),fonts=FRESH_FONTS,seedBase=20261016113,exclusionsSHA256=sha(a.exclusions),exclusionProvenance=exclusions['sources'],classes=['1_1','2_IT','AI_1'],days=5,periods=8,positiveDocuments=2,negativeDocuments=4,bodyFieldsPerPositive=360,variants=['positive','missing-day','missing-class'],roomDistribution=['60% Japanese prefix plus arbitrary 2..8 ASCII code containing digit','20% code alone','20% Japanese descriptive room plus four digits'],priorPatternNotAssumed=True,plannedArms=['historical-4ff-v1','source-repairs-role-label-retry-mixed-code-v3expert-fallback-v1'],prospectiveCriteria=dict(maximumCERPerFont=.03,minimumExactFieldRate=.90,maximumRSSBytes=6*1024**3,maximumReviewUnion=128,completeFieldsAndSlots=True,negativeDocumentsRefused=True,pairedLiteralImprovement='Every positive font nondecreasing exact fields and nonincreasing edits; fresh nonregression and quality, all-positive total exact strictly improves'),zeroErrorsRequired=False,scope='Two locallyunusedfontfiles/newliteralsets; sixearlierfontcorpora are separate consumed source-only regression. KaiseiDecol is related to previously trained KaiseiTokumin; no family/pretraining-unseen claim. Fixed model/route before fresh, no fresh selection.'))
 for ordinal,(file,_,_) in enumerate(FRESH_FONTS):
  generated=[];rng=random.Random(20261016113+ordinal)
  face=ImageFont.truetype(str(a.fonts/file),20);header=ImageFont.truetype(str(a.fonts/file),24)
  if file=='MPLUS2-Variable.ttf':
   face.set_variation_by_axes([400]);header.set_variation_by_axes([400])
  def unique(factory):
   for attempt in range(10000):
    value=factory();digest=literal_sha(value)
    if digest not in excluded and digest not in seen:
     seen.add(digest);return value
   raise ValueError('Could not draw independent literal')
  for day in range(1,6):
   for cls in ['1_1','2_IT','AI_1']:
    for period in range(1,9):
     def number():return unique(lambda:str(rng.randrange(1000,10000)))
     subject=unique(lambda:'架空'+rng.choice(['情報','音楽','美術','地理','家庭','体育'])+number())
     teacher=unique(lambda:'仮想'+rng.choice(['伊藤','渡辺','山本','中村','小林','加藤'])+number())
     def code():
      while True:
       value=''.join(rng.choice(string.ascii_letters+string.digits+'_-') for _ in range(rng.randrange(2,9)))
       if any(c.isdigit() for c in value):return value
     pick=rng.random()
     if pick<.8:
      suffix=unique(code);room=('架空室' if pick<.6 else '')+suffix
      if literal_sha(room) in excluded:raise ValueError('Unexpected full room collision; retain failed generation')
     else:room=unique(lambda:'仮設'+rng.choice(['実習室','講義室','研究室'])+number())
     generated.append(dict(className=cls,weekday=day,period=period,names=dict(subject=subject,teacher=teacher,room=room)))
  for variant in ['positive','missing-day','missing-class']:
   id=f'sourcefresh-{ordinal}-{variant}';pages=[]
   for day in range(1,6):
    width,height=2237,415;label,col,top,row=100,267,108,102
    image=Image.new('RGB',(width,height),'white');pen=ImageDraw.Draw(image)
    def text(value,x,y,font=face):pen.text((x,y),value,font=font,fill='black')
    text('2033年度 後期',8,0,header);pen.rectangle((label,36,width-1,70),outline='black',width=1)
    if not(variant=='missing-day' and day==1):text(['月','火','水','木','金'][day-1],label+4*col,36,header)
    for y in [70,top]+[top+n*row for n in range(1,4)]:pen.line((0,y,width-1,y),fill='black',width=1)
    pen.line((0,70,0,height-1),fill='black',width=1)
    for n in range(9):pen.line((label+n*col,70,label+n*col,height-1),fill='black',width=1)
    for period in range(1,9):text(str(period),label+(period-1)*col+col//2,73,header)
    for idx,cls in enumerate(['1_1','2_IT','AI_1']):
     if not(variant=='missing-class' and idx==2 and day==1):text(cls,5,top+idx*row+33,header)
     for period in range(1,9):
      lesson=next(v for v in generated if v['className']==cls and v['weekday']==day and v['period']==period)
      for role,(prefix,value) in enumerate(zip(['科目：','担当：','教室：'],lesson['names'].values())):
       if pen.textlength(prefix+value,font=face)>col-20:raise ValueError('Printed line overflows cell; retain failure')
       text(prefix+value,label+(period-1)*col+8,top+idx*row+4+role*31)
    path=out/f'{id}-p{day}.bgra';path.write_bytes(image.convert('RGBA').tobytes('raw','BGRA'));pages.append(dict(page=day,width=width,height=height,pixels=asset(path)))
   identity=hashlib.sha256(json.dumps(pages,sort_keys=True).encode()).hexdigest()
   m['documents'].append(dict(id=id,originalPDFSHA256=identity,pages=pages))
   gold.append(dict(id=id,expect='exact' if variant=='positive' else 'refuse',variant=variant,schoolYear=2033,font=file,cohort='fresh',allOtherSlotsBlank=True,requiredSlotSet=[dict(className=v['className'],day=str(v['weekday']),period=v['period']) for v in generated],timetable=dict(term='後期',lessons=generated)))
 m.update(recipe='source-order-native-fresh-v1',sourceScope='Two locally unusedfontfiles/newliterals on identical original native Windows PDF pixels. Historical4ff baseline versus fixed model/route with independently proved original native-row ordering.',researchDeadlineSeconds=1250)
 shutil.copyfile(a.exclusions,out/'exclusions.json');write(out/'inputs.json',m);write(out/'oracle.json',gold)
 write(out/'fresh-literal-receipt.json',dict(uniqueSampledLiterals=len(seen),allExcludedLiteralHashesAvoided=not bool(seen&excluded),freshFontCount=2,inferenceCalls=0))
 print(json.dumps(dict(documents=len(gold),positiveDocuments=2,bodyFieldsPerPositive=360,inferenceCalls=0)))

if __name__=='__main__':main()
