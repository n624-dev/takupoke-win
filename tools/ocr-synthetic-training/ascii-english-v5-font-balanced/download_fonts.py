from pathlib import Path
import urllib.request,json,hashlib
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
ROOT=Path('/workspace/scratch/ascii-english-v5-font-balanced-20261014-v1/assets');REV='295d98a7a0c17c68f1341eaeea354e7960ea70d3'
items={'IBMPlexMono':'ofl/ibmplexmono/IBMPlexMono-Regular.ttf','Inconsolata':'ofl/inconsolata/Inconsolata[wdth,wght].ttf','FiraMono':'ofl/firamono/FiraMono-Regular.ttf','SpaceMono':'ofl/spacemono/SpaceMono-Regular.ttf','CrimsonText':'ofl/crimsontext/CrimsonText-Regular.ttf','LibreBaskerville':'ofl/librebaskerville/LibreBaskerville-Regular.ttf','Lora':'ofl/lora/Lora[wght].ttf','Alegreya':'ofl/alegreya/Alegreya[wght].ttf','Cousine':'apache/cousine/Cousine-Regular.ttf','UbuntuMono':'ufl/ubuntumono/UbuntuMono-Regular.ttf','PTSerif':'ofl/ptserif/PT_Serif-Web-Regular.ttf','LibreCaslonText':'ofl/librecaslontext/LibreCaslonText[wght].ttf'}
receipt={'repository':'google/fonts','revision':REV,'files':{}}
for name,path in items.items():
 url='https://raw.githubusercontent.com/google/fonts/'+REV+'/'+urllib.parse.quote(path)
 try:payload=urllib.request.urlopen(url,timeout=30).read()
 except Exception as e:
  print(json.dumps({'failed':name,'url':url,'error':str(e)}),flush=True);continue
 original=ROOT/(name+'-original.ttf');original.write_bytes(payload);face=TTFont(original)
 axes={a.axisTag:(400 if a.axisTag=='wght' else a.defaultValue) for a in face['fvar'].axes} if 'fvar' in face else {}
 if axes:face=instantiateVariableFont(face,axes,inplace=False)
 final=ROOT/(name+'.ttf');face.save(final)
 directory=path.rsplit('/',1)[0];license_name='UFL.txt' if directory.startswith('ufl/') else 'LICENSE.txt' if directory.startswith('apache/') else 'OFL.txt';license_url='https://raw.githubusercontent.com/google/fonts/'+REV+'/'+directory+'/'+license_name
 try:license_payload=urllib.request.urlopen(license_url,timeout=30).read()
 except Exception as e:print(json.dumps({'failedLicense':name,'error':str(e)}),flush=True);continue
 (ROOT/(name+'-'+license_name)).write_bytes(license_payload)
 receipt['files'][name]={'url':url,'originalBytes':len(payload),'originalSha256':hashlib.sha256(payload).hexdigest(),'staticAxes':axes,'bytes':final.stat().st_size,'sha256':hashlib.sha256(final.read_bytes()).hexdigest(),'licenseUrl':license_url,'licenseSha256':hashlib.sha256(license_payload).hexdigest()};print(json.dumps({'font':name,'bytes':final.stat().st_size}),flush=True)
(ROOT/'font-download-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8',newline='\n')
