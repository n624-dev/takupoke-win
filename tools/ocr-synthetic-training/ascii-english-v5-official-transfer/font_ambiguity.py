"""Postselection font/anchored-line information audit, zero model inference."""
import hashlib,json
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from fontTools.ttLib import TTFont
from fontTools.pens.recordingPen import DecomposingRecordingPen
from data import FONTS,ROOT,RESULTS,frame,tensor,save
PAIRS=[('I','l'),('I','1'),('l','1'),('O','0')]
def main():
 assert (ROOT/'selection.json').exists();rows=[]
 for name,path in FONTS.items():
  ft=TTFont(path);cmap=ft.getBestCmap();glyphs=ft.getGlyphSet();record={}
  for ch in 'Il1O0':
   glyph=glyphs[cmap[ord(ch)]];pen=DecomposingRecordingPen(glyphs);glyph.draw(pen);record[ch]=(glyph.width,pen.value)
  pairs=[]
  for a,b in PAIRS:
   sizes=[]
   for size in [14,18,24,28,48,72,96]:
    face=ImageFont.truetype(str(path),size);ascent,descent=face.getmetrics();images=[];arrays=[]
    for ch in [a,b]:
     value='H_'+ch+'_g';im=Image.new('RGB',(int(max(face.getlength('H_'+a+'_g'),face.getlength('H_'+b+'_g')))+20,ascent+descent+20),'white');ImageDraw.Draw(im).text((10,10+ascent),value,font=face,fill='black',anchor='ls');images.append(np.asarray(im));crop,source=frame(im)
     try:x,valid=tensor(crop);arrays.append((x,valid))
     except ValueError:arrays.append(None)
    sameinput=arrays[0] is not None and arrays[1] is not None and arrays[0][1]==arrays[1][1] and arrays[0][0].shape==arrays[1][0].shape and arrays[0][0].tobytes()==arrays[1][0].tobytes();sizes.append({'fontSize':size,'wholeAnchoredRgbIdentical':images[0].shape==images[1].shape and images[0].tobytes()==images[1].tobytes(),'framedFp32InputIdentical':sameinput})
   pairs.append({'characters':[a,b],'decomposedOutlineAndAdvanceIdentical':record[a]==record[b],'sizes':sizes})
  rows.append({'font':name,'fontSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'pairs':pairs})
 collisions={}
 for split in ['train','development']:
  m=json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'));c=json.loads((ROOT/(split+'-cache')/'cache.json').read_text(encoding='utf-8'));groups={}
  for row,item in zip(m['rows'],c['rows']):
   if row['selectionPositive']:groups.setdefault(item['inputSha256'],[]).append(row)
  collisions[split]=[{'inputSha256':h,'ids':[r['id'] for r in g],'differentTargets':sorted({r['text'] for r in g})} for h,g in groups.items() if len({r['text'] for r in g})>1]
 save(RESULTS/'font-information-audit.json',{'scope':'Postselection zero-call font/source information diagnosis; consumedtrain/devfontfiles only, no final/fontselection/training/modelrepair','anchor':'H_<character>_g commonfont/sharedbaseline/fullcanvas before wholeink2px frame; preservescapheight/descender/internalgap. Not isolatedtighttrimglyph comparison.','fonts':rows,'actualCorpusExactFp32DifferentPositiveTargetCollisions':collisions,'limits':'Outline/pixel nonidentity is not proof of reliable separability. Exact identical pixels cannot be disambiguated by learning or font-prior guess. No gate/selection change.'});print(json.dumps({r['font']:{''.join(p['characters']):{'outlineSame':p['decomposedOutlineAndAdvanceIdentical'],'sameInputSizes':[v['fontSize'] for v in p['sizes'] if v['framedFp32InputIdentical']]} for p in r['pairs']} for r in rows}))
if __name__=='__main__':main()
