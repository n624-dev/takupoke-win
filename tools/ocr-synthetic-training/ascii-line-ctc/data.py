"""Fictional whole-line sources: no known-school values or consumed literals."""
import hashlib
import io
import json
import math
import random
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT=Path('/workspace/scratch/ascii-line-ctc-20261013-v1')
FONTS={
    'NotoSansJP400':Path('/workspace/scratch/ocr-comparison-20261013/sans-static.ttf'),
    'NotoSerifJP400':Path('/workspace/scratch/ocr-comparison-20261013/serif-static.ttf'),
    'MPLUS1p-Regular':Path('/workspace/scratch/ocr-training-assets/MPLUS1p-Regular.ttf'),
    'ZenKakuGothicNew-Regular':Path('/workspace/scratch/ocr-training-assets/ZenKakuGothicNew-Regular.ttf'),
    'BIZUDPMincho-Regular':Path('/workspace/scratch/ocr-training-assets/BIZUDPMincho-Regular.ttf'),
    **{name:ROOT/'assets'/f'{name}.ttf' for name in ['Kosugi-Regular','SawarabiGothic-Regular','SawarabiMincho-Regular']},
}
DICTIONARY=['']+[chr(c) for c in range(32,127)]+['�']
INDEX={c:i for i,c in enumerate(DICTIONARY)}
ASCII='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
CJK='風雲月星花鳥山川海空森林雨雪光影音色青赤白黒緑黄春夏秋冬石岩水火金銀土竹草葉根種枝果湖谷丘波船島岸庭窓箱橋道輪羽夢虹霧朝夕'
WORDS=['blue','silver','quiet','bright','round','gentle','cloud','stone','lantern','river','garden','orbit','paper','copper','shadow','breeze']


def save_json(path,value):
    Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


@lru_cache(maxsize=1024)
def font(name,size):return ImageFont.truetype(str(FONTS[name]),size)


def line_text(rng,kind):
    if kind=='code':return ''.join(rng.choice(ASCII) for _ in range(rng.randint(3,14)))
    if kind=='printable':
        value=''.join(rng.choice(DICTIONARY[1:96]) for _ in range(rng.randint(4,16))).strip()
        return value or 'x'+str(rng.randrange(100000))
    if kind=='lookalike':
        return rng.choice(ASCII[:26])+''.join(rng.choice('Il1O0XYZ278') for _ in range(rng.randint(3,7)))+rng.choice(['_','-',' '])+''.join(rng.choice(ASCII) for _ in range(3))
    if kind=='natural':return rng.choice(WORDS)+' '+rng.choice(WORDS)+' '+''.join(rng.choice(ASCII) for _ in range(4))
    return ''.join(rng.choice(CJK) for _ in range(rng.randint(3,7)))


def draw_line(text,rng,name,density=None):
    density=density or rng.choice([2,4]);size=rng.randint(16,34)*density;face=font(name,size);ascent,descent=face.getmetrics()
    margin=rng.randint(3,10)*density;width=math.ceil(face.getlength(text))+margin*2;height=ascent+descent+margin*2
    image=Image.new('RGB',(max(12,width),height),'white');baseline=margin+ascent
    colour=rng.choice([(5,5,5),(0,0,95),(80,80,80),(0,75,30)])
    ImageDraw.Draw(image).text((margin,baseline),text,font=face,fill=colour,anchor='ls')
    degradation=rng.random()
    if degradation<.15:image=image.filter(ImageFilter.GaussianBlur(rng.uniform(.1,.3)*density))
    elif degradation<.3:
        stream=io.BytesIO();image.save(stream,'JPEG',quality=rng.randint(75,94));image=Image.open(io.BytesIO(stream.getvalue())).convert('RGB')
    line_crop=False
    if rng.random()<.5:
        # Crop the whole line, never individual glyphs. Shared baseline and
        # relative character heights survive, while all original ink remains.
        ys,xs=np.where(np.min(np.asarray(image),axis=2)<200)
        if len(ys):
            pad=rng.randint(2,12)*density;top=max(0,int(ys.min())-pad);bottom=min(height,int(ys.max())+1+pad)
            image=image.crop((0,top,image.width,bottom));baseline-=top;line_crop=True
    return image,{'density':density,'fontSize':size,'baseline':baseline,'canvasHeight':image.height,'margin':margin,'wholeLineInkCrop':line_crop}


def tensor(image):
    rgb=np.asarray(image.convert('RGB'),dtype=np.uint8);height,width=rgb.shape[:2]
    input_width=math.floor(64*max(320/64,width/height))
    if input_width>960:raise ValueError('ASCII recognizer original source exceeds width960')
    valid=min(input_width,math.ceil(64*width/height));xs=np.clip((np.arange(valid)+.5)*width/valid-.5,0,width-1);ys=np.clip((np.arange(64)+.5)*height/64-.5,0,height-1)
    x0=np.floor(xs).astype(int);x1=np.minimum(x0+1,width-1);fx=xs-x0;y0=np.floor(ys).astype(int);y1=np.minimum(y0+1,height-1);fy=ys-y0;pixels=rgb[:,:,::-1].astype(np.float64)
    values=((pixels[y0[:,None],x0[None,:]]*(1-fx)[None,:,None]+pixels[y0[:,None],x1[None,:]]*fx[None,:,None])*(1-fy)[:,None,None]+(pixels[y1[:,None],x0[None,:]]*(1-fx)[None,:,None]+pixels[y1[:,None],x1[None,:]]*fx[None,:,None])*fy[:,None,None])
    values=np.floor(values+.5).astype(np.float32);values=(values/np.float32(255)-np.float32(.5))/np.float32(.5);output=np.zeros((3,64,input_width),dtype=np.float32);output[:,:,:valid]=values.transpose(2,0,1)
    return output,valid


def excluded_literals():
    used=set()
    paths=[Path('/workspace/scratch/takupoke-win/tools/vision-ocr-comparison/oracle.json')]
    paths.extend(Path('/workspace/scratch/ocr-finetune-20261013').glob('*/manifest.json'))
    def walk(value):
        if isinstance(value,str):used.add(value)
        elif isinstance(value,list):
            for child in value:walk(child)
        elif isinstance(value,dict):
            for child in value.values():walk(child)
    for path in paths:
        if path.exists():walk(json.loads(path.read_text(encoding='utf-8')))
    return used


def generate(protocol,split,forbidden):
    directory=ROOT/split
    if directory.exists():raise ValueError('Corpus already exists; never overwrite')
    directory.mkdir();spec=protocol[split];rng=random.Random(spec['seed']);rows=[];used=set(forbidden)
    for i in range(spec['positiveRows']+spec['negativeRows']):
        negative=i>=spec['positiveRows'];kind=(['cjk','clipped','multiline','unknown'][(i-spec['positiveRows'])%4] if negative else ['code','printable','lookalike','natural'][i%4]);name=spec['fonts'][i%len(spec['fonts'])]
        while True:
            text=line_text(rng,'cjk' if kind=='cjk' else 'code' if negative else kind)
            if text not in used:break
        used.add(text);image,geometry=draw_line(text,rng,name)
        if kind=='clipped':
            ys,xs=np.where(np.min(np.asarray(image),axis=2)<200);top=int(ys.min());bottom=int(ys.max())+1
            if rng.random()<.5:
                cut=top+max(1,int((bottom-top)*rng.uniform(.2,.4)));image=image.crop((0,cut,image.width,image.height))
            else:
                cut=bottom-max(1,int((bottom-top)*rng.uniform(.2,.4)));image=image.crop((0,0,image.width,cut))
        elif kind=='multiline':
            other=Image.new('RGB',(image.width,image.height*2+6),'white');other.paste(image,(0,0));other.paste(image,(0,image.height+6));image=other
        elif kind=='unknown':
            draw=ImageDraw.Draw(image);image.paste('white',(0,0,image.width,image.height))
            for _ in range(rng.randint(4,10)):
                coords=[rng.randrange(image.width),rng.randrange(image.height),rng.randrange(image.width),rng.randrange(image.height)];draw.line(coords,fill='black',width=rng.randint(1,4))
        array,valid=tensor(image);path=directory/f'{i:05d}.png';image.save(path)
        rows.append({'id':f'{split}-{i:05d}','kind':kind,'font':name,'text':None if negative else text,'target':'�' if negative else text,'reject':negative,'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'sourceWidth':image.width,'sourceHeight':image.height,'validWidth':valid,'inputWidth':array.shape[-1],'geometry':geometry})
    manifest={'split':split,'protocolSha256':hashlib.sha256(json.dumps(protocol,sort_keys=True).encode('utf-8')).hexdigest(),'fonts':{name:hashlib.sha256(FONTS[name].read_bytes()).hexdigest() for name in spec['fonts']},'rows':rows};save_json(directory/'manifest.json',manifest)
    return manifest


def arrays(manifest):
    result=[]
    for row in manifest['rows']:
        path=ROOT/manifest['split']/row['file']
        if hashlib.sha256(path.read_bytes()).hexdigest()!=row['sha256']:raise ValueError('Pixel integrity')
        value,_=tensor(Image.open(path));result.append(value.astype(np.float16))
    return result
