"""Read-only decomposition of previously registered geometry aliases; no model inference."""
import hashlib,json,math
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
from data import RESULTS,font,frame,tensor,save

def main():
 original=RESULTS/'glyph-geometry.json';payload=json.loads(original.read_text(encoding='utf-8'));rows=[]
 for r in payload['rows']:
  if not r['sameNormalizedInput']:continue
  face=font(r['font'],r['size']*r['density']);margin=8*r['density'];values=[r['context'].format(c) for c in ('Il1' if any(c in r['a']+r['b'] for c in 'Il1') else 'O0')];ascent,descent=face.getmetrics();width=math.ceil(max(face.getlength(v) for v in values))+margin*2;height=ascent+descent+margin*2;images=[];framed=[];arrays=[]
  for text in [r['a'],r['b']]:
   im=Image.new('RGB',(width,height),'white');ImageDraw.Draw(im).text((margin,margin+ascent),text,font=face,fill='black',anchor='ls');images.append(np.asarray(im));crop,_=frame(im);framed.append(np.asarray(crop));x,_=tensor(crop);arrays.append(x)
  assert int(np.any(images[0]!=images[1],axis=2).sum())==r['originalDifferingPixels'];assert np.array_equal(arrays[0],arrays[1]);same=np.array_equal(framed[0],framed[1]);rows.append({**r,'sameFramedRasterBeforeResize':bool(same),'aliasIntroducedByBboxFraming':not r['sameOriginalCanvasRaster'] and bool(same),'aliasIntroducedByResizeAfterFraming':not same,'framedShapes':[list(v.shape) for v in framed]})
 result={'scope':'Reproduction/decomposition of the exact already-registered 17 aliases; no new condition/model/quality credit','geometrySha256':hashlib.sha256(original.read_bytes()).hexdigest(),'aliases':len(rows),'sourceRasterAliases':sum(r['sameOriginalCanvasRaster'] for r in rows),'aliasesIntroducedByBbox':sum(r['aliasIntroducedByBboxFraming'] for r in rows),'aliasesIntroducedByResizeAfterBbox':sum(r['aliasIntroducedByResizeAfterFraming'] for r in rows),'rows':rows,'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()};save(RESULTS/'glyph-framing-alias-audit.json',result);print(json.dumps({k:v for k,v in result.items() if k!='rows'}))
if __name__=='__main__':main()
