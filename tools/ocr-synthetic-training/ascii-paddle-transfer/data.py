"""Independent fictional sources, with complete original-ink whole-line framing."""
from functools import lru_cache
import hashlib
import json
import math
from pathlib import Path
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

sys.path.insert(0,str(Path(__file__).parents[1]))
from image_tensor import tensor

ROOT=Path('/workspace/scratch/ascii-paddle-transfer-20261013-v1')
REPO=Path('/workspace/scratch/takupoke-win')
FONTS={
    'NotoSansJP400':Path('/workspace/scratch/ocr-comparison-20261013/sans-static.ttf'),
    'NotoSerifJP400':Path('/workspace/scratch/ocr-comparison-20261013/serif-static.ttf'),
    **{name:Path('/workspace/scratch/ocr-training-assets')/(name+'.ttf') for name in ['MPLUS1p-Regular','ZenKakuGothicNew-Regular','BIZUDPMincho-Regular']},
    **{name:ROOT/'assets'/(name+'.ttf') for name in ['KiwiMaru-Regular','HinaMincho-Regular']},
}
DICTIONARY=['']+[chr(c) for c in range(32,127)]+['�']
INDEX={c:i for i,c in enumerate(DICTIONARY)}
LETTERS='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'
ALNUM=LETTERS+'0123456789'
CJK='風雲月星花鳥山川海空森林雨雪光影音色青赤白黒緑黄春夏秋冬石岩水火金銀土竹草葉根種枝果湖谷丘波船島岸庭窓箱橋道輪羽夢虹霧朝夕'


def save(path,value):
    Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


@lru_cache(maxsize=512)
def font(name,size):
    return ImageFont.truetype(str(FONTS[name]),size)


def exclusion():
    paths=[REPO/'tools/vision-ocr-comparison/oracle.json',Path('/workspace/scratch/physical-frame-fresh-development-20261013/oracle.json')]
    paths.extend(Path('/workspace/scratch/ocr-finetune-20261013').glob('*/manifest.json'))
    paths.extend(Path('/workspace/scratch/ascii-line-ctc-20261013-v1').glob('*/manifest.json'))
    paths.extend((REPO/'research-results').rglob('*.json'))
    paths.extend((REPO/'research-results').rglob('*.jsonl'))
    used=set();receipts=[]
    def walk(value):
        if isinstance(value,str) and len(value)<=256:used.add(value)
        elif isinstance(value,list):
            for child in value:walk(child)
        elif isinstance(value,dict):
            for child in value.values():walk(child)
    for path in sorted(set(paths)):
        if not path.exists():continue
        payload=path.read_bytes();receipts.append({'path':str(path),'sha256':hashlib.sha256(payload).hexdigest()})
        content=payload.decode('utf-8')
        if path.suffix=='.jsonl':
            for line in content.splitlines():
                if line.strip():walk(json.loads(line))
        else:walk(json.loads(content))
    save(ROOT/'exclusion-receipt.json',{'purpose':'Exclusion only. Prior labels never become training targets. No school originals.', 'files':receipts,'excludedStringCount':len(used),'stringSetSha256':hashlib.sha256(json.dumps(sorted(used),ensure_ascii=False).encode('utf-8')).hexdigest()})
    return used


def text(rng,kind):
    if kind=='code':
        value=''.join(rng.choice(ALNUM) for _ in range(rng.randint(3,12)))
        if rng.random()<.5:
            cut=rng.randrange(1,len(value));value=value[:cut]+rng.choice('_- ')+value[cut:]
        return value
    if kind=='case':return ''.join(rng.choice(LETTERS) for _ in range(rng.randint(3,10)))+rng.choice('_- ')+''.join(rng.choice(ALNUM) for _ in range(2))
    if kind=='confusable':return rng.choice(LETTERS)+''.join(rng.choice('Il1O0') for _ in range(rng.randint(2,7)))+rng.choice('_- ')+''.join(rng.choice(ALNUM) for _ in range(2))
    if kind=='printable':return ''.join(rng.choice(DICTIONARY[1:96]) for _ in range(rng.randint(3,14))).strip() or 'xyz'
    if kind=='cjk':return ''.join(rng.choice(CJK) for _ in range(rng.randint(2,6)))
    if kind=='mixed':return rng.choice(CJK)+text(rng,'code')+rng.choice(CJK)
    return text(rng,'code')


def counterpart(value,rng,kind):
    positions=[i for i,c in enumerate(value) if c.isalpha()] if kind=='case' else [i for i,c in enumerate(value) if c in 'Il1O0']
    position=rng.choice(positions);char=value[position]
    replacement=char.swapcase() if kind=='case' else rng.choice([c for c in ('Il1' if char in 'Il1' else 'O0') if c!=char])
    return value[:position]+replacement+value[position+1:]


def draw(text_value,name,seed):
    rng=random.Random(seed);density=rng.choice([2,4]);size=rng.randint(16,32)*density;face=font(name,size)
    ascent,descent=face.getmetrics();margin=rng.randint(3,9)*density
    image=Image.new('RGB',(max(12,math.ceil(face.getlength(text_value))+margin*2),ascent+descent+margin*2),'white')
    baseline=margin+ascent
    ImageDraw.Draw(image).text((margin,baseline),text_value,font=face,fill=rng.choice([(5,5,5),(0,0,95),(80,80,80),(0,75,30)]),anchor='ls')
    if rng.random()<.1:image=image.filter(ImageFilter.GaussianBlur(rng.uniform(.1,.25)*density))
    return image,{'fontSize':size,'density':density,'baseline':baseline,'margin':margin}


def frame(image):
    rgb=np.asarray(image);foreground=np.any(rgb!=255,axis=2);ys,xs=np.where(foreground)
    if len(xs):box=(max(0,int(xs.min())-2),max(0,int(ys.min())-2),min(image.width,int(xs.max())+3),min(image.height,int(ys.max())+3))
    else:box=(0,0,image.width,image.height)
    crop=image.crop(box)
    assert int(foreground.sum())==int(np.any(np.asarray(crop)!=255,axis=2).sum())
    return crop,{'crop':list(box),'originalWidth':image.width,'originalHeight':image.height,'nonwhiteSamples':int(foreground.sum()),'allNonwhiteRetained':True,'borderInk':bool(foreground[0].any() or foreground[-1].any() or foreground[:,0].any() or foreground[:,-1].any())}


def generate(protocol,split,forbidden):
    directory=ROOT/split
    if directory.exists():raise ValueError('Independent split already generated')
    directory.mkdir();spec=protocol[split];rng=random.Random(spec['seed']);rows=[];used=set(forbidden)
    kinds=protocol['positiveKinds'];per_kind=spec['positiveRows']//len(kinds)
    for kind in kinds+protocol['negativeKinds']:
        negative=kind in protocol['negativeKinds'];count=spec['negativeRows']//len(protocol['negativeKinds']) if negative else per_kind
        # Training negative count may have a small remainder; assigned deterministically.
        if negative and kind==protocol['negativeKinds'][-1]:count+=spec['negativeRows']%len(protocol['negativeKinds'])
        pair=None
        for j in range(count):
            while True:
                if not negative and kind in ['case','confusable'] and j%2 and pair is not None:
                    value=counterpart(pair[0],rng,kind);seed=pair[1];name=pair[2]
                else:
                    value=text(rng,kind);seed=rng.randrange(2**31);name=spec['fonts'][(j//2 if kind in ['case','confusable'] else j)%len(spec['fonts'])]
                if value in used:
                    pair=None;continue
                image,geometry=draw(value,name,seed)
                pre_damage_nonwhite=int(np.any(np.asarray(image)!=255,axis=2).sum());clip_top=0
                if kind=='clipped':
                    ys,xs=np.where(np.any(np.asarray(image)!=255,axis=2));top,bottom=int(ys.min()),int(ys.max())+1
                    if rng.random()<.5:cut=top+max(1,int((bottom-top)*rng.uniform(.2,.4)));clip_top=cut;image=image.crop((0,cut,image.width,image.height))
                    else:cut=bottom-max(1,int((bottom-top)*rng.uniform(.2,.4)));image=image.crop((0,0,image.width,cut))
                elif kind=='multiline':
                    combined=Image.new('RGB',(image.width,image.height*2+8),'white');combined.paste(image,(0,0));combined.paste(image,(0,image.height+8));image=combined
                elif kind=='noise':
                    image.paste('white',(0,0,image.width,image.height));ink=ImageDraw.Draw(image)
                    for _ in range(rng.randint(4,10)):ink.line([rng.randrange(image.width),rng.randrange(image.height),rng.randrange(image.width),rng.randrange(image.height)],fill='black',width=rng.randint(1,4))
                elif kind=='blank':image.paste('white',(0,0,image.width,image.height))
                original=image.copy();image,source=frame(image)
                source['preDamageNonwhiteSamples']=pre_damage_nonwhite
                source['removedNonwhiteSamples']=pre_damage_nonwhite-source['nonwhiteSamples'] if kind=='clipped' else 0
                source['framedBaseline']=geometry['baseline']-clip_top-source['crop'][1]
                source['retainedScope']='All nonwhite in the provided original crop; synthetic clipping removes ink before this framing step'
                try:array,valid=tensor(image)
                except ValueError:
                    pair=None;continue
                break
            used.add(value)
            if not negative and kind in ['case','confusable'] and not j%2:pair=(value,seed,name)
            path=directory/f'{len(rows):05d}.png';image.save(path)
            rows.append({'id':f'{split}-{len(rows):05d}','kind':kind,'font':name,'text':None if negative else value,'target':'�' if negative else value,'reject':negative,'file':path.name,'pngSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'originalRgbSha256':hashlib.sha256(np.asarray(original).tobytes()).hexdigest(),'geometry':geometry,'source':source,'width':image.width,'height':image.height,'inputWidth':array.shape[-1],'validWidth':valid})
    manifest={'split':split,'protocolSha256':hashlib.sha256(json.dumps(protocol,sort_keys=True).encode('utf-8')).hexdigest(),'fonts':{name:hashlib.sha256(FONTS[name].read_bytes()).hexdigest() for name in spec['fonts']},'rows':rows}
    save(directory/'manifest.json',manifest)
    assert len(rows)==spec['positiveRows']+spec['negativeRows']
    return manifest


def arrays(manifest):
    result=[]
    for row in manifest['rows']:
        path=ROOT/manifest['split']/row['file'];assert hashlib.sha256(path.read_bytes()).hexdigest()==row['pngSha256']
        value,valid=tensor(Image.open(path));assert valid==row['validWidth'];result.append(value.astype(np.float16))
    return result
