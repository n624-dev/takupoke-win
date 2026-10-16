"""License/font/hash preflight only; never render heldout fonts or call model."""
import json
from pathlib import Path
from fontTools.ttLib import TTFont
from data import FONTS,V1,V2,V3,PARENT,ROOT,RESULTS,PRINTABLE,ALNUM,sha,save
LICENSES={n:V1/(n+'-OFL.txt') for n in ['IBMPlexMono','Inconsolata','CrimsonText','Alegreya','PTSerif']};LICENSES['Cousine']=V1/'Cousine-LICENSE.txt';LICENSES.update({n:V2/(n+'-OFL.txt') for n in ['RedHatMono','Literata','SplineSansMono','MartianMono','Newsreader','Fraunces']});LICENSES.update({n:V3/(n+'-OFL.txt') for n in ['SawarabiGothic','SawarabiMincho','KleeOne','KaiseiTokumin','ZenOldMincho','YuseiMagic','ShipporiAntique','ZenAntiqueSoft']});LICENSES.update({n:PARENT/'assets'/(n+'-OFL.txt') for n in ['Murecho','NewTegomin']});LICENSES.update({'KosugiMaru':Path('/workspace/scratch/ascii-paddle-medium-height-neck-20261013-v1/assets/KosugiMaru-Regular-LICENSE.txt'),'KiwiMaru':Path('/workspace/scratch/ascii-paddle-transfer-20261013-v1/assets/KiwiMaru-OFL.txt'),'HinaMincho':Path('/workspace/scratch/ascii-paddle-transfer-20261013-v1/assets/HinaMincho-OFL.txt'),'ShipporiMincho':Path('/workspace/scratch/physical-frame-fresh-fonts/shipporimincho-OFL.txt'),'NotoSansJP400':Path('/workspace/scratch/ocr-finetune-20261013/licenses/notosansjp-OFL.txt'),'NotoSerifJP400':Path('/workspace/scratch/ocr-finetune-20261013/licenses/notoserifjp-OFL.txt')})
def main():
 protocol=json.loads(Path(__file__).with_name('protocol.json').read_text(encoding='utf-8'));latin={n for split in ['train','development','fresh'] for n in protocol[split]['latinFonts']};fonts={}
 for name,path in FONTS.items():
  assert name not in protocol['reservedRootFonts'];assert path.exists() and LICENSES[name].exists();license=LICENSES[name].read_text(encoding='utf-8');assert 'OPEN FONT LICENSE' in license.upper() or 'Apache' in license or 'APACHE' in license
  with TTFont(path) as face:chars=set(face.getBestCmap());required=PRINTABLE if name in latin else ALNUM+'_- ';assert all(ord(c) in chars for c in required)
  fonts[name]={'file':str(path),'sha256':sha(path),'bytes':path.stat().st_size,'license':str(LICENSES[name]),'licenseSha256':sha(LICENSES[name]),'asciiCmapPassed':True,'renderedOrInferredByThisPreflight':False}
 save(PARENT/'assets/readiness.json',{'fonts':fonts,'all28FontsVerified':len(fonts)==28,'newFontReceipt':json.loads((PARENT/'assets/font-download-receipt.json').read_text(encoding='utf-8')),'freshRendered':False,'modelCalled':False});print(json.dumps({'fonts':len(fonts),'newDev':['Murecho','NewTegomin'],'freshRendered':False,'modelCalled':False}))
if __name__=='__main__':main()
