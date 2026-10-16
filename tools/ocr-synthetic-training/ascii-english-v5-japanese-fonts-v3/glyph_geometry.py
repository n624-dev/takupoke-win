"""Post-fresh source/normalized input distinguishability; no recognition or label fitting."""
from collections import Counter
import hashlib,itertools,json,math
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
from fontTools.ttLib import TTFont
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.recordingPen import DecomposingRecordingPen
from data import ROOT,RESULTS,FONTS,font,frame,tensor,save

def digest(v):return hashlib.sha256(json.dumps(v,sort_keys=True).encode('utf-8')).hexdigest()
def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));fresh_closed=(RESULTS/'fresh-comparison.json').exists();assert fresh_closed or selection['selected']['epoch']==0;out=RESULTS/'glyph-geometry.json'
 if out.exists():raise RuntimeError('Fixed structural diagnostic cannot overwrite')
 protocol=json.loads(Path(__file__).with_name('protocol.json').read_text(encoding='utf-8'));names=list(dict.fromkeys(protocol['train']['kindFonts']['code']+protocol['development']['kindFonts']['code']+(protocol['fresh']['kindFonts']['code'] if fresh_closed else [])));contexts=['{}','x{}x','A{}7','j{}p','88{}','{}Qy'];sizes=[14,18,20,22,28];densities=[1,2,4];metrics={};records=[]
 for name in names:
  tt=TTFont(FONTS[name]);glyphs=tt.getGlyphSet();cm=tt.getBestCmap();data={}
  for ch in 'Il1O0':
   glyph=glyphs[cm[ord(ch)]];bounds=BoundsPen(glyphs);glyph.draw(bounds);pen=DecomposingRecordingPen(glyphs);glyph.draw(pen);left,bottom,_,_=bounds.bounds
   shifted=[]
   for op,args in pen.value:shifted.append([op,[None if point is None else [float(point[0]-left),float(point[1]-bottom)] for point in args]])
   data[ch]={'unitsPerEm':tt['head'].unitsPerEm,'advance':glyph.width,'bounds':list(bounds.bounds),'baselineOutlineCommandsSha256':digest(pen.value),'translatedTightOutlineCommandsSha256':digest(shifted)}
  metrics[name]={'fontSha256':hashlib.sha256(FONTS[name].read_bytes()).hexdigest(),'glyphs':data};tt.close()
  for context,size,density,chars in itertools.product(contexts,sizes,densities,['Il1','O0']):
   face=font(name,size*density);values=[context.format(ch) for ch in chars];ascent,descent=face.getmetrics();margin=8*density;width=math.ceil(max(face.getlength(v) for v in values))+margin*2;height=ascent+descent+margin*2;images=[]
   for value in values:
    image=Image.new('RGB',(width,height),'white');ImageDraw.Draw(image).text((margin,margin+ascent),value,font=face,fill='black',anchor='ls');framed,source=frame(image);x,valid=tensor(framed);images.append({'text':value,'advance':float(face.getlength(value)),'image':np.asarray(image),'input':x,'validWidth':valid,'sourceCrop':source['crop'],'framedBaseline':margin+ascent-source['crop'][1]})
   for a,b in itertools.combinations(images,2):
    same_shape=a['input'].shape==b['input'].shape;records.append({'font':name,'context':context,'size':size,'density':density,'a':a['text'],'b':b['text'],'advances':[a['advance'],b['advance']],'sameOriginalCanvasRaster':bool(np.array_equal(a['image'],b['image'])),'originalDifferingPixels':int(np.any(a['image']!=b['image'],axis=2).sum()),'sameNormalizedInput':bool(np.array_equal(a['input'],b['input'])),'normalizedDifferingPixels':int(np.any(a['input']!=b['input'],axis=0).sum()) if same_shape else None,'inputWidths':[a['input'].shape[-1],b['input'].shape[-1]],'validWidths':[a['validWidth'],b['validWidth']],'sourceCrops':[a['sourceCrop'],b['sourceCrop']],'framedBaselines':[a['framedBaseline'],b['framedBaseline']]})
 counts={}
 for name in names:
  rows=[r for r in records if r['font']==name];counts[name]={'pairs':len(rows),'originalRasterAliases':sum(r['sameOriginalCanvasRaster'] for r in rows),'normalizedAliases':sum(r['sameNormalizedInput'] for r in rows),'contextualNormalizedAliases':sum(r['sameNormalizedInput'] and r['context']!='{}' for r in rows),'normalizationIntroducedAliases':sum(r['sameNormalizedInput'] and not r['sameOriginalCanvasRaster'] for r in rows)}
 save(out,{'scope':'Consumed synthetic source diagnostic after frozen selection; no recognition/training/reselection/accuracy credit','freshOpened':fresh_closed,'fontMetrics':metrics,'counts':counts,'conditions':{'sizes':sizes,'densities':densities,'contexts':contexts,'source':'Fixed common baseline/canvas within each comparison; whole-line bbox+2 per sample; exact existing FP32 BGR48 sampler','outlineLimit':'Equal translated outline command hashes show equal recorded geometry up to translation. Different command hashes do not prove visually different shapes. Isolated glyph equality alone never proves wholecode indistinguishability.'},'rows':records,'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()});print(json.dumps(counts))
if __name__=='__main__':main()
