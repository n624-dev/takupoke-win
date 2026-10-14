"""Read-only reproduce pinned static font bytes with captured head timestamp."""
from io import BytesIO
import hashlib,json,fontTools
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from data import PARENT,RESULTS,save

def main():
 receipt=json.loads((PARENT/'assets/font-download-receipt.json').read_text(encoding='utf-8'));rows=[]
 for name,item in receipt['files'].items():
  final=PARENT/'assets'/(name+'.ttf');original=PARENT/'assets'/(name+'-original.ttf');assert hashlib.sha256(original.read_bytes()).hexdigest()==item['originalSha256'];pinned=TTFont(final,recalcTimestamp=False);face=TTFont(original,recalcTimestamp=False)
  if item['staticAxes']:face=instantiateVariableFont(face,item['staticAxes'],inplace=False)
  face.recalcTimestamp=False;face['head'].modified=pinned['head'].modified;buffer=BytesIO();face.save(buffer);same=hashlib.sha256(buffer.getvalue()).hexdigest()==item['sha256'];assert same
  rows.append({'font':name,'headModified':pinned['head'].modified,'reproducedStaticByteSha256':item['sha256'],'sameBytes':True,'axes':item['staticAxes']})
 save(RESULTS/'font-serialization-audit.json',{'fontToolsVersion':fontTools.__version__,'scope':'Read-only originalfont-to-static reproduction, no image/model call. TTFont default recalcTimestamp can change headmodified bytes; restore recorded modified timestamp with recalcTimestampFalse. All external font assets/receipts left unchanged.','fonts':rows})
 print(json.dumps({'reproducedFonts':len(rows)}))
if __name__=='__main__':main()
