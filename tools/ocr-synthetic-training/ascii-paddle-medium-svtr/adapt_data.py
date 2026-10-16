"""Independent natural mixed lines. No old labels or school originals as examples."""
from functools import lru_cache
import hashlib
import json
import math
from pathlib import Path
import random
import sys

import numpy as np
from PIL import Image,ImageDraw,ImageFilter,ImageFont

sys.path.insert(0,str(Path(__file__).parents[1]))
from image_tensor import tensor

PARENT=Path('/workspace/scratch/ascii-paddle-medium-svtr-20261013-v1/corrected-epsilon-v2')
ROOT=Path('/workspace/scratch/ascii-paddle-medium-svtr-20261013-v1/adaptation-v1')
REPO=Path('/workspace/scratch/takupoke-win')
RESULTS=REPO/'research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-svtr-v1/adaptation-v1'
FONTS={
 'NotoSansJP400':Path('/workspace/scratch/ocr-comparison-20261013/sans-static.ttf'),
 'NotoSerifJP400':Path('/workspace/scratch/ocr-comparison-20261013/serif-static.ttf'),
 **{name:Path('/workspace/scratch/ocr-training-assets')/(name+'.ttf') for name in ['MPLUS1p-Regular','ZenKakuGothicNew-Regular','BIZUDPMincho-Regular']},
 **{name:Path('/workspace/scratch/ascii-paddle-medium-features-20261013-v1/mixed-head-v1/assets')/(name+'.ttf') for name in ['BIZUDGothic-Regular','KaiseiTokumin-Regular']},
}
TRAIN_NOUNS=['朝露','紫雲','涼風','木漏日','深緑','白露','春雨','雨雲','新雪','月光','星影','葉陰','夏空','朝焼','夕凪','緑陰','山霧','清流','風音','水音','紅葉','雪花','雨空','光彩','薄雲','青葉','霞空','遠波','木葉','露草','波間','山風']
DEV_NOUNS=['水鳥','日差','草笛','冬空','海原','山波','小径','薄雪','野菊','白砂','浜辺','木道','稲穂','水滴','小雨','夕日','夏雲','山頂','川辺','雨滴','月明','葉音','雪晴','蒼穹']
LETTERS='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'
ALNUM=LETTERS+'0123456789'
PRINTABLE=''.join(chr(c) for c in range(32,127))


def save(path,value):Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


@lru_cache(maxsize=512)
def font(name,size):return ImageFont.truetype(str(FONTS[name]),size)


def excluded():
 paths=[REPO/'tools/vision-ocr-comparison/oracle.json',Path('/workspace/scratch/physical-frame-fresh-development-20261013/oracle.json')]
 for directory in ['/workspace/scratch/ocr-finetune-20261013','/workspace/scratch/ascii-line-ctc-20261013-v1','/workspace/scratch/ascii-paddle-transfer-20261013-v1','/workspace/scratch/ascii-paddle-medium-features-20261013-v1/mixed-head-v1']:
  paths.extend(Path(directory).glob('*/manifest.json'))
 paths.extend((REPO/'research-results').rglob('*.json'));paths.extend((REPO/'research-results').rglob('*.jsonl'))
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


def generate(protocol,split,forbidden):
 directory=ROOT/split
 if directory.exists():raise ValueError('Corpus already frozen')
 directory.mkdir();spec=protocol[split];rng=random.Random(spec['seed']);used=set(forbidden);rows=[]
 counts=dict(spec['positiveCounts'])
 if split=='development':counts['retention_japanese']=spec['retentionRows']
 negative_kinds=['clipped','interior_damage','multiline','noise','blank']
 counts.update({kind:spec['negativeRows']//5+(spec['negativeRows']%5 if kind=='blank' else 0) for kind in negative_kinds})
 for kind,count in counts.items():
  negative=kind in negative_kinds;pair=None
  for j in range(count):
   while True:
    confusable=kind=='mixed_confusable';paired=kind=='mixed_confusable' or kind=='mixed_code' and j%4<2
    if paired and j%2 and pair is not None:
     old_value,prefix,old_code,seed,name=pair;new_code=changed(old_code,rng,confusable)
     if new_code is None:pair=None;continue
     value=prefix+new_code
    else:
     seed=rng.randrange(2**31);name=spec['fonts'][(j//2 if paired else j)%len(spec['fonts'])]
     if kind in ['japanese','retention_japanese']:prefix='';new_code='';value=phrase(rng,split)
     elif kind=='ascii_short':prefix='';new_code=code(rng,printable=j%4==2);value=new_code
     else:
      prefix=phrase(rng,split)+rng.choice([':','：',' ',': ',' — ']);new_code=code(rng,confusable);value=prefix+new_code
    if value in used:pair=None;continue
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
    provided=image.copy();image,source=frame(image);source['framedBaseline']=geometry['baseline']-clip_top-source['crop'][1];source['preDamageNonwhiteSamples']=before;source['removedNonwhiteSamples']=before-source['providedNonwhiteSamples'] if kind in ['clipped','interior_damage'] else 0
    try:array,valid=tensor(image)
    except ValueError:pair=None;continue
    break
   used.add(value)
   if paired and not j%2:pair=(value,prefix,new_code,seed,name)
   path=directory/f'{len(rows):05d}.png';image.save(path)
   rows.append({'id':f'{split}-{len(rows):05d}','kind':kind,'font':name,'text':'' if kind=='blank' else None if negative else value,'reject':negative,'ctcEligible':not negative or kind=='blank','retention':kind in ['japanese','retention_japanese'],'selectionPositive':not negative and kind!='retention_japanese','file':path.name,'pngSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'inputWidth':array.shape[-1],'validWidth':valid,'source':source,'geometry':geometry,'width':image.width,'height':image.height})
 manifest={'split':split,'protocolSha256':hashlib.sha256(json.dumps(protocol,sort_keys=True).encode('utf-8')).hexdigest(),'fonts':{name:hashlib.sha256(FONTS[name].read_bytes()).hexdigest() for name in spec['fonts']},'rows':rows};save(directory/'manifest.json',manifest)
 return manifest
