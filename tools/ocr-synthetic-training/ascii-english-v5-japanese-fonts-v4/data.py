"""Independent v4 synthetic units, exact old/root exclusion, all-side train alias rejection."""
from collections import Counter
from functools import lru_cache
import hashlib,io,json,math,random,re,sys,unicodedata
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from fontTools.ttLib import TTFont
sys.path.insert(0,str(Path(__file__).parents[1]));from image_tensor import tensor
REPO=Path('/workspace/scratch/takupoke-win');PARENT=Path('/workspace/scratch/ascii-english-v5-japanese-fonts-v4');ROOT=PARENT/'adaptation-v4';RESULTS=REPO/'research-results/ocr-synthetic-training-20261013/ascii-english-v5-japanese-fonts-v4'
V1=Path('/workspace/scratch/ascii-english-v5-font-balanced-20261014-v1/assets');V2=Path('/workspace/scratch/ascii-english-v5-confusion-20261016-v2/assets');V3=Path('/workspace/scratch/ascii-english-v5-japanese-fonts-v3/assets')
FONTS={n:V1/(n+'.ttf') for n in ['IBMPlexMono','Inconsolata','CrimsonText','Alegreya','Cousine','PTSerif']};FONTS.update({n:V2/(n+'.ttf') for n in ['RedHatMono','Literata','SplineSansMono','MartianMono','Newsreader','Fraunces']});FONTS.update({n:V3/(n+'.ttf') for n in ['SawarabiGothic','SawarabiMincho','KleeOne','KaiseiTokumin','ZenOldMincho','YuseiMagic','ShipporiAntique','ZenAntiqueSoft']});FONTS.update({n:PARENT/'assets'/(n+'.ttf') for n in ['Murecho','NewTegomin']});FONTS.update({'KosugiMaru':Path('/workspace/scratch/ascii-paddle-medium-height-neck-20261013-v1/assets/KosugiMaru-Regular.ttf'),'KiwiMaru':Path('/workspace/scratch/ascii-paddle-transfer-20261013-v1/assets/KiwiMaru-Regular.ttf'),'HinaMincho':Path('/workspace/scratch/ascii-paddle-transfer-20261013-v1/assets/HinaMincho-Regular.ttf'),'ShipporiMincho':Path('/workspace/scratch/physical-frame-fresh-fonts/shipporimincho-ShipporiMincho-Regular.ttf'),'NotoSansJP400':Path('/workspace/scratch/ocr-comparison-20261013/sans-static.ttf'),'NotoSerifJP400':Path('/workspace/scratch/ocr-comparison-20261013/serif-static.ttf')})
LETTERS='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz';ALNUM=LETTERS+'0123456789';PRINTABLE=''.join(chr(c) for c in range(32,127));SHAPING=['-liga','-clig']
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def digest(x):return hashlib.sha256(x.encode('utf-8')).hexdigest()
def save(p,v):Path(p).write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
@lru_cache(maxsize=40)
def cmap(name):
 with TTFont(FONTS[name]) as f:return set(f.getBestCmap())
@lru_cache(maxsize=320)
def font(name,size):return ImageFont.truetype(str(FONTS[name]),size)
def code(rng):
 s=''.join(rng.choice(ALNUM) for _ in range(rng.randint(2,10)))
 if len(s)>2 and rng.random()<.4:
  k=rng.randrange(1,len(s));s=s[:k]+rng.choice('_- ')+s[k:]
 return s
def phrase(rng):return ''.join(rng.sample(['青空','木風','雨波','星花','白月','朝森','雲葉','雪山','春海','夜光','夏虹','霜野'],2))+rng.choice(['の','や','と'])+rng.choice(['小川','丘','道','林'])
TRAIN_EN=['amber','violet','maple','river','garden','orchard','hazel','daisy','prairie','harbor','autumn','sparrow','pebble','valley','sunlight','oak','elm','robin','poppy','clouds','bells','flowers','birds','waves']
DEV_EN=['silver','golden','sequoia','beach','marsh','forest','canyon','nightfall','lilac','spring','summit','cricket','coral','island','moonlight','ash','fir','wren','iris','streams','chimes','blossoms','insects','leaves']
def natural(rng,split):
 a,b=rng.sample(TRAIN_EN if split=='train' else DEV_EN,2);s=rng.choice([a+' and '+b,'The '+a+' is quiet.','We see '+a+' today.',a+' grows near '+b,'Fresh '+a+' at dawn.','Light over the '+a,'A calm '+a+' morning.']);return rng.choice([str.lower,str.upper,lambda x:x])(s)
def render(value,name,seed,spec):
 rng=random.Random(seed);density=rng.choice(spec['densities']);size=rng.choice(spec['sizesPx'])*density;face=font(name,size);tracking=rng.choice(spec['trackingEm'])*size;stroke=rng.choice([0,1]) if density>=2 else 0;margin=rng.choice([3,5,8])*density;ascent,descent=face.getmetrics();advance=float(face.getlength(value,features=SHAPING));width=max(12,math.ceil(advance+tracking*max(0,len(value)-1))+2*margin+2*stroke);image=Image.new('RGB',(width,ascent+descent+2*margin+2*stroke),'white');baseline=margin+stroke+ascent;color=rng.choice([(0,0,0),(10,10,10),(0,0,90),(65,65,65)])
 draw=ImageDraw.Draw(image)
 for i,ch in enumerate(value):
  x=margin+stroke+float(face.getlength(value[:i+1],features=SHAPING))-float(face.getlength(ch,features=SHAPING))+tracking*i;draw.text((x,baseline),ch,font=face,anchor='ls',fill=color,stroke_width=stroke,stroke_fill=color,features=SHAPING)
 rgb=np.asarray(image);edge=np.any(rgb!=255,axis=2)
 if edge[0].any() or edge[-1].any() or edge[:,0].any() or edge[:,-1].any():raise ValueError('Rendered font touches source canvas border')
 return image,{'fontSize':size,'density':density,'trackingPixels':tracking,'strokePixels':stroke,'margin':margin,'baseline':baseline,'advance':advance,'ligaturesDisabled':True}
def frame(image):
 rgb=np.asarray(image);ink=np.any(rgb!=255,axis=2);ys,xs=np.where(ink);box=(max(0,int(xs.min())-2),max(0,int(ys.min())-2),min(image.width,int(xs.max())+3),min(image.height,int(ys.max())+3)) if len(xs) else (0,0,image.width,image.height);crop=image.crop(box);assert int(ink.sum())==int(np.any(np.asarray(crop)!=255,axis=2).sum());return crop,{'crop':list(box),'originalWidth':image.width,'originalHeight':image.height,'allNonwhiteRetained':True,'providedNonwhiteSamples':int(ink.sum()),'originalRgbSha256':hashlib.sha256(rgb.tobytes()).hexdigest()}
def prepared(value,name,seed,spec):
 image,geometry=render(value,name,seed,spec);image,source=frame(image);source['framedBaseline']=geometry['baseline']-source['crop'][1];x,valid=tensor(image);buf=io.BytesIO();image.save(buf,format='PNG');return image,geometry,source,x,valid,buf.getvalue()
def alternatives(value):
 result=set()
 for i,ch in enumerate(value):
  group='Il1' if ch in 'Il1' else 'O0' if ch in 'O0' else ''
  for replacement in group:
   if replacement!=ch:result.add(value[:i]+replacement+value[i+1:])
 return sorted(result)
def train_ambiguities(values,name,seed,spec):
 conflicts=[]
 for value in values:
  primary=prepared(value,name,seed,spec);h=hashlib.sha256(primary[3].tobytes()).hexdigest()
  for alternate in alternatives(value):
   try:other=prepared(alternate,name,seed,spec)
   except ValueError:continue
   if h==hashlib.sha256(other[3].tobytes()).hexdigest():conflicts.append({'target':value,'alternative':alternate,'inputSha256':h})
 return conflicts

def exclusion():
 paths=list(Path('/workspace/scratch').rglob('manifest.json'))+list(Path('/workspace/scratch').rglob('cache.json'))+list(Path('/workspace/scratch').rglob('*oracle*.json'))+list(Path('/workspace/scratch').rglob('inputs.json'))+list((REPO/'research-results').rglob('*.json'))+list((REPO/'research-results').rglob('*.jsonl'));paths += [Path('/workspace/scratch/ascii-english-v5-japanese-fonts-v3/adaptation-v3/excluded-values.json'),REPO/'tools/ocr-whole-model-comparison/exclusions.json'];paths=sorted({p for p in paths if not p.is_relative_to(PARENT) and not p.is_relative_to(RESULTS)});used=set();files=[]
 def walk(v):
  if isinstance(v,str) and len(v)<=256:used.add(v);used.add(unicodedata.normalize('NFKC',v).strip())
  elif isinstance(v,(int,float)) and not isinstance(v,bool):used.add(str(v))
  elif isinstance(v,list):
   for a in v:walk(a)
  elif isinstance(v,dict):
   for a in v.values():walk(a)
 for p in paths:
  try:payload=p.read_bytes();s=payload.decode('utf-8')
  except (OSError,UnicodeError):continue
  files.append({'path':str(p),'sha256':hashlib.sha256(payload).hexdigest()})
  if p.suffix=='.jsonl':
   for line in s.splitlines():
    if line.strip():walk(json.loads(line))
  else:walk(json.loads(s))
 oracle=Path('/workspace/scratch/ocr-whole-v3-generation-precheck-20261016-v2/oracle.json');assert oracle.exists();rows=json.loads(oracle.read_text(encoding='utf-8'));fields=set();components=set();fresh_fields=set()
 for document in rows:
  for lesson in document.get('timetable',{}).get('lessons',[]):
   for field in lesson.get('names',{}).values():
    fields.add(field)
    if document.get('id','').startswith('fresh'):fresh_fields.add(field)
    normalized=unicodedata.normalize('NFKC',field).strip();components.add(normalized);components.update(re.findall(r'[A-Za-z0-9_\-]+',normalized));components.update(re.findall(r'\d+',normalized));components.update(re.findall(r'[^A-Za-z0-9_\-\s]+',normalized))
 used.update(fields|components);legacy=json.loads((REPO/'tools/ocr-whole-model-comparison/exclusions.json').read_text(encoding='utf-8'))['literalSha256'];assert len(legacy)==21201;used.update(legacy)
 # Old cache receipts supply normalized hashes; root BGRA crops are normalized for exclusion only.
 normalized=0
 for p in paths:
  if p.name!='inputs.json':continue
  doc=json.loads(p.read_text(encoding='utf-8'))
  if not isinstance(doc,dict):continue
  for row in doc.get('rows',[]):
   if not isinstance(row,dict) or not all(k in row for k in ['pixels','width','height']):continue
   pix=row['pixels']
   if not isinstance(pix,dict) or not isinstance(pix.get('file'),str):continue
   q=p.parent/pix['file']
   if q.suffix!='.bgra' or not q.exists():continue
   payload=q.read_bytes()
   if len(payload)!=row['width']*row['height']*4:continue
   if 'sha256' in pix:assert hashlib.sha256(payload).hexdigest()==pix['sha256']
   image=Image.fromarray(np.frombuffer(payload,dtype=np.uint8).reshape(row['height'],row['width'],4)[:,:,[2,1,0]])
   for im in [image,frame(image)[0]]:
    try:a,_=tensor(im)
    except ValueError:continue
    used.add(hashlib.sha256(a.tobytes()).hexdigest());normalized+=1
 save(ROOT/'excluded-values.json',sorted(used));save(ROOT/'exclusion-receipt.json',{'files':files,'excludedCount':len(used),'setSha256':digest(json.dumps(sorted(used),ensure_ascii=False)),'rootOracle':{'path':str(oracle),'sha256':sha(oracle),'fieldLiterals':len(fields),'freshFieldLiterals':len(fresh_fields),'freshFieldsSha256':digest(json.dumps(sorted(fresh_fields),ensure_ascii=False)),'components':len(components),'valuesSha256':digest(json.dumps(sorted(fields|components),ensure_ascii=False))},'legacyHashCount':len(legacy),'rootBgraConditions':normalized,'granularity':'Exact full target equality, exact UTF8SHA and NFKC+strip variants. Root full field/ASCII suffix/maximal numeric/CJK component strings included. No casefold/substring/character alphabet prohibition. Structural vocabulary is shared control, not new evidence.'});return used

def generate(protocol,split,forbidden):
 directory=ROOT/split
 if directory.exists():raise RuntimeError('Frozen corpus cannot repeat')
 directory.mkdir();spec=protocol[split];rng=random.Random(spec['seed']);used=set(forbidden);rows=[];rejected=[];attempts=Counter();coverage=list(PRINTABLE);rng.shuffle(coverage);render_spec=protocol['sourceRendering'];units=[]
 for name in spec['jpFonts']:
  ordinary=spec['positiveCounts']['code']//len(spec['jpFonts']);hard=spec['positiveCounts']['lookalike']//len(spec['jpFonts'])
  units += [(name,'code','single')]*(ordinary//2)+[(name,'code','case')]*(ordinary//4)
  triples,pairs=(8,12) if hard==48 else (10,17)
  assert triples*3+pairs*2==hard;units += [(name,'lookalike','Il1')]*triples+[(name,'lookalike','O0')]*pairs
 for kind in ['natural','printable']:
  for i in range(spec['positiveCounts'][kind]):units.append((spec['latinFonts'][i%len(spec['latinFonts'])],kind,'single'))
 for unit_index,(name,kind,mode) in enumerate(units):
  while True:
   attempts[kind]+=1;seed=rng.randrange(2**31)
   if kind=='natural':value=natural(rng,split)
   elif kind=='printable':value=rng.choice(LETTERS)+''.join(coverage[(unit_index*8+i)%95] for i in range(8))+''.join(rng.choice(ALNUM) for _ in range(3))
   else:value=code(rng)
   if mode in ['Il1','O0']:
    pos=rng.randrange(len(value));values=[value[:pos]+c+value[pos+1:] for c in mode]
   elif mode=='case':
    positions=[i for i,c in enumerate(value) if c in LETTERS]
    if not positions:continue
    pos=rng.choice(positions);values=[value,value[:pos]+value[pos].swapcase()+value[pos+1:]]
   else:values=[value]
   if any(v in used or digest(v) in forbidden or unicodedata.normalize('NFKC',v).strip() in used or any(ord(c) not in cmap(name) for c in v) for v in values):continue
   try:items=[prepared(v,name,seed,render_spec) for v in values]
   except ValueError:continue
   hashes=[hashlib.sha256(item[3].tobytes()).hexdigest() for item in items];pngs=[hashlib.sha256(item[5]).hexdigest() for item in items]
   if any(h in used for h in hashes+pngs):continue
   if split=='train':
    conflicts=train_ambiguities(values,name,seed,render_spec)
    if conflicts:rejected.append({'unit':unit_index,'font':name,'kind':kind,'values':values,'reason':'Physical normalizedinput aliases; entire proposed unit rejected before admission','conflicts':conflicts});continue
   break
  for value,item,h,ph in zip(values,items,hashes,pngs):
   image,geometry,source,x,valid,payload=item;path=directory/f'{len(rows):05d}.png';path.write_bytes(payload);rows.append({'id':f'{split}-{len(rows):05d}','unitId':f'{split}-unit-{unit_index:05d}','renderSeed':seed,'kind':kind,'font':name,'text':value,'renderedLiteral':value,'reject':False,'ctcEligible':True,'selectionPositive':True,'file':path.name,'pngSha256':ph,'inputSha256':h,'inputWidth':x.shape[-1],'validWidth':valid,'source':source,'geometry':geometry,'width':image.width,'height':image.height})
  used.update(values+hashes+pngs)
 for kind,count in spec['negativeCounts'].items():
  for i in range(count):
   name=spec['fonts'][i%len(spec['fonts'])]
   if kind in ['unknown_cjk','unknown_mixed']:name='NotoSansJP400' if split=='train' else 'NotoSerifJP400'
   while True:
    seed=rng.randrange(2**31);value=phrase(rng) if kind=='unknown_cjk' else phrase(rng)+' '+code(rng) if kind=='unknown_mixed' else code(rng)
    if value in used or digest(value) in forbidden or any(ord(c) not in cmap(name) for c in value):continue
    try:image,geometry=render(value,name,seed,render_spec)
    except ValueError:continue
    before=int(np.any(np.asarray(image)!=255,axis=2).sum());clip_top=0
    if kind=='clipped':
     ys,xs=np.where(np.any(np.asarray(image)!=255,axis=2));top,bottom=int(ys.min()),int(ys.max())+1
     if rng.random()<.5:cut=top+max(1,int((bottom-top)*rng.uniform(.2,.4)));clip_top=cut;image=image.crop((0,cut,image.width,image.height))
     else:cut=bottom-max(1,int((bottom-top)*rng.uniform(.2,.4)));image=image.crop((0,0,image.width,cut))
    elif kind=='interior_damage':
     rgb=np.asarray(image).copy();ys,xs=np.where(np.any(rgb!=255,axis=2));k=rng.randrange(len(xs));x,y=int(xs[k]),int(ys[k]);radius=max(1,int(geometry['fontSize']*.08));rgb[max(1,y-radius):min(image.height-1,y+radius+1),max(1,x-radius):min(image.width-1,x+radius+1)]=255;image=Image.fromarray(rgb)
     if int(np.any(rgb!=255,axis=2).sum())>=before:continue
    elif kind=='multiline':
     both=Image.new('RGB',(image.width,image.height*2+8),'white');both.paste(image,(0,0));both.paste(image,(0,image.height+8));image=both
    elif kind=='noise':
     image.paste('white',(0,0,image.width,image.height));draw=ImageDraw.Draw(image)
     for _ in range(rng.randint(4,10)):draw.line([rng.randrange(image.width),rng.randrange(image.height),rng.randrange(image.width),rng.randrange(image.height)],fill='black',width=rng.randint(1,4))
    elif kind=='blank':image.paste('white',(0,0,image.width,image.height))
    image,source=frame(image);source.update({'framedBaseline':geometry['baseline']-clip_top-source['crop'][1],'preDamageNonwhiteSamples':before,'removedNonwhiteSamples':before-source['providedNonwhiteSamples'] if kind in ['clipped','interior_damage'] else 0})
    try:x,valid=tensor(image)
    except ValueError:continue
    buf=io.BytesIO();image.save(buf,format='PNG');payload=buf.getvalue();h=hashlib.sha256(x.tobytes()).hexdigest();ph=hashlib.sha256(payload).hexdigest()
    if kind!='blank' and (h in used or ph in used):continue
    break
   path=directory/f'{len(rows):05d}.png';path.write_bytes(payload);rows.append({'id':f'{split}-{len(rows):05d}','unitId':f'{split}-negative-{kind}-{i}','renderSeed':seed,'kind':kind,'font':name,'text':'' if kind=='blank' else None,'renderedLiteral':value,'reject':True,'ctcEligible':kind=='blank','selectionPositive':False,'file':path.name,'pngSha256':ph,'inputSha256':h,'inputWidth':x.shape[-1],'validWidth':valid,'source':source,'geometry':geometry,'width':image.width,'height':image.height});used.update([value,h,ph])
 manifest={'split':split,'protocolSha256':digest(json.dumps(protocol,sort_keys=True)),'fonts':{n:sha(FONTS[n]) for n in spec['fonts']},'rows':rows};save(directory/'manifest.json',manifest);save(directory/'generation-audit.json',{'attempts':dict(attempts),'rejectedWholeAliasUnits':len(rejected),'rejectedUnits':rejected,'trainingOnlyAliasExclusion':True,'devFreshAdmittedAtomicAllMembersRetained':split!='train','testAliasDiagnosticsAfterModelAssessmentOnly':True});font.cache_clear();return manifest
