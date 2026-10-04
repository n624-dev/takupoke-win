from pathlib import Path
import json,hashlib,datetime,importlib.util,math
ROOT=Path(__file__).resolve().parent
RECIPE=Path('/tmp/takupoke-windows-raster-tiles-20261004/tools/localized-raster-native-probe/recipe.json')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(RECIPE)=='0bdec55548182fb2ed6f12738d0c5e0ea346ec52a21ecbffb4fe1d33d3769fdc'
m=json.loads((ROOT/'manifest.json').read_text());assert len(m['fixtures'])==6
old=json.loads(Path('/workspace/recovery-research/windows-raster-acquisition/manifest.json').read_text());old_pdf={x['pdfSha256'] for x in old['fixtures']}
for f in m['fixtures']:
 folder=ROOT/f['id'];p=folder/'literal-oracle.json';o=json.loads(p.read_text());assert o['pdfSha256'] not in old_pdf
 slots=[{'className':cls,'day':day,'period':period} for cls in o['classes'] for day in o['days'] for period in range(1,o['maxPeriod']+1)]
 assert len(slots)==o['requiredSlots']
 lesson=o['lessons'][0];names={k:lesson[k] for k in ['subject','teacher','room']}
 formal={'schoolYear':o['schoolYear'],'kind':o['kind'],'expectedAdopt':not o['expectedSafeRejection'],'requiredSlotSet':slots,'allOtherSlotsBlank':True,'scope':'Independent literal semantic Analysis oracle, excludes runtime/source-record IDs, timestamps, parser version and provider metadata. No converter or OCR/model output invoked.'}
 if o['kind']=='Timetable':formal['timetable']={'term':o['term'],'lessons':[{'className':lesson['class'],'weekday':int(lesson['day']),'period':1,'names':names}]}
 else:
  date_clocks=o.get('datePeriodClocks') or [{'date':day,**c} for day in o['days'] for c in o['periodClocks']]
  first=next(c for c in date_clocks if c['date']==lesson['day'] and c['period']==1);clock={'start':first['start'],'end':first['end']}
  formal['special']={'coveredClasses':o['classes'],'coveredDates':o['days'],'lessons':[{'className':lesson['class'],'date':lesson['day'],'period':1,'spanStart':1,'spanEnd':1,'names':names,'recordedTime':clock,'displayedTime':clock}], 'datePeriodClocks':date_clocks}
 if o['expectedSafeRejection']:formal.update(rejectionReason=o['negativeReason'],unknownOriginalTeacher=True)
 formal_path=folder/'literal-analysis-oracle.json';
 if o['kind']=='ExamReturn':formal_path.write_text(json.dumps(formal,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 f['formalOracleFile']=f['id']+'/'+formal_path.name;f['formalOracleSha256']=sha(formal_path)
 source=json.loads((folder/'drawing-source.json').read_text());
 for page in o['pages']:
  assert sha(folder/page['imageFile'])==page['imageSha256']
  rw=min(2400,max(640,round(page['width']*72/150*2)));rh=round(page['height']/page['width']*rw)
  if rh>3200:rw=round(rw*3200/rh);rh=3200
  assert rw<=2400 and rh<=3200
 assert (folder/'fictional.pdf').stat().st_size<=2_000_000
 assert len(o['pages'])==5
 if not o['expectedSafeRejection']:
  for value in lesson.values():pass
  for call in source['pages'][0]['textsAndInk']:
   if call.get('text') in [v for v in names.values() if v]:
    x1,y1,x2,y2=call['bbox'];assert 96<x1<=x2<212 and 90<y1<=y2<90+source['configuration']['row']
 f['oracleSha256']=sha(p)
m['freezeScriptSha256']=sha(Path(__file__));m['createdUTC']=datetime.datetime.now(datetime.timezone.utc).isoformat();m['plannedPositives']=5;m['plannedUnreadableNegatives']=1;m['plannedPDFs']=6;m['plannedPages']=30;m['noInferencePerformed']=True;m['noOracleCoordinatesToRuntime']=True
(ROOT/'manifest.json').write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
entries={str(p.relative_to(ROOT)):{'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(ROOT.rglob('*')) if p.is_file() and p.name!='frozen-receipt.json' and '__pycache__' not in p.parts}
receipt={'root':str(ROOT),'recipeSHA256':sha(RECIPE),'manifestSHA256':sha(ROOT/'manifest.json'),'files':entries,'fixtures':6,'positive':5,'negative':1,'source':'Independent literal drawing revision before any OCR, correcting pre-existing Return semantic fixture error; model/threshold unchanged; original archive retained.'}
(ROOT/'frozen-receipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print({'manifestSHA':receipt['manifestSHA256'],'receiptSHA':sha(ROOT/'frozen-receipt.json'),'files':len(entries),'pdfBytes':[ (ROOT/f['id']/'fictional.pdf').stat().st_size for f in m['fixtures']]})
