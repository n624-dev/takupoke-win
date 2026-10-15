"""Independent natural mixed lines. No old labels or school originals as examples."""
from functools import lru_cache
import hashlib
import json
import math
from pathlib import Path
import random
import sys
import io

import numpy as np
from PIL import Image,ImageDraw,ImageFilter,ImageFont
from fontTools.ttLib import TTFont

sys.path.insert(0,str(Path(__file__).parents[1]))
from image_tensor import tensor

PARENT=Path('/workspace/scratch/ascii-english-v5-confusion-20261016-v2')
ROOT=PARENT/'adaptation-v2'
REPO=Path('/workspace/scratch/takupoke-win')
RESULTS=REPO/'research-results/ocr-synthetic-training-20261013/ascii-english-v5-confusion-v2'
OLD_ASSETS=Path('/workspace/scratch/ascii-english-v5-font-balanced-20261014-v1/assets')
FONTS={name:OLD_ASSETS/(name+'.ttf') for name in ['IBMPlexMono','Inconsolata','FiraMono','SpaceMono','CrimsonText','LibreBaskerville','Lora','Alegreya']}
FONTS.update({name:PARENT/'assets'/(name+'.ttf') for name in ['RedHatMono','OverpassMono','SourceSerif4','Literata','SplineSansMono','MartianMono','Newsreader','Fraunces']})
FONTS.update({'NotoSansJP400':Path('/workspace/scratch/ocr-comparison-20261013/sans-static.ttf'),'NotoSerifJP400':Path('/workspace/scratch/ocr-comparison-20261013/serif-static.ttf')})
TRAIN_NOUNS=['夜風','雨森','朝星','霜空','白花','緑波','山川','海月','月夜','星月','小森','雪森','風波','露花','潮雲','晴空','陽炎','風花','雨景','雲影','春芽','波音','淡雪','夕森','月雲','朝霞','野風','山雲','空色','水光','若草','冬星']
DEV_NOUNS=['青波','花道','雪丘','雲間','松林','冬木','朝雲','夏月','月虹','冬霧','春野','山露','雨夜','虹空','海色','冬花','木霧','葉雲','朝風','星花','波影','森月','冬川','雪月']
LETTERS='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'
ALNUM=LETTERS+'0123456789'
PRINTABLE=''.join(chr(c) for c in range(32,127))


def save(path,value):Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


@lru_cache(maxsize=32)
def cmap(name):return set(TTFont(FONTS[name]).getBestCmap())


@lru_cache(maxsize=512)
def font(name,size):return ImageFont.truetype(str(FONTS[name]),size)


def excluded():
 paths=[REPO/'tools/vision-ocr-comparison/oracle.json',Path('/workspace/scratch/physical-frame-fresh-development-20261013/oracle.json')]
 for directory in ['/workspace/scratch/ocr-finetune-20261013','/workspace/scratch/ascii-line-ctc-20261013-v1','/workspace/scratch/ascii-paddle-transfer-20261013-v1','/workspace/scratch/ascii-paddle-medium-features-20261013-v1/mixed-head-v1','/workspace/scratch/ascii-paddle-medium-svtr-20261013-v1/adaptation-v1','/workspace/scratch/ascii-paddle-medium-height-neck-20261013-v1/adaptation-v1','/workspace/scratch/ascii-paddle-medium-cnn-tail-20261014-v1/adaptation-v1','/workspace/scratch/ascii-english-v5-official-transfer-20261014-v1/adaptation-v1']:
  paths.extend(Path(directory).glob('*/manifest.json'))
 paths.extend(Path('/workspace/scratch').rglob('manifest.json'));paths.extend(Path('/workspace/scratch').glob('ocr-room-candidates-20261016-v1/*oracle*.json'));paths.extend((REPO/'tools').glob('ocr-code-comparison/*oracle*.json'));paths.extend(Path('/workspace/scratch').rglob('cache.json'));paths=[p for p in paths if not p.is_relative_to(PARENT)];paths.extend((REPO/'research-results').rglob('*.json'));paths.extend((REPO/'research-results').rglob('*.jsonl'))
 used=set();receipts=[]
 def walk(value):
  if isinstance(value,str) and len(value)<=256:used.add(value)
  elif isinstance(value,list):
   for item in value:walk(item)
  elif isinstance(value,dict):
   for item in value.values():walk(item)
 for path in sorted(set(paths)):
  if not path.exists():continue
  payload=path.read_bytes();receipts.append({'path':str(path),'sha256':hashlib.sha256(payload).hexdigest()});content=payload.decode('utf-8')
  if path.suffix=='.jsonl':
   for line in content.splitlines():
    if line.strip():walk(json.loads(line))
  else:walk(json.loads(content))
 save(ROOT/'exclusion-receipt.json',{'purpose':'Exact-target exclusion only; no school originals or prior-label training examples','files':receipts,'excludedStringCount':len(used),'setSha256':hashlib.sha256(json.dumps(sorted(used),ensure_ascii=False).encode('utf-8')).hexdigest()})
 return used


def phrase(rng,split):
 nouns=TRAIN_NOUNS if split=='train' else DEV_NOUNS
 return rng.choice(nouns)+rng.choice(['の','と','や','に'])+rng.choice(nouns)


def code(rng,confusable=False,printable=False):
 if printable:return ''.join(rng.choice(PRINTABLE) for _ in range(rng.randint(3,12))).strip() or 'qZ7'
 if not confusable and rng.random()<.5:return rng.choice('ABCDEFGHIJKLMNOPQRSTUVWXYZ')+''.join(rng.choice('0123456789') for _ in range(rng.randint(1,3)))+rng.choice(['','A','B','I','l','O','0'])
 if confusable:return rng.choice(LETTERS)+''.join(rng.choice('Il1O0') for _ in range(rng.randint(2,7)))+rng.choice('_- ')+''.join(rng.choice(ALNUM) for _ in range(2))
 result=''.join(rng.choice(ALNUM) for _ in range(rng.randint(2,10)))
 if len(result)>2 and rng.random()<.5:
  cut=rng.randrange(1,len(result));result=result[:cut]+rng.choice('_- ')+result[cut:]
 return result


def changed(value,rng,confusable):
 positions=[i for i,c in enumerate(value) if c in 'Il1O0'] if confusable else [i for i,c in enumerate(value) if c in LETTERS]
 if not positions:return None
 position=rng.choice(positions);char=value[position];replacement=rng.choice([x for x in ('Il1' if char in 'Il1' else 'O0') if x!=char]) if confusable else char.swapcase()
 return value[:position]+replacement+value[position+1:]


def render(value,name,seed):
 rng=random.Random(seed);density=rng.choices([1,2,4],[.4,.4,.2])[0];size=rng.randint(14,28)*density;face=font(name,size);ascent,descent=face.getmetrics();margin=rng.randint(3,9)*density
 image=Image.new('RGB',(max(12,math.ceil(face.getlength(value))+2*margin),ascent+descent+2*margin),'white');baseline=margin+ascent
 ImageDraw.Draw(image).text((margin,baseline),value,font=face,fill=rng.choice([(5,5,5),(0,0,95),(80,80,80),(0,75,30)]),anchor='ls')
 if rng.random()<.1:image=image.filter(ImageFilter.GaussianBlur(rng.uniform(.1,.25)*density))
 return image,{'fontSize':size,'density':density,'baseline':baseline,'margin':margin}


def frame(image):
 rgb=np.asarray(image);ink=np.any(rgb!=255,axis=2);ys,xs=np.where(ink)
 if len(xs):box=(max(0,int(xs.min())-2),max(0,int(ys.min())-2),min(image.width,int(xs.max())+3),min(image.height,int(ys.max())+3))
 else:box=(0,0,image.width,image.height)
 crop=image.crop(box);assert int(ink.sum())==int(np.any(np.asarray(crop)!=255,axis=2).sum())
 return crop,{'crop':list(box),'originalWidth':image.width,'originalHeight':image.height,'allNonwhiteRetained':True,'providedNonwhiteSamples':int(ink.sum()),'originalRgbSha256':hashlib.sha256(rgb.tobytes()).hexdigest(),'borderInk':bool(ink[0].any() or ink[-1].any() or ink[:,0].any() or ink[:,-1].any())}


TRAIN_EN=['amber','violet','maple','river','garden','orchard','hazel','daisy','prairie','harbor','autumn','sparrow','pebble','valley','sunlight','oak','elm','robin','poppy','clouds','bells','flowers','birds','waves']
DEV_EN=['silver','golden','sequoia','beach','marsh','forest','canyon','nightfall','lilac','spring','summit','cricket','coral','island','moonlight','ash','fir','wren','iris','streams','chimes','blossoms','insects','leaves']
def natural(rng,split):
 words=TRAIN_EN if split=='train' else DEV_EN
 a,b=rng.sample(words,2)
 templates=[a+' and '+b, 'The '+a+' is quiet.', 'We see '+a+' today.', a+' grows near '+b, 'Fresh '+a+' at dawn.', 'Light over the '+a, 'A calm '+a+' morning.']
 value=rng.choice(templates)
 return rng.choice([str.lower,str.upper,lambda x:x])(value)
def variants(value,confusable):
 result=[]
 for i,c in enumerate(value):
  options=[x for x in ('Il1' if c in 'Il1' else 'O0') if x!=c] if confusable and c in 'Il1O0' else [c.swapcase()] if not confusable and c in LETTERS else []
  result.extend(value[:i]+new+value[i+1:] for new in options)
 return result
def generate(protocol,split,forbidden):
 directory=ROOT/split
 if directory.exists():raise ValueError('Corpus already frozen')
 directory.mkdir();spec=protocol[split];rng=random.Random(spec['seed']);coverage=list(PRINTABLE);random.Random(spec['seed']+7919).shuffle(coverage);used=set(forbidden);rows=[];counts={**spec['positiveCounts'],**spec['negativeCounts']};positive=set(spec['positiveCounts'])
 for kind,count in counts.items():
  negative=kind not in positive;pair=None
  for j in range(count):
   paired=kind in ['code','lookalike'];name=spec['fonts'][(j//2 if paired else j)%len(spec['fonts'])]
   if kind in ['unknown_cjk','unknown_mixed']:name='NotoSansJP400' if split=='train' else 'NotoSerifJP400'
   while True:
    seed=rng.randrange(2**31)
    if paired and j%2 and pair is not None:
     oldvalue,seed,options=pair;allowed=[v for v in options if v not in used]
     assert allowed;value=rng.choice(allowed)
    elif kind=='lookalike':value=code(rng,True)
    elif kind=='natural':
     value=natural(rng,split)
    elif kind=='printable':
     chunk=''.join(coverage[(j*8+k)%len(coverage)] for k in range(8));value=rng.choice(LETTERS)+chunk+''.join(rng.choice(ALNUM) for _ in range(2))
    elif kind=='unknown_cjk':value=phrase(rng,split)
    elif kind=='unknown_mixed':value=phrase(rng,split)+rng.choice([':','：',' '])+code(rng,True)
    else:value=code(rng,j%2==0)
    if value in used or any(ord(c) not in cmap(name) for c in value):pair=None;continue
    if paired and not j%2:
     options=[v for v in variants(value,kind=='lookalike') if v not in used and all(ord(c) in cmap(name) for c in v)]
     if not options:continue
    image,geometry=render(value,name,seed);before=int(np.any(np.asarray(image)!=255,axis=2).sum());clip_top=0
    if kind=='clipped':
     ys,xs=np.where(np.any(np.asarray(image)!=255,axis=2));top,bottom=int(ys.min()),int(ys.max())+1
     if rng.random()<.5:cut=top+max(1,int((bottom-top)*rng.uniform(.2,.4)));clip_top=cut;image=image.crop((0,cut,image.width,image.height))
     else:cut=bottom-max(1,int((bottom-top)*rng.uniform(.2,.4)));image=image.crop((0,0,image.width,cut))
    elif kind=='interior_damage':
     rgb=np.asarray(image).copy();ys,xs=np.where(np.any(rgb!=255,axis=2));pick=rng.randrange(len(xs));x,y=int(xs[pick]),int(ys[pick]);radius=max(1,int(geometry['fontSize']*.08));rgb[max(1,y-radius):min(image.height-1,y+radius+1),max(1,x-radius):min(image.width-1,x+radius+1)]=255;image=Image.fromarray(rgb)
     if int(np.any(rgb!=255,axis=2).sum())>=before:continue
    elif kind=='multiline':
     multi=Image.new('RGB',(image.width,image.height*2+8),'white');multi.paste(image,(0,0));multi.paste(image,(0,image.height+8));image=multi
    elif kind=='noise':
     image.paste('white',(0,0,image.width,image.height));draw=ImageDraw.Draw(image)
     for _ in range(rng.randint(4,10)):draw.line([rng.randrange(image.width),rng.randrange(image.height),rng.randrange(image.width),rng.randrange(image.height)],fill='black',width=rng.randint(1,4))
    elif kind=='blank':image.paste('white',(0,0,image.width,image.height))
    image,source=frame(image);source['framedBaseline']=geometry['baseline']-clip_top-source['crop'][1];source['preDamageNonwhiteSamples']=before;source['removedNonwhiteSamples']=before-source['providedNonwhiteSamples'] if kind in ['clipped','interior_damage'] else 0
    try:array,valid=tensor(image)
    except ValueError:pair=None;continue
    buffer=io.BytesIO();image.save(buffer,format='PNG')
    if hashlib.sha256(buffer.getvalue()).hexdigest() in forbidden or hashlib.sha256(array.tobytes()).hexdigest() in forbidden:pair=None;continue
    break
   used.add(value)
   if paired and not j%2:pair=(value,seed,options)
   path=directory/f'{len(rows):05d}.png';image.save(path)
   rows.append({'id':f'{split}-{len(rows):05d}','kind':kind,'font':name,'text':'' if kind=='blank' else None if negative else value,'renderedLiteral':value,'reject':negative,'ctcEligible':not negative or kind=='blank','selectionPositive':not negative,'file':path.name,'pngSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'inputWidth':array.shape[-1],'validWidth':valid,'source':source,'geometry':geometry,'width':image.width,'height':image.height})
 manifest={'split':split,'protocolSha256':hashlib.sha256(json.dumps(protocol,sort_keys=True).encode('utf-8')).hexdigest(),'fonts':{name:hashlib.sha256(FONTS[name].read_bytes()).hexdigest() for name in spec['fonts']},'rows':rows};save(directory/'manifest.json',manifest);return manifest
