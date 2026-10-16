#!/usr/bin/env python3
"""New two-font native trial; separate historical/current binaries, disclosed old source-only regression."""
import argparse,hashlib,json,subprocess,sys,urllib.request,shutil,zipfile,os
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
subprocess.run([a.dotnet,str(candidate_reader/'Takupoke.OcrComparison.dll'),'--bundle',str(out/'candidate-bundle.json')],check=True)
current_bundle=json.loads((out/'candidate-bundle.json').read_text())
if current_bundle['Version']!='20261014-v1' or len(current_bundle['Artifacts'])!=4:raise ValueError('This closed comparison must run at its recorded pre-activation source; do not compare changed defaults against themselves')
subprocess.run([a.dotnet,str(baseline),'--bundle',str(out/'bundle.json')],check=True)
bundle=json.loads((out/'bundle.json').read_text());models=out/'models';models.mkdir();downloads=[]
if next(x for x in bundle['Artifacts'] if x['Path']=='ascii-rec.onnx')['Sha256']!='9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703':raise ValueError('Wrong historical baseline bundle')
for x in bundle['Artifacts']:
 download(x['Url'],models/x['Path'],x['Size'],x['Sha256']);downloads.append(x)
for spec in json.loads(Path(__file__).with_name('protocol.json').read_text())['fontPins']:
 download(spec['url'],fonts/spec['file'],spec['bytes'],spec['sha256']);downloads.append(spec)
write(out/'download-receipt.json',downloads)
study=out/'study'
# Only the new six-document cohort is inferred here. Old six-font outputs
# are separately disclosed, closed source-only regression; never new credit.
subprocess.run([sys.executable,str(Path(__file__).with_name('prepare.py')),'--fonts',str(fonts),'--models',str(models),'--output',str(study),'--exclusions',str(Path(__file__).with_name('exclusions.json'))],check=True)
shutil.copyfile(Path(__file__).with_name('consumed-regression.json'),study/'consumed-regression.json')
shutil.move(models,study/'models');m=json.loads((study/'inputs.json').read_text())
dictionary=study/'hybrid-dictionary.json';shutil.copyfile(ROOT/'src/Takupoke.Win/Assets/Recovery/ocr-characters-v6.json',dictionary);m['hybridDictionary']=asset(dictionary)
spec=json.loads((ROOT/'tools/ocr-whole-model-comparison/candidate.json').read_text());selected=ROOT/spec['file']
if selected.stat().st_size!=spec['bytes'] or sha(selected)!=spec['sha256']:raise ValueError('Fixed exploratory expert changed')
shutil.copyfile(selected,study/'candidate-rec.onnx');m['candidateRecognizer']=asset(study/'candidate-rec.onnx');write(study/'candidate-model-receipt.json',spec);write(study/'inputs.json',m)
subprocess.run([sys.executable,str(ROOT/'tools/ocr-physical-framing/prepare_pdf.py'),'--manifest',str(study/'inputs.json')],check=True)
subprocess.run([a.dotnet,'build',str(ROOT/'tools/ocr-physical-framing/windows-render/WindowsRender.csproj'),'-c','Release','--nologo'],check=True)
renderer=ROOT/'tools/ocr-physical-framing/windows-render/bin/Release/net10.0-windows10.0.26100.0/win-x64/WindowsRender.dll'
sources=[*(ROOT/'src/Takupoke.Core/Recovery').glob('*.cs'),*(ROOT/'src/Takupoke.Infrastructure/Recovery').glob('*.cs'),*Path(__file__).parent.glob('*.py'),*Path(__file__).parent.glob('*.cs'),*Path(__file__).parent.glob('*.json'),Path(__file__).with_name('Read.csproj'),Path(__file__).with_name('Inspect.csproj'),ROOT/'tools/ocr-whole-model-comparison/Inspect.cs',ROOT/'tools/ocr-physical-framing/prepare_pdf.py',ROOT/'tools/ocr-physical-framing/windows-render/WindowsRender.cs']
write(study/'freeze.json',dict(recipe='source-order-native-fresh-v1',sourceCommit=os.environ.get('GITHUB_SHA'),protocolSHA256=sha(Path(__file__).with_name('protocol.json')),historicalBaselineCommit=BASELINE,historicalArchiveSHA256=sha(archive),inputSHA256=sha(study/'pdf-inputs.json'),oracleSHA256=sha(study/'oracle.json'),generationSHA256=sha(study/'generation-freeze.json'),sourceSHA256={str(q.relative_to(ROOT)):sha(q) for q in sources},rendererSHA256=sha(renderer),zeroErrorsRequired=False))
subprocess.run([a.dotnet,str(renderer),str(study/'pdf-inputs.json')],check=True)
subprocess.run([sys.executable,str(Path(__file__).with_name('run.py')),'--study',str(study),'--dotnet',a.dotnet,'--baseline-reader',str(baseline.parent),'--candidate-reader',str(candidate_reader)],check=True)
subprocess.run([sys.executable,str(Path(__file__).with_name('score.py')),'--study',str(study),'--dotnet',a.dotnet],check=True)
print(json.dumps({'closed':True,'report':str(study/'report.json')}),flush=True)
