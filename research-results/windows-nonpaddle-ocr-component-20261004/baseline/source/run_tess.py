import subprocess,json,time,hashlib,sys
from pathlib import Path
root=Path(sys.argv[1]).resolve();items=json.loads((root/'native-inputs.json').read_text())['images'];out=root/'owned-output';out.mkdir(exist_ok=True)
def emit(v):print(json.dumps(v,ensure_ascii=False),flush=True)
emit({'type':'runtime','engine':'tesseract','version':subprocess.check_output(['/usr/bin/tesseract','--version'],stderr=subprocess.STDOUT).decode(),'engineSHA256':hashlib.sha256(Path('/usr/bin/tesseract').read_bytes()).hexdigest(),'language':'jpn','OEM':1,'PSM':11,'hocr_char_boxes':True,'confidenceScope':'native word/candidate scope, not calibrated against other engines','coldLoadEachImage':True})
for item in items:
 p=Path(item['pngPath'])
 if hashlib.sha256(p.read_bytes()).hexdigest()!=item['pngSHA256']:raise RuntimeError('Frozen PNG changed')
 base=out/('tess-'+item['id']);start=time.monotonic()
 command=['/usr/bin/tesseract',str(p),str(base),'--tessdata-dir',str(root/'tess-models'),'-l','jpn','--oem','1','--psm','11','-c','hocr_char_boxes=1','-c','tessedit_create_hocr=1','-c','hocr_font_info=0','-c','tessedit_create_tsv=1','-c','tessedit_create_boxfile=1']
 # One engine invocation per PNG; three renderers consume that same output.
 result=subprocess.run(command,capture_output=True)
 emit({'type':'result','id':item['id'],'returned':result.returncode==0,'exitCode':result.returncode,'milliseconds':(time.monotonic()-start)*1000,'command':command,'stderr':result.stderr.decode('utf-8',errors='replace'),'outputs':{suffix:base.with_suffix('.'+suffix).read_text(encoding='utf-8') if base.with_suffix('.'+suffix).exists() else None for suffix in ['hocr','tsv','box']}})
