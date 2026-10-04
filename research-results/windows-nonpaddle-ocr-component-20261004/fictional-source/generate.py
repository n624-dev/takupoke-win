"""Render only wholly invented source; no OCR/models, source documents or network.
The owner supplies an empty, uniquely owned output directory and removes images
after the finite comparison. Literal/position gold is a separate evaluator file.
"""
import argparse
import hashlib
import json
from pathlib import Path
import PIL
from PIL import Image, ImageDraw, ImageFont, features

ROOT = Path(__file__).resolve().parent

def sha(data):
    return hashlib.sha256(data).hexdigest()

def render(source, condition, font_path):
    size = source['renderer']['fontConditionSizes'][condition]
    font = ImageFont.truetype(str(font_path), size, index=0)
    image = Image.new('RGB', (1120, 400), 'white')
    draw = ImageDraw.Draw(image)
    records = []
    def line(points):
        draw.line(points, fill='black', width=2)
    def text(identifier, literal, x, y, cell, kind, **tags):
        # Measure actual painted ink of this original source independently of OCR.
        mask = Image.new('L', image.size, 0)
        ImageDraw.Draw(mask).text((x,y), literal, font=font, fill=255, anchor='lt')
        box = mask.getbbox()
        assert box is not None
        assert 0 <= box[0] < box[2] <= 1120 and 0 <= box[1] < box[3] <= 400
        assert cell[0] < box[0] and cell[1] < box[1] and box[2] < cell[2] and box[3] < cell[3], (identifier, box, cell)
        draw.text((x,y), literal, font=font, fill='black', anchor='lt')
        records.append({'id':identifier,'literal':literal,'paintAnchor':[x,y],
                        'actualOriginalInkBox':list(box),'semanticCell':list(cell),
                        'kind':kind,**tags})
    def centered(identifier, literal, box, kind, **tags):
        probe = Image.new('L',image.size,0)
        ImageDraw.Draw(probe).text((0,0),literal,font=font,fill=255,anchor='lt')
        bounds = probe.getbbox()
        width, height = bounds[2]-bounds[0], bounds[3]-bounds[1]
        x = box[0]+(box[2]-box[0]-width)//2-bounds[0]
        y = box[1]+(box[3]-box[1]-height)//2-bounds[1]
        text(identifier,literal,x,y,box,kind,**tags)
    # Separate small metadata cells; all border endpoints lie inside the canvas.
    draw.rectangle((30,10,190,50),outline='black',width=2)
    draw.rectangle((205,10,295,50),outline='black',width=2)
    centered('year',source['metadata']['yearLiteral'],(30,10,190,50),'year')
    centered('term',source['metadata']['term'],(205,10,295,50),'term')
    text('title',source['metadata']['title'],500,20,(480,10,1060,50),'title')
    for y in (62,130,240,350): line((30,y,1052,y))
    line((172,96,1052,96))
    for x in (30,172,612,1052): line((x,62,x,350))
    for x in (392,832): line((x,96,x,350))
    centered('class-label','クラス',(30,62,172,130),'class-label')
    for index, day in enumerate(source['weekdays']):
        x = 172+index*440
        centered('day-'+str(index),day,(x,62,x+440,96),'weekday',weekday=day)
    for column in range(4):
        period=column%2+1; x=172+column*220
        centered('period-'+str(column),str(period)+'時限',(x,96,x+220,130),
                 'period',weekday=source['weekdays'][column//2],period=period)
    for row, class_name in enumerate(source['classNames']):
        y=130+row*110
        centered('class-'+str(row),class_name,(30,y,172,y+110),'class',className=class_name)
    for index, slot in enumerate(source['slots']):
        row,column=index//4,index%4; left,top=172+column*220,130+row*110
        for line_index,role in enumerate(('subject','teacher','room')):
            y=top+12+line_index*30
            cell=(left,top,left+220,top+110)
            tags={'tupleIndex':index,'className':slot['className'],
                  'weekday':slot['weekday'],'period':slot['period'],'role':role}
            text('slot-'+str(index)+'-'+role+'-label',source['roleLabels'][role],
                 left+12,y,cell,'role-label',**tags)
            text('slot-'+str(index)+'-'+role+'-value',slot[role],
                 left+90,y,cell,'role-value',**tags)
    for i,a in enumerate(records):
        ax1,ay1,ax2,ay2=a['actualOriginalInkBox']
        for b in records[i+1:]:
            bx1,by1,bx2,by2=b['actualOriginalInkBox']
            assert min(ax2,bx2)<=max(ax1,bx1) or min(ay2,by2)<=max(ay1,by1), (a['id'],b['id'])
    return image,records

def generate(output, font_path):
    source_path=ROOT/'drawing-source.json'
    source=json.loads(source_path.read_text(encoding='utf-8'))
    assert PIL.__version__==source['renderer']['Pillow']
    assert features.version('freetype2')==source['renderer']['FreeType']
    assert sha(font_path.read_bytes())==source['font']['expectedSHA256']
    assert font_path.stat().st_size==source['font']['bytes']
    assert len(source['slots'])==8
    assert not output.exists(), 'Output directory must be new and owned by caller'
    output.mkdir(parents=True)
    (output/'.independent-ocr-pair-owned').write_text('new synthetic images only\n')
    receipt={'sourceSHA256':sha(source_path.read_bytes()),'fontSHA256':source['font']['expectedSHA256'],
             'Pillow':PIL.__version__,'FreeType':features.version('freetype2'),
             'nativeCalls':0,'pairedConditions':[]}
    for condition in ('clear','small'):
        image,records=render(source,condition,font_path)
        png=output/(condition+'.png')
        image.save(png,format='PNG',optimize=False,compress_level=6)
        receipt['pairedConditions'].append({'condition':condition,'fontPixels':source['renderer']['fontConditionSizes'][condition],
            'width':1120,'height':400,'pngBytes':png.stat().st_size,'pngSHA256':sha(png.read_bytes()),
            'RGBSHA256':sha(image.tobytes()),'sourceTextRegions':len(records),
            'sourceAllInkWithinSemanticCells':True,'textInkRegionsDoNotOverlap':True})
    (output/'generation-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
    return receipt

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--output',required=True,type=Path)
    parser.add_argument('--font',required=True,type=Path)
    args=parser.parse_args()
    print(json.dumps(generate(args.output,args.font),indent=2))
