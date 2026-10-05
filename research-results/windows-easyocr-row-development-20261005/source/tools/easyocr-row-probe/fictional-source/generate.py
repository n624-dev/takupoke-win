"""Entirely invented input. No original PDF, OCR output, model or network read.

Native owner receives only the separately generated pixels, not the drawing
glyph inventory or literal formal oracle. Emit one source-fixed baseline once;
do not tune the source or expected values from OCR observations.
"""
from pathlib import Path
import argparse, hashlib, io, json, math
import PIL
from PIL import Image, ImageDraw, ImageFont, features

ROOT = Path(__file__).resolve().parent
FONT_SHA = 'b76b0433203017ca80401b2ee0dd69350349871c4b19d504c34dbdd80541690a'
def sha(b): return hashlib.sha256(b).hexdigest()
def write(path, value): path.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
def render(source, font_path):
    assert sha(font_path.read_bytes()) == FONT_SHA
    assert PIL.__version__ == '12.3.0'
    w,h = source['width'],source['height']
    image=Image.new('RGB',(w,h),'white');draw=ImageDraw.Draw(image)
    font=ImageFont.truetype(str(font_path),source['fontSize'],index=0)
    glyphs=[];paint=[];rules=[]
    def rule(x1,y1,x2,y2):
        assert 0<x1<w and 0<x2<w and 0<y1<h and 0<y2<h
        draw.line((x1,y1,x2,y2),fill='black',width=2)
        rules.append({'x1':x1,'y1':y1,'x2':x2,'y2':y2})
    def text(identifier,literal,box,kind,**tags):
        advances=[math.ceil(font.getlength(c)) for c in literal]
        total=sum(advances);line_height=26
        x=box[0]+(box[2]-box[0]-total)//2
        y=box[1]+(box[3]-box[1]-line_height)//2
        assert box[0]<x and x+total<box[2] and box[1]<y and y+line_height<box[3]
        actual=[]
        for index,(c,advance) in enumerate(zip(literal,advances)):
            mask=Image.new('L',(w,h),0);ImageDraw.Draw(mask).text((x,y),c,font=font,fill=255,anchor='lt')
            ink=mask.getbbox()
            # Bounds use the actual renderer's declared advance and line baseline,
            # not guessed subdivision of an OCR whole-line box. Ink is separate.
            bounds=[x,y,x+advance,y+line_height]
            if ink is not None:
                assert box[0]<ink[0] and box[1]<ink[1] and ink[2]<box[2] and ink[3]<box[3],(identifier,c,ink,box)
                assert bounds[0]<=ink[0] and bounds[1]<=ink[1] and ink[2]<=bounds[2] and ink[3]<=bounds[3],(c,ink,bounds)
                actual.append(ink)
            draw.text((x,y),c,font=font,fill='black',anchor='lt')
            glyphs.append({'id':identifier+'-'+str(index),'text':c,'box':bounds,'fontSize':source['fontSize'],'sourceLine':len(paint),'sourceOrder':len(glyphs),'kind':kind})
            x+=advance
        inkbox=[min(b[0] for b in actual),min(b[1] for b in actual),max(b[2] for b in actual),max(b[3] for b in actual)]
        paint.append({'id':identifier,'literal':literal,'semanticBox':box,'actualInkBox':inkbox,'kind':kind,**tags})
    left,grade_right,body_left,cell=20,60,120,90
    right=body_left+40*cell;day_top,period_top,body_top,bottom=70,104,136,232
    for y in [day_top,body_top,bottom]:rule(left,y,right,y)
    rule(body_left,period_top,right,period_top)
    for x in [left,grade_right,body_left]:rule(x,day_top,x,bottom)
    for i in range(1,41):rule(body_left+i*cell,day_top if i%8==0 else period_top,body_left+i*cell,bottom)
    text('year','令和14年度',[20,2,280,50],'year')
    text('term','前期',[310,2,430,50],'term')
    text('title','時間割',[480,2,680,50],'title')
    text('grade','1',[left,body_top,grade_right,bottom],'grade')
    text('class','2',[grade_right,body_top,body_left,bottom],'class')
    for d,day in enumerate(source['days']):
        x=body_left+d*8*cell
        text('day-'+str(d),day['printed'],[x,day_top,x+8*cell,period_top],'day',day=day['value'])
        for p in range(1,9):
            cx=x+(p-1)*cell
            text('period-'+str(d)+'-'+str(p),str(p),[cx,period_top,cx+cell,body_top],'period',day=day['value'],period=p)
    for lesson in source['lessons']:
        d=next(i for i,v in enumerate(source['days']) if v['value']==lesson['day']);p=lesson['period']
        x=body_left+(d*8+p-1)*cell
        for r,role in enumerate(['subject','teacher','room']):
            band=[x,body_top+r*32,x+cell,body_top+(r+1)*32]
            text('body-'+str(d)+'-'+str(p)+'-'+role,lesson[role],band,'body',className=source['className'],day=lesson['day'],period=p,role=role)
    b=io.BytesIO();image.save(b,format='PNG',compress_level=9)
    return b.getvalue(),{'width':w,'height':h,'glyphs':glyphs,'rules':rules,'paint':paint,'glyphGeometryScope':'Actual source renderer advance/line boxes; original ink bounds separately retained. Not OCR charboxes.'}

def main():
    a=argparse.ArgumentParser();a.add_argument('--font',type=Path,default=Path('/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'));a.add_argument('--owned-output',type=Path);args=a.parse_args()
    source=json.loads((ROOT/'drawing-source.json').read_text(encoding='utf-8'))
    pixels,drawing=render(source,args.font)
    if args.owned_output:
        # A consumer regenerates only pixels and never rewrites the frozen
        # evaluator records, source pins or inference-status history.
        frozen=json.loads((ROOT/'frozen.json').read_text(encoding='utf-8'))
        for pin in frozen['files']:
            data=(ROOT/pin['path']).read_bytes()
            assert len(data)==pin['bytes'] and sha(data)==pin['sha256']
        assert len(pixels)==frozen['PNGBytes'] and sha(pixels)==frozen['PNGSHA256']
        assert args.owned_output.is_dir() and (args.owned_output/'.owned-synthetic-ocr').is_file()
        target=args.owned_output/'unlabeled-baseline.png'
        with target.open('xb') as f:f.write(pixels)
        print(json.dumps({'PNGSHA256':sha(pixels),'PNGBytes':len(pixels),'OCRCalls':0,'fixture':str(ROOT),'frozenRecordsRewritten':False}))
        return
    write(ROOT/'drawing-glyph-preflight.json',drawing)
    oracle={'documentKind':'timetable','schoolYear':2032,'term':'前期','classes':[source['className']],'days':[d['value'] for d in source['days']],'requiredSlots':[{'className':source['className'],'day':d['value'],'period':p} for d in source['days'] for p in range(1,9)],'lessons':[{'className':source['className'],**l} for l in source['lessons']],'expectedLessonCount':40,'expectedSlots':40,'parallelCountPerCell':1,'semantics':'No label prefixes. Printed cell top/middle/bottom lines under existing trusted Strict template identify subject/teacher/room. Full formal proof depends on actual Builder preflight; no native/gold role assignment.'}
    write(ROOT/'literal-formal-oracle.json',oracle)
    receipt={'phase':'Fresh invented unlabeled baseline source freeze; not final merged/parallel/blank coverage','inputIndependentOfOCROutputs':True,'OCRNativeCalls':0,'modelCalls':0,'fontSHA256':FONT_SHA,'fontLicense':'SIL Open Font License1.1; Noto Sans CJK JP Regular2.004','Pillow':PIL.__version__,'FreeType':features.version('freetype2'),'PNGBytes':len(pixels),'PNGSHA256':sha(pixels),'glyphCount':len(drawing['glyphs']),'printedLines':len(drawing['paint']),'sourceContractPreflight':'Pending unchanged Strict/Builder/Rules/Engine/Validator/fullformal, not assumed success','files':[{'path':n,'bytes':(ROOT/n).stat().st_size,'sha256':sha((ROOT/n).read_bytes())} for n in ['drawing-source.json','generate.py','drawing-glyph-preflight.json','literal-formal-oracle.json']]}
    write(ROOT/'frozen.json',receipt)
    print(json.dumps({'PNGSHA256':receipt['PNGSHA256'],'PNGBytes':len(pixels),'OCRCalls':0,'fixture':str(ROOT)}))
if __name__=='__main__':main()
