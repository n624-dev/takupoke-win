"""Pinned public fonts, no private source data. Download only, no final rendering."""
from pathlib import Path
import urllib.request,urllib.parse,json,hashlib
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
ROOT=Path('/workspace/scratch/ascii-english-v5-japanese-fonts-v3/assets');REV='94301472b4d809299b4775bc6502592b83dd6934'
items={'ZenOldMincho':'ofl/zenoldmincho/ZenOldMincho-Regular.ttf','YuseiMagic':'ofl/yuseimagic/YuseiMagic-Regular.ttf','ShipporiAntique':'ofl/shipporiantique/ShipporiAntique-Regular.ttf','ZenAntiqueSoft':'ofl/zenantiquesoft/ZenAntiqueSoft-Regular.ttf','SawarabiGothic':'ofl/sawarabigothic/SawarabiGothic-Regular.ttf','SawarabiMincho':'ofl/sawarabimincho/SawarabiMincho-Regular.ttf','KleeOne':'ofl/kleeone/KleeOne-Regular.ttf','KaiseiTokumin':'ofl/kaiseitokumin/KaiseiTokumin-Regular.ttf'}
receipt={'repository':'google/fonts','revision':REV,'files':{}}
for name,path in items.items():
 url='https://raw.githubusercontent.com/google/fonts/'+REV+'/'+urllib.parse.quote(path);payload=urllib.request.urlopen(url,timeout=45).read();original=ROOT/(name+'-original.ttf');original.write_bytes(payload);face=TTFont(original);axes={a.axisTag:(400 if a.axisTag=='wght' else a.defaultValue) for a in face['fvar'].axes} if 'fvar' in face else {};face=instantiateVariableFont(face,axes,inplace=False) if axes else face;final=ROOT/(name+'.ttf');face.save(final);license_url='https://raw.githubusercontent.com/google/fonts/'+REV+'/'+path.rsplit('/',1)[0]+'/OFL.txt';license_payload=urllib.request.urlopen(license_url,timeout=45).read();(ROOT/(name+'-OFL.txt')).write_bytes(license_payload);receipt['files'][name]={'url':url,'originalSha256':hashlib.sha256(payload).hexdigest(),'staticAxes':axes,'bytes':final.stat().st_size,'sha256':hashlib.sha256(final.read_bytes()).hexdigest(),'licenseUrl':license_url,'licenseSha256':hashlib.sha256(license_payload).hexdigest()};print(name,final.stat().st_size,flush=True)
(ROOT/'font-download-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8',newline='\n')
