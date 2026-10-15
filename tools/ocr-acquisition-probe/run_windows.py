#!/usr/bin/env python3
"""Consumed native acquisition diagnosis only. No default change or accuracy qualification."""
import argparse,hashlib,json,subprocess,sys,urllib.request,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
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
    if path.stat().st_size!=size or sha(path)!=digest:raise ValueError('Pinned public artifact differs')

p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--dotnet',default='dotnet');a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);assets=out/'assets';assets.mkdir();fonts=out/'fonts';fonts.mkdir()
subprocess.run([a.dotnet,'build',str(ROOT/'tools/ocr-whole-model-comparison/Read.csproj'),'-c','Release','--nologo'],check=True)
read=ROOT/'tools/ocr-whole-model-comparison/bin/Release/net10.0/Takupoke.OcrComparison.dll'
subprocess.run([a.dotnet,str(read),'--bundle',str(out/'bundle.json')],check=True)
bundle=json.loads((out/'bundle.json').read_text());models=out/'models';models.mkdir()
if next(v for v in bundle['Artifacts'] if v['Path']=='ascii-rec.onnx')['Sha256']!='9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703':raise ValueError('Historical v1 baseline required; changed defaults need a new comparison protocol')
downloads=[]
for item in bundle['Artifacts']:
    path=models/item['Path'];download(item['Url'],path,item['Size'],item['Sha256']);downloads.append(item)
shutil.copyfile(models/'det.onnx',assets/'det.onnx')
download('https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_rec_onnx/resolve/ed152b8b495f84de93cda5709d768548a9127622/inference.onnx',assets/'rec.onnx',16534782,'da72dc72ca4dc220df0dfde68c1dedc31c58d3e76a25871122e5056227d50092')
shutil.copyfile(ROOT/'src/Takupoke.Win/Assets/Recovery/ocr-characters.json',assets/'alphabet.json')
revision='295d98a7a0c17c68f1341eaeea354e7960ea70d3'
for name,source,size,digest in [
    ('BIZUDPGothic-Regular.ttf','ofl/bizudpgothic/BIZUDPGothic-Regular.ttf',4669688,'a8e73ddd5a208b2bef62c2846a87c173cd7aae101725044d9e723d361093d7db'),
    ('ZenMaruGothic-Regular.ttf','ofl/zenmarugothic/ZenMaruGothic-Regular.ttf',3832700,'d38657d92bbb343a1c6c4f9133025c7991efc52419444d16e31e3a97aaef2d9d'),
    ('SawarabiGothic-Regular.ttf','ofl/sawarabigothic/SawarabiGothic-Regular.ttf',1902848,'04f2f9111bb69cb8d3706c27b44879c2c0fac593a11edf5cfe8cec1efc92c789'),
    ('SawarabiMincho-Regular.ttf','ofl/sawarabimincho/SawarabiMincho-Regular.ttf',1084300,'86373c619710d1528aac3a4d47ce96834101bb38b057235b39e47b61ba143c6d')]:
    url='https://raw.githubusercontent.com/google/fonts/'+revision+'/'+source;download(url,fonts/name,size,digest);downloads.append({'Path':name,'Url':url,'Size':size,'Sha256':digest})
write(out/'download-receipt.json',downloads)
study=out/'study'
subprocess.run([sys.executable,str(ROOT/'tools/ocr-whole-model-comparison/prepare.py'),'--fonts',str(fonts),'--models',str(assets),'--output',str(study),'--exclusions',str(ROOT/'tools/ocr-whole-model-comparison/exclusions.json')],check=True)
shutil.move(models,study/'models')
m=json.loads((study/'inputs.json').read_text())
dictionary=study/'hybrid-dictionary.json';shutil.copyfile(ROOT/'src/Takupoke.Win/Assets/Recovery/ocr-characters-v6.json',dictionary);m['hybridDictionary']=asset(dictionary)
spec=json.loads((ROOT/'tools/ocr-whole-model-comparison/candidate.json').read_text())
candidate=ROOT/spec['file']
if candidate.stat().st_size!=spec['bytes'] or sha(candidate)!=spec['sha256']:raise ValueError('Selected candidate does not match frozen model')
target=study/'candidate-rec.onnx';shutil.copyfile(candidate,target);m['candidateRecognizer']=asset(target)
write(study/'candidate-model-receipt.json',spec)
write(study/'inputs.json',m)
subprocess.run([sys.executable,str(ROOT/'tools/ocr-physical-framing/prepare_pdf.py'),'--manifest',str(study/'inputs.json')],check=True)
pdf_manifest=json.loads((study/'pdf-inputs.json').read_text());pdf_manifest['sourceScope']='All four font/literal cohorts are consumed from original run38824053198; acquisition diagnosis only. Lossless PDF containers do not add extra independent samples. Both arms consume identical native Windows-rendered source pages.';write(study/'pdf-inputs.json',pdf_manifest)
subprocess.run([a.dotnet,'build',str(ROOT/'tools/ocr-physical-framing/windows-render/WindowsRender.csproj'),'-c','Release','--nologo'],check=True)
renderer=ROOT/'tools/ocr-physical-framing/windows-render/bin/Release/net10.0-windows10.0.26100.0/win-x64/WindowsRender.dll'
sources=[*[p for p in (ROOT/'src/Takupoke.Infrastructure/Recovery').glob('*.cs')],ROOT/'tools/ocr-whole-model-comparison/Read.cs',ROOT/'tools/ocr-whole-model-comparison/Inspect.cs',ROOT/'tools/ocr-whole-model-comparison/run.py',ROOT/'tools/ocr-whole-model-comparison/collect.py',Path(__file__),Path(__file__).with_name('Probe.cs'),Path(__file__).with_name('Probe.csproj'),Path(__file__).with_name('expected-native-pages.json'),ROOT/'tools/ocr-whole-model-comparison/prepare.py',ROOT/'tools/ocr-whole-model-comparison/score.py',ROOT/'tools/ocr-physical-framing/windows-render/WindowsRender.cs']
write(study/'freeze.json',{'recipe':'consumed-native-acquisition-diagnostic-v1','inputSHA256':sha(study/'pdf-inputs.json'),'oracleSHA256':sha(study/'oracle.json'),
 'generationSHA256':sha(study/'generation-freeze.json'),'sourceSHA256':{str(p.relative_to(ROOT)):sha(p) for p in sources},'readDllSHA256':sha(read),'rendererSHA256':sha(renderer),'zeroErrorsRequired':False})
subprocess.run([a.dotnet,str(renderer),str(study/'pdf-inputs.json')],check=True)
# Preserve all consumed positive pages and exact failure observations.
expected=json.loads(Path(__file__).with_name('expected-native-pages.json').read_text())
native=json.loads((study/'native-inputs.json').read_text())
for doc in native['documents']:
    if doc['id'] not in expected['pages']:continue
    for page in doc['pages']:
        assert page['pixels']['sha256']==expected['pages'][doc['id']][str(page['page'])], 'Native raster changed from original failed run'
subprocess.run([a.dotnet,'build',str(Path(__file__).with_name('Probe.csproj')),'-c','Release','--nologo'],check=True)
probe=Path(__file__).parent/'bin/Release/net10.0/Takupoke.OcrComparison.dll'
write(study/'probe-execution-freeze.json',{'inputSHA256':sha(study/'native-inputs.json'),'files':{str(p.relative_to(probe.parent)):asset(p) for p in probe.parent.rglob('*') if p.is_file()},'oracleReadByWorker':False})
subprocess.run([a.dotnet,str(probe),str(study/'native-inputs.json'),str(study/'models'),str(study/'acquisition-observations.jsonl')],check=True)
records=out/'diagnostic-records';records.mkdir()
for name in ['acquisition-observations.jsonl','probe-execution-freeze.json','native-render-receipt.json','native-inputs.json','generation-freeze.json','freeze.json']:
    shutil.copyfile(study/name,records/name)
for doc in native['documents']:
    if doc['id'] not in expected['pages']:continue
    for page in doc['pages']:shutil.copyfile(study/page['pixels']['file'],records/page['pixels']['file'])
write(records/'scope.json',{'scope':'All10consumed native pages from 2 wholly fictional positive documents, exactpreviousrun38824053198 rasterhashes; includes fullpage BGRA for diagnosis only, exclude frompublicsourcecuration. No private data, oracle-fed inference or productdefault activation.', 'files':{p.name:asset(p) for p in records.iterdir() if p.is_file()}})
print(json.dumps({'closed':True,'diagnosticOnly':True,'records':str(records)}),flush=True)
