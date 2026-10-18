#!/usr/bin/env python3
"""Separate immutable historical/current binaries; four consumed plus two new fonts."""
import argparse,hashlib,json,subprocess,sys,urllib.request,shutil,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
BASELINE='4ff7d2c4c894c8748d9c6c4182d15dde2e962904'
REVISION='295d98a7a0c17c68f1341eaeea354e7960ea70d3'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,x):p.write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def asset(p):return {'file':p.name,'bytes':p.stat().st_size,'sha256':sha(p)}
def download(url,path,size,digest):
 with urllib.request.urlopen(url,timeout=60) as response,path.open('xb') as output:
  count=0
  while block:=response.read(1024*1024):
   count+=len(block)
   if count>size:raise ValueError('Pinned artifact exceeded size')
   output.write(block)
 if path.stat().st_size!=size or sha(path)!=digest:raise ValueError('Pinned public artifact differs: '+path.name)

p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--dotnet',default='dotnet');a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
assets=out/'assets';assets.mkdir();fonts=out/'fonts';fonts.mkdir()
# Archive the actual prior source; the baseline never links repaired code.
archive=out/'historical-source.zip';subprocess.run(['git','archive','--format=zip','-o',str(archive),BASELINE],cwd=ROOT,check=True)
historical=out/'historical-source';historical.mkdir()
with zipfile.ZipFile(archive) as z:z.extractall(historical)
subprocess.run([a.dotnet,'build',str(historical/'tools/ocr-whole-model-comparison/Read.csproj'),'-c','Release','--nologo'],check=True)
baseline=historical/'tools/ocr-whole-model-comparison/bin/Release/net10.0/Takupoke.OcrComparison.dll'
subprocess.run([a.dotnet,'build',str(Path(__file__).with_name('Read.csproj')),'-c','Release','--nologo'],check=True)
candidate_reader=Path(__file__).parent/'bin/Release/net10.0'
subprocess.run([a.dotnet,str(baseline),'--bundle',str(out/'bundle.json')],check=True)
bundle=json.loads((out/'bundle.json').read_text());models=out/'models';models.mkdir();downloads=[]
if next(x for x in bundle['Artifacts'] if x['Path']=='ascii-rec.onnx')['Sha256']!='9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703':raise ValueError('Wrong historical baseline bundle')
for x in bundle['Artifacts']:
 download(x['Url'],models/x['Path'],x['Size'],x['Sha256']);downloads.append(x)
shutil.copyfile(models/'det.onnx',assets/'det.onnx')
download('https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_rec_onnx/resolve/ed152b8b495f84de93cda5709d768548a9127622/inference.onnx',assets/'rec.onnx',16534782,'da72dc72ca4dc220df0dfde68c1dedc31c58d3e76a25871122e5056227d50092')
shutil.copyfile(ROOT/'src/Takupoke.Win/Assets/Recovery/ocr-characters.json',assets/'alphabet.json')
font_specs=[
 ('BIZUDPGothic-Regular.ttf','bizudpgothic','BIZUDPGothic-Regular.ttf',4669688,'a8e73ddd5a208b2bef62c2846a87c173cd7aae101725044d9e723d361093d7db',4408,'e753d7155d53c747d037a445e584c8ecfca6dd79846db610417e282a736b28bc'),
 ('ZenMaruGothic-Regular.ttf','zenmarugothic','ZenMaruGothic-Regular.ttf',3832700,'d38657d92bbb343a1c6c4f9133025c7991efc52419444d16e31e3a97aaef2d9d',4402,'2a20cf7ce1909d8ee1e949095d340f7d7656705f7c810a2d6faf56800ad0cb3d'),
 ('SawarabiGothic-Regular.ttf','sawarabigothic','SawarabiGothic-Regular.ttf',1902848,'04f2f9111bb69cb8d3706c27b44879c2c0fac593a11edf5cfe8cec1efc92c789',4355,'5f247fad3e8a86b6c0779dff09277047c1ce2b0b7580a1093347a8af755352ba'),
 ('SawarabiMincho-Regular.ttf','sawarabimincho','SawarabiMincho-Regular.ttf',1084300,'86373c619710d1528aac3a4d47ce96834101bb38b057235b39e47b61ba143c6d',4355,'c254ae01c407f4a2bd0ad163e4c1a795d57ddfa8967c21b8a28c250b44d13963'),
 ('BIZUDPMincho-Regular.ttf','bizudpmincho','BIZUDPMincho-Regular.ttf',6156436,'02e07546c51503acb864004b663f1762f099d8fbe1af7d619b185568568101ef',4408,'ef0ed524b3bd012f37015e986809eed6f67905456ebfc1ebd7fcc2765282ee01'),
 ('MPLUS2-Variable.ttf','mplus2','MPLUS2%5Bwght%5D.ttf',4141232,'837dd9cc8ee812e541025f602362ae17d57a5942ab81bea5cc60242e8f6d3f66',4387,'04971e3fcee60b247395150d93b3616f6a0b092572332c96187b472976553abc')]
for name,family,remote,size,digest,license_size,license_sha in font_specs:
 url=f'https://raw.githubusercontent.com/google/fonts/{REVISION}/ofl/{family}/{remote}';download(url,fonts/name,size,digest);downloads.append(dict(file=name,url=url,bytes=size,sha256=digest))
 url=f'https://raw.githubusercontent.com/google/fonts/{REVISION}/ofl/{family}/OFL.txt';download(url,fonts/(family+'-OFL.txt'),license_size,license_sha);downloads.append(dict(file=family+'-OFL.txt',url=url,bytes=license_size,sha256=license_sha))
write(out/'download-receipt.json',downloads)
study=out/'study'
# The original generator and exclusions preserve every historical corpus byte.
subprocess.run([sys.executable,str(historical/'tools/ocr-whole-model-comparison/prepare.py'),'--fonts',str(fonts),'--models',str(assets),'--output',str(study),'--exclusions',str(historical/'tools/ocr-whole-model-comparison/exclusions.json')],check=True)
subprocess.run([sys.executable,str(Path(__file__).with_name('prepare.py')),'--fonts',str(fonts),'--models',str(assets),'--output',str(study),'--exclusions',str(Path(__file__).with_name('exclusions.json'))],check=True)
shutil.move(models,study/'models');m=json.loads((study/'inputs.json').read_text())
dictionary=study/'hybrid-dictionary.json';shutil.copyfile(ROOT/'src/Takupoke.Win/Assets/Recovery/ocr-characters-v6.json',dictionary);m['hybridDictionary']=asset(dictionary)
spec=json.loads((ROOT/'tools/ocr-whole-model-comparison/candidate.json').read_text());selected=ROOT/spec['file']
if selected.stat().st_size!=spec['bytes'] or sha(selected)!=spec['sha256']:raise ValueError('Fixed exploratory expert changed')
shutil.copyfile(selected,study/'candidate-rec.onnx');m['candidateRecognizer']=asset(study/'candidate-rec.onnx');write(study/'candidate-model-receipt.json',spec);write(study/'inputs.json',m)
subprocess.run([sys.executable,str(ROOT/'tools/ocr-physical-framing/prepare_pdf.py'),'--manifest',str(study/'inputs.json')],check=True)
subprocess.run([a.dotnet,'build',str(ROOT/'tools/ocr-physical-framing/windows-render/WindowsRender.csproj'),'-c','Release','--nologo'],check=True)
renderer=ROOT/'tools/ocr-physical-framing/windows-render/bin/Release/net10.0-windows10.0.26100.0/win-x64/WindowsRender.dll'
sources=[*(ROOT/'src/Takupoke.Core/Recovery').glob('*.cs'),*(ROOT/'src/Takupoke.Infrastructure/Recovery').glob('*.cs'),*Path(__file__).parent.glob('*.py'),*Path(__file__).parent.glob('*.cs'),Path(__file__).with_name('Read.csproj'),Path(__file__).with_name('protocol.json'),ROOT/'tools/ocr-whole-model-comparison/Inspect.cs',ROOT/'tools/ocr-physical-framing/prepare_pdf.py',ROOT/'tools/ocr-physical-framing/windows-render/WindowsRender.cs']
write(study/'freeze.json',dict(recipe='paired-native-acquisition-accuracy-v1',historicalBaselineCommit=BASELINE,historicalArchiveSHA256=sha(archive),inputSHA256=sha(study/'pdf-inputs.json'),oracleSHA256=sha(study/'oracle.json'),generationSHA256=sha(study/'generation-freeze.json'),sourceSHA256={str(q.relative_to(ROOT)):sha(q) for q in sources},rendererSHA256=sha(renderer),zeroErrorsRequired=False))
subprocess.run([a.dotnet,str(renderer),str(study/'pdf-inputs.json')],check=True)
expected=json.loads(Path(__file__).with_name('expected-consumed-native-pages.json').read_text())['pages'];native=json.loads((study/'native-inputs.json').read_text());verified=0
for doc in native['documents']:
 if doc['id'] not in expected:continue
 for page in doc['pages']:
  assert page['pixels']['sha256']==expected[doc['id']][str(page['page'])],'Historical native raster changed'
  verified+=1
assert verified==60
write(study/'consumed-raster-identity.json',dict(originalRun=38824053198,verifiedNativePages=verified,exactAllHistoricalHashes=True))
subprocess.run([sys.executable,str(Path(__file__).with_name('run.py')),'--study',str(study),'--dotnet',a.dotnet,'--baseline-reader',str(baseline.parent),'--candidate-reader',str(candidate_reader)],check=True)
subprocess.run([sys.executable,str(Path(__file__).with_name('score.py')),'--study',str(study),'--dotnet',a.dotnet],check=True)
print(json.dumps({'closed':True,'report':str(study/'report.json')}),flush=True)
