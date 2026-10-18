"""Public pinned mono train candidates/new dev fonts; no final-font access."""
import hashlib
import json
from pathlib import Path
import urllib.parse
import urllib.request

from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

REVISION='295d98a7a0c17c68f1341eaeea354e7960ea70d3'
ROOT=Path('/workspace/scratch/ascii-paddle-medium-height-neck-20261013-v1/assets')
SPECS=[('MPLUS1Code-Regular','ofl/mplus1code','MPLUS1Code[wght].ttf','OFL.txt','SIL-OFL-1.1',True),('BIZUDMincho-Regular','ofl/bizudmincho','BIZUDMincho-Regular.ttf','OFL.txt','SIL-OFL-1.1',False),('KosugiMaru-Regular','apache/kosugimaru','KosugiMaru-Regular.ttf','LICENSE.txt','Apache-2.0',False),('ZenAntique-Regular','ofl/zenantique','ZenAntique-Regular.ttf','OFL.txt','SIL-OFL-1.1',False)]


def fetch(path):
 url='https://raw.githubusercontent.com/google/fonts/'+REVISION+'/'+urllib.parse.quote(path,safe='/')
 with urllib.request.urlopen(url,timeout=40) as response:payload=response.read()
 if not payload or len(payload)>32*1024**2:raise ValueError('Publicfont unexpected size')
 return url,payload


def main():
 ROOT.mkdir(parents=True,exist_ok=True)
 if (ROOT/'font-receipt.json').exists():raise ValueError('Pinned assets already downloaded')
 rows=[]
 for name,directory,file,license_file,license_name,variable in SPECS:
  url,payload=fetch(directory+'/'+file);original=ROOT/file;original.write_bytes(payload);license_url,license_payload=fetch(directory+'/'+license_file);assert ('SIL OPEN FONT LICENSE' if license_name=='SIL-OFL-1.1' else 'Apache License') in license_payload.decode('utf-8');license_path=ROOT/(name+'-'+license_file);license_path.write_bytes(license_payload);path=ROOT/(name+'.ttf')
  if variable:
   font=TTFont(original);axes={axis.axisTag:(axis.minValue,axis.defaultValue,axis.maxValue) for axis in font['fvar'].axes};assert set(axes)=={'wght'};font=instantiateVariableFont(font,{'wght':400},inplace=False);font.save(path)
  else:
   axes={};path.write_bytes(payload)
  font=TTFont(path);glyphs=font.getGlyphSet();cmap=font.getBestCmap();assert all(i in cmap for i in range(32,127));widths={glyphs[cmap[i]].width for i in range(32,127)};rows.append({'name':name,'sourceUrl':url,'revision':REVISION,'sourceBytes':len(payload),'sourceSha256':hashlib.sha256(payload).hexdigest(),'staticPath':str(path),'staticSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'instancedAxes':{'wght':400} if variable else None,'sourceAxes':axes,'asciiAdvanceDistinctValues':len(widths),'license':license_name,'licenseUrl':license_url,'licenseSha256':hashlib.sha256(license_payload).hexdigest(),'purpose':'Prospective independent monospace training coverage' if name.startswith(('MPLUS','BIZUD')) else 'Prospective newly reserved development font, pending parent exposure coordination'})
 receipt={'scope':'Public pinned font/source/license receipt only; no rendered pixels/model inference/training/final','fonts':rows,'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()};(ROOT/'font-receipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(rows))


if __name__=='__main__':main()
