"""Independent six-fixture literal canvas, frozen after tile recipe. Never reads OCR/model output."""
from pathlib import Path
import hashlib,json,importlib.util
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parent
FONT=Path('/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc')
ASSEMBLER=Path('/workspace/takupoke-win/tools/raster-acquisition-native-probe/fixtures/assemble.py')
spec=importlib.util.spec_from_file_location('literal_png_pdf',ASSEMBLER);assembler=importlib.util.module_from_spec(spec);spec.loader.exec_module(assembler)
CLASSES=['1_1','1_2','1_3']+[f'{year}_{course}' for year in range(2,6) for course in ['CN','ES','IT']]+['AI_1','AI_2']
CLOCKS=[('09:10','09:50'),('10:00','10:40'),('10:50','11:30'),('11:40','12:20'),('13:10','13:50'),('14:00','14:40'),('14:50','15:30'),('15:40','16:20')]
NORMAL_CLOCKS=[('08:50','09:35'),('09:35','10:20'),('10:30','11:15'),('11:15','12:00'),('12:50','13:35'),('13:35','14:20'),('14:30','15:15'),('15:15','16:00')]
CONFIGS=[
 dict(id='independent-Timetable-literal-乙',kind='Timetable',year=2027,term='前期',classes=['5_ES'],month=10,startDay=11,values=['架空化学乙','架空担当乙','架空室乙'],row=76,size=13),
 dict(id='independent-Timetable-verifiedblank-丙',kind='Timetable',year=2028,term='後期',classes=['2_IT'],month=11,startDay=6,values=['架空物理丙','','架空室丙'],row=80,size=12),
 dict(id='independent-Exam-reverseclasses-丁',kind='Exam',year=2027,term=None,classes=list(reversed(CLASSES)),month=10,startDay=11,values=['架空数学丁','架空担当丁','架空室丁'],row=76,size=12),
 dict(id='independent-Exam-shiftclasses-戊',kind='Exam',year=2028,term=None,classes=CLASSES[8:]+CLASSES[:8],month=11,startDay=6,values=['架空化学戊','架空担当戊','架空室戊'],row=80,size=13),
 dict(id='independent-ExamReturn-verifiedblank-己',kind='ExamReturn',year=2027,term=None,classes=CLASSES[5:]+CLASSES[:5],month=10,startDay=21,values=['架空情報己','架空担当己',''],row=76,size=12),
 dict(id='independent-ExamReturn-unreadableink-庚',kind='ExamReturn',year=2028,term=None,classes=CLASSES[2:]+CLASSES[:2],month=11,startDay=16,values=['架空数学庚',None,'架空室庚'],row=80,size=13,negative=True)]
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def generate(c):
 special=c['kind']!='Timetable';periods=6 if c['kind']=='Exam' else 8;left=96;cell=116;row=c['row'];width=left+periods*cell;bottom=90+len(c['classes'])*row;height=bottom+(220 if special else 40)
 folder=ROOT/c['id'];folder.mkdir(exist_ok=True);pages=[];drawing=[]
 for index in range(5):
  image=Image.new('RGB',(width+1,height),'white');d=ImageDraw.Draw(image);calls=[]
  def text(value,x,y,size=14):
   assert isinstance(value,str)
   d.text((x,y),value,font=ImageFont.truetype(str(FONT),size),fill='black')
   calls.append({'text':value,'x':x,'y':y,'fontPixels':size,'bbox':list(d.textbbox((x,y),value,font=ImageFont.truetype(str(FONT),size)))})
  text(f"{c['year']}年度"+(' '+c['term'] if not special else ''),5,5,18)
  text({'Timetable':'時間割','Exam':'試験時間割','ExamReturn':'試験返却時間割'}[c['kind']],220,5,18)
  text(f"{c['month']}月{c['startDay']+index}日" if special else ['月','火','水','木','金'][index],left+10,36,17)
  for y in [32,60,90]+[90+(i+1)*row for i in range(len(c['classes']))]:d.line((0,y,width,y),fill='black',width=1)
  for x in [0,left]+[left+(i+1)*cell for i in range(periods)]:d.line((x,32 if x in [0,left,width] else 60,x,bottom),fill='black',width=1)
  for period in range(periods):text(str(period+1),left+period*cell+8,66,15)
  for i,cls in enumerate(c['classes']):text(cls,5,90+i*row+24,15)
  if index==0:
   for role,(label,value) in enumerate(zip(['科目:','担当:','教室:'],c['values'])):
    y=96+role*21;text(label,left+3,y,c['size'])
    if value is not None:text(value,left+39,y,c['size'])
    else:
     # Opaque ink with no recoverable letter: not an absent or blank teacher.
     box=(left+39,y+4,left+100,y+16);d.rectangle(box,fill='black');calls.append({'unreadableInkRectangle':list(box),'role':'teacher','sourceTextExists':False})
  if special:
   if c['kind']=='ExamReturn':
    text(f"{c['month']}月{c['startDay']}日の時間割は以下のとおり",5,bottom+10,14)
    text(f"{c['month']}月{c['startDay']+1}日〜{c['startDay']+4}日は通常の授業日どおりの授業時間",5,bottom+36,14)
   else:text('試験時間割',5,bottom+10,14)
   for period in range(periods):
    if c['kind']=='ExamReturn':text(f"{c['month']}月{c['startDay']+index}日",left+period*cell+3,bottom+56,12)
    text(str(period+1),left+period*cell+5,bottom+80,14)
    clocks=NORMAL_CLOCKS if c['kind']=='ExamReturn' and index>0 else CLOCKS
    text('〜'.join(clocks[period]),left+period*cell+3,bottom+110,12)
  png=folder/f'page-{index+1}.png';image.save(png)
  pages.append({'page':index+1,'imageFile':png.name,'imageSha256':sha(png),'width':width+1,'height':height})
  drawing.append({'page':index+1,'textsAndInk':calls})
 pdf=folder/'fictional.pdf';assembler.assemble(pdf,[folder/p['imageFile'] for p in pages]);assert pdf.stat().st_size<2_000_000
 values=c['values'];oracle={'kind':c['kind'],'profile':c['id'],'schoolYear':c['year'],'term':c['term'],'classes':c['classes'],'days':[f"{c['year']}-{c['month']:02d}-{c['startDay']+i:02d}" if special else str(i+1) for i in range(5)],'maxPeriod':periods,'requiredSlots':len(c['classes'])*5*periods,'lessons':[{'class':c['classes'][0],'day':f"{c['year']}-{c['month']:02d}-{c['startDay']:02d}" if special else '1','periods':[1],'subject':values[0],'teacher':values[1],'room':values[2]}],'allOtherSlotsBlank':True,'periodClocks':[{'period':i+1,'start':a,'end':b} for i,(a,b) in enumerate(CLOCKS[:periods])] if special else [],'expectedSafeRejection':c.get('negative',False),'pdfFile':'fictional.pdf','pdfSha256':sha(pdf),'pages':pages}
 if c['kind']=='ExamReturn':
  oracle.pop('periodClocks')
  oracle['datePeriodClocks']=[{'date':day,'period':period+1,'start':a,'end':b} for index,day in enumerate(oracle['days']) for period,(a,b) in enumerate(CLOCKS if index==0 else NORMAL_CLOCKS)]
  oracle['normalTimeNote']=f"{c['month']}月{c['startDay']}日の時間割は以下のとおり{c['month']}月{c['startDay']+1}日〜{c['startDay']+4}日は通常の授業日どおりの授業時間"
 if c.get('negative'):oracle.update(unreadableRole='teacher',negativeReason='Opaque original black ink with no readable teacher glyphs; never treat it as verified blank or invent a value.')
 (folder/'literal-oracle.json').write_text(json.dumps(oracle,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 (folder/'drawing-source.json').write_text(json.dumps({'configuration':c,'pages':drawing,'scope':'Independent exact drawing instructions; not runtime region coordinates or OCR-derived text.'},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 return {'id':c['id'],'oracleFile':f"{c['id']}/literal-oracle.json",'oracleSha256':sha(folder/'literal-oracle.json'),'pdfSha256':sha(pdf),'drawingSourceSha256':sha(folder/'drawing-source.json'),'positive':not c.get('negative',False)}
if __name__=='__main__':
 old_root=ROOT.with_name('windows-raster-independent-heldout-v2-20261004')
 previous=json.loads((old_root/'manifest.json').read_text(encoding='utf-8'))
 fixtures=[]
 for c,old in zip(CONFIGS,previous['fixtures']):
  fixtures.append(generate(c))
 manifest={**previous,'fixtures':fixtures,'recipeSHA256':'0bdec55548182fb2ed6f12738d0c5e0ea346ec52a21ecbffb4fe1d33d3769fdc','generatorSha256':sha(Path(__file__)),'previousManifestSHA256':sha(old_root/'manifest.json'),'revisionReason':'Before any OCR: correct missing right ruled border on all6 input canvases. Width increases1px to contain original right grid coordinate; horizontal lines connect to it. Text/fonts/positions, kind/classes/dates/values and v2 clock semantics unchanged. v1/v2 archives retained. Actual pixel RecoveryRaster.Rules must pass semantic preflight before native.'}
 manifest.pop('createdUTC',None)
 manifest.pop('freezeScriptSha256',None)
 (ROOT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 print({'manifestBeforeFreezeSHA':sha(ROOT/'manifest.json')})
