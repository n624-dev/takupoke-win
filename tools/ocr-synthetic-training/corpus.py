"""Independent synthetic text, pinned fonts and immutable pixel manifests.

Never imports consumed comparison data or real school documents.
"""
import hashlib
import io
import json
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

from image_tensor import tensor

FONTS = {
    'NotoSansJP400': '/workspace/scratch/ocr-comparison-20261013/sans-static.ttf',
    'NotoSerifJP400': '/workspace/scratch/ocr-comparison-20261013/serif-static.ttf',
    'MPLUS1p-Regular': '/workspace/scratch/ocr-training-assets/MPLUS1p-Regular.ttf',
    'ZenKakuGothicNew-Regular': '/workspace/scratch/ocr-training-assets/ZenKakuGothicNew-Regular.ttf',
    'BIZUDPMincho-Regular': '/workspace/scratch/ocr-training-assets/BIZUDPMincho-Regular.ttf',
}
KANJI = '風雲月星花鳥山川海空森林雨雪光影音色青赤白黒緑黄春夏秋冬東西南北大小長短新古高低強弱遠近静動明暗深浅早夜石岩水火金銀土竹草葉根種枝果湖谷丘波船島岸庭窓箱橋道輪羽夢虹霧朝夕坂門本頁絵筆糸布玉貝魚猫犬虫豆米茶香塩砂線円角形点記号番号図棚杯瓶皿椅机灯鏡鍵鈴弓矢刀糧塔城園旅道楽勇仁礼和安満福平晴読写歩走泳飛跳眠起笑泣声話言思考聞見集散開閉続停始終順逆左中央右上下面内外'
KANA = 'あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわんアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワンー'
ASCII = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
CONFUSABLE = 'O0Il1'
CATEGORIES = ['confusable-code', 'arbitrary-ascii', 'japanese', 'mixed']


def text_for(rng, category):
    if category == 'confusable-code':
        # Arbitrary codes, no room-number grammar or correction dictionary.
        alphabet = CONFUSABLE + 'ABXYZ25689'
        return ''.join(rng.choice(alphabet) for _ in range(rng.randint(3, 9)))
    if category == 'arbitrary-ascii':
        parts = [''.join(rng.choice(ASCII) for _ in range(rng.randint(2, 6))) for _ in range(rng.randint(1, 3))]
        return rng.choice(['-', '_', ' ', '/', '.']).join(parts)
    if category == 'japanese':
        alphabet = KANJI * 2 + KANA
        return ''.join(rng.choice(alphabet) for _ in range(rng.randint(3, 9)))
    return ''.join(rng.choice(KANJI) for _ in range(rng.randint(2, 4))) + rng.choice([' ', '-', '']) + ''.join(rng.choice(ASCII) for _ in range(rng.randint(2, 5)))


def render(text, rng, font_path, control=None):
    size = rng.randint(19, 33)
    font = ImageFont.truetype(font_path, size)
    bbox = font.getbbox(text or '空')
    spacing = rng.choice([0, 0, 0, 1, 2])
    widths = [font.getlength(char) for char in text]
    width = max(16, int(sum(widths) + spacing * max(0, len(text)-1)) + rng.randint(4, 16))
    height = max(size+5, bbox[3]-bbox[1]+rng.randint(6, 15))
    image = Image.new('RGB', (width, height), (255, 255, 255))
    draw = ImageDraw.Draw(image)
    x = rng.randint(2, 5)
    y = rng.randint(2, 6)-bbox[1]
    shade = rng.randint(60, 115) if rng.random()<.1 else rng.randint(0, 30)
    for char, advance in zip(text, widths):
        draw.text((x, y), char, font=font, fill=(shade,shade,shade))
        x += advance+spacing
    degradation = rng.random()
    if degradation < .2:
        image = image.filter(ImageFilter.GaussianBlur(rng.uniform(.15,.45)))
    elif degradation < .4:
        buf = io.BytesIO(); image.save(buf, format='JPEG', quality=rng.randint(65,88))
        image = Image.open(io.BytesIO(buf.getvalue())).convert('RGB')
    if control == 'clipped':
        image = image.crop((0, max(1, height//3), width, height))
    elif control == 'multiline':
        result = Image.new('RGB', (width, height*2+3), 'white')
        result.paste(image, (0,0)); result.paste(image,(0,height+3)); image=result
    return image


def generate(protocol, split, directory, forbidden=()):
    directory = Path(directory)
    if directory.exists():
        manifest=json.loads((directory/'manifest.json').read_text(encoding='utf-8'))
        if manifest['protocolSha256'] != hashlib.sha256(json.dumps(protocol,sort_keys=True).encode()).hexdigest():
            raise ValueError('Existing corpus protocol mismatch')
        return manifest
    directory.mkdir(parents=True)
    spec = protocol[split]; rng = random.Random(spec['seed']); used=set(forbidden); rows=[]
    for i in range(spec['rows']):
        category=CATEGORIES[i%4]
        while True:
            label=text_for(rng,category)
            if label not in used:break
        used.add(label)
        font_name=spec['fonts'][i%len(spec['fonts'])] if len(spec['fonts'])==1 else spec['fonts'][(i//4)%len(spec['fonts'])]
        image=render(label,rng,FONTS[font_name])
        path=directory/f'{i:04d}.png';image.save(path)
        array,valid=tensor(image)
        rows.append({'id':f'{split}-{i:04d}','category':category,'font':font_name,'text':label,'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'width':image.width,'height':image.height,'validWidth':valid,'inputWidth':array.shape[-1]})
    # Controls are not scored as ordinary exact-match recognition.
    controls=[]
    for control in ['blank','clipped','multiline']:
        for i in range(16):
            font_name=spec['fonts'][i%len(spec['fonts'])];label=text_for(rng,CATEGORIES[i%4]) if control!='blank' else ''
            image=render(label,rng,FONTS[font_name],control)
            path=directory/f'control-{control}-{i:02d}.png';image.save(path)
            controls.append({'id':f'{split}-{control}-{i:02d}','category':control,'font':font_name,'text':label,'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
    manifest={'split':split,'protocolSha256':hashlib.sha256(json.dumps(protocol,sort_keys=True).encode()).hexdigest(),'fontSha256':{n:hashlib.sha256(Path(FONTS[n]).read_bytes()).hexdigest() for n in spec['fonts']},'rows':rows,'controls':controls,'provenance':'independent fictional strings; no school originals or consumed comparison corpus'}
    (directory/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n')
    return manifest


def load_arrays(manifest, directory, controls=False):
    rows=manifest['controls'] if controls else manifest['rows'];arrays=[]
    for row in rows:
        path=Path(directory)/row['file']
        if hashlib.sha256(path.read_bytes()).hexdigest()!=row['sha256']:raise ValueError('Pixel integrity')
        arrays.append(tensor(Image.open(path))[0])
    return arrays
