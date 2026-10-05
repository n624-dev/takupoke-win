"""One hosted Windows transaction. Public prerequisites before fictional PDF assembly."""
from pathlib import Path
import argparse,base64,ctypes,hashlib,importlib.util,json,os,shutil,subprocess,sys,tempfile,threading,time,urllib.request
D=Path(__file__).resolve().parent; ROOT=D.parents[1]
MODEL={'url':'https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_det_onnx/resolve/e6f4fa85f00e168c862bc462aebca69eef9b3d3d/inference.onnx','bytes':4826518,'sha256':'a431985659dc921974177a95adcfbb90fd9e51989a5e04d70d0b75f597b6e61d'}
PDF_SHA='932c9a783cae67b52d878bb4b7f090d9993cea0eb88f1171b4fd4c01a31696f1'
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def check(v,m):
 if not v:raise ValueError(m)
def validate():
 f=D/'packet-freeze.json'; freeze=json.loads(f.read_text())
 for pin in freeze['pins']:
  p=ROOT/pin['path'];check(p.stat().st_size==pin['bytes'] and sha(p)==pin['sha256'],'source pin '+pin['path'])
 print(json.dumps({'sourceValidation':'PASS','pins':len(freeze['pins']),'packetSHA256':sha(f),'downloads':0,'modelSessions':0,'detectorCalls':0}))
 return sha(f)
def event_gate(packet):
 check(os.environ.get('GITHUB_EVENT_NAME')=='workflow_dispatch','actual requires manual exact dispatch')
 e=json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text());ins=e['inputs']
 check(ins.get('packet_sha256')==packet and ins.get('source_commit')==os.environ.get('GITHUB_SHA') and len(ins['source_commit'])==40,'exact root packet/source')
 check(os.environ.get('GITHUB_RUN_ATTEMPT')=='1','no retry')
 check(os.environ.get('GITHUB_REF')=='refs/heads/research/windows-original-pdf-roi-20261006','research ref only')
def write_capped(file,data,count,cap):
 check(count+len(data)<=cap,'pre-write owned file cap')
 file.write(data);return count+len(data)
def download(path):
 with urllib.request.urlopen(MODEL['url'],timeout=30) as response,path.open('xb') as f:
  n=0
  while b:=response.read(64*1024):n=write_capped(f,b,n,MODEL['bytes'])
 check(n==MODEL['bytes'] and sha(path)==MODEL['sha256'],'existing detector download identity')
def memory_bytes(proc):
 class Counters(ctypes.Structure):
  _fields_=[('cb',ctypes.c_ulong),('PageFaultCount',ctypes.c_ulong),('PeakWorkingSetSize',ctypes.c_size_t),('WorkingSetSize',ctypes.c_size_t),('QuotaPeakPagedPoolUsage',ctypes.c_size_t),('QuotaPagedPoolUsage',ctypes.c_size_t),('QuotaPeakNonPagedPoolUsage',ctypes.c_size_t),('QuotaNonPagedPoolUsage',ctypes.c_size_t),('PagefileUsage',ctypes.c_size_t),('PeakPagefileUsage',ctypes.c_size_t)]
 c=Counters();c.cb=ctypes.sizeof(c)
 ok=ctypes.windll.psapi.GetProcessMemoryInfo(ctypes.c_void_p(int(proc._handle)),ctypes.byref(c),c.cb)
 if not ok and proc.poll() is not None:return 0
 check(ok,'process memory inspection')
 return c.WorkingSetSize
def capture(cmd,out,err,env):
 start=time.monotonic();counts=[0,0];errors=[];peak=0;reason=None
 proc=subprocess.Popen(cmd,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env)
 def drain(pipe,path,slot,cap):
  try:
   with path.open('xb') as f:
    while b:=pipe.read(4096):counts[slot]=write_capped(f,b,counts[slot],cap)
  except BaseException as e:
   errors.append(type(e).__name__+':'+str(e));proc.kill()
  finally:pipe.close()
 threads=[threading.Thread(target=drain,args=(proc.stdout,out,0,4*1024**2)),threading.Thread(target=drain,args=(proc.stderr,err,1,256*1024))]
 try:
  for t in threads:t.start()
  while proc.poll() is None:
   rss=memory_bytes(proc);peak=max(peak,rss)
   if rss>1024**3:reason='RSS exceeded1GiB';proc.kill();break
   if time.monotonic()-start>180:reason='wall exceeded180s';proc.kill();break
   if shutil.disk_usage(out.parent).free<2*1024**3:reason='retained disk below2GiB';proc.kill();break
   time.sleep(.05)
 finally:
  if proc.poll() is None:proc.kill()
  proc.wait()
  for t in threads:
   if t.ident is not None:t.join(timeout=2)
  for p in (proc.stdout,proc.stderr):
   if not p.closed:p.close()
 check(not any(t.is_alive() for t in threads),'owned pipe drainage incomplete')
 return {'exitCode':proc.returncode,'watchdogReason':reason,'errors':errors,'wallSeconds':time.monotonic()-start,'sampledRSSBytes':peak,'stdoutBytes':counts[0],'stderrBytes':counts[1]}
def emit_closed(name,path):
 b=path.read_bytes();check(len(b)<=4*1024**2,'capture bound');parts=[b[i:i+3072] for i in range(0,len(b),3072)]
 print(json.dumps({'closedCapture':name,'bytes':len(b),'sha256':hashlib.sha256(b).hexdigest(),'parts':len(parts),'sourceCommit':os.environ.get('GITHUB_SHA'),'runID':os.environ.get('GITHUB_RUN_ID'),'runAttempt':os.environ.get('GITHUB_RUN_ATTEMPT')}))
 for i,p in enumerate(parts):print(json.dumps({'closedCapturePart':name,'index':i,'base64':base64.b64encode(p).decode()}))
 print(json.dumps({'closedCaptureEnd':name,'sha256':hashlib.sha256(b).hexdigest()}))
def assess(path):
 check(path.stat().st_size<=4*1024**2,'native output bound');rows=[json.loads(s) for s in path.read_text().splitlines()]
 types=[r['type'] for r in rows];check(types==['render','local-render','runtime']+['detector-start','detector-map','detector']*3+['result'],'complete exact stage inventory')
 check(rows[-1]['actualDetectorCalls']==3 and rows[-1]['recognizerCalls']==0 and rows[-1]['modelSessions']==1 and rows[-1]['retry'] is False,'one exact detector transaction')
 arms=['whole-product-render','roi-existing-product-raster','roi-original-pdf-native-render']
 for i,arm in enumerate(arms):
  start,maprow,decoded=rows[3+i*3:6+i*3]
  check(all(r['calls']==i+1 and r['arm']==arm for r in (start,maprow,decoded)),'arm identity')
  check(start['inputSHA256']==decoded['inputSHA256'] and maprow['outputSHA256']==decoded['outputSHA256'] and decoded['rawPreserved'] is True,'native tensor/map provenance')
  check(maprow['elements']==maprow['finite'] and maprow['nonfinite']==maprow['below']==0 and maprow['above']==maprow['normalized'],'finite bounded map')
  check(len(decoded['sourceBoxes'])==len(decoded['mappedWholeRasterBoxes'])==decoded['candidateCount']<=10000,'no partial box inventory')
 return {'status':'COMPLETE_MAX3_DETECTOR_ACQUISITION_DIAGNOSTIC_NO_QUALITY_CREDIT','detectorCalls':3,'recognizerCalls':0,'arms':[{'arm':r['arm'],'candidates':r['candidateCount'],'boundaryCandidates':r['boundaryTouchingCandidates'],'spatialCoverage':r['spatialCoverage'],'sourceClosure':r['unchangedSourceClosure']} for r in rows if r['type']=='detector'],'acquisitionAndRoleQuality':'UNASSESSED','previous4060LinuxOrTileResultsPooled':False,'qualifiedModels':[]}
def actual(packet):
 recipe=json.loads((D/'recipe.json').read_text());check(sha(ROOT/'src/Takupoke.Win/Platform/WindowsPdfRecovery.cs')==recipe['productionRendererSHA256'],'production renderer source changed')
 check(sys.platform=='win32','Windows native only');event_gate(packet)
 check(shutil.disk_usage(tempfile.gettempdir()).free>=3*1024**3,'host public prerequisite/owned output reserve')
 owned=Path(tempfile.mkdtemp(prefix='takupoke-original-pdf-roi-'));receipt={};status=1
 try:
  # This isolated csproj resolves the pinned WindowsNuGet backend without Foundry/recognizer.
  build=subprocess.run(['dotnet','build',str(D/'native/RoiProbe.csproj'),'-c','Release','--no-restore'],timeout=180)
  check(build.returncode==0,'frozen prerequisite build failed')
  controls=subprocess.run(['dotnet',str(D/'native/bin/Release/net10.0-windows10.0.26100.0/win-x64/RoiProbe.dll'),'--controls'],timeout=30)
  check(controls.returncode==0,'model-free native geometry controls')
  model=owned/'det.onnx';download(model)
  recipe=json.loads((D/'recipe.json').read_text()); fixture=ROOT/'tools/raster-acquisition-native-probe/fixtures/ExamReturn-clean'
  images=[]
  for pin in recipe['fictionalImagePins']:
   p=ROOT/pin['path'];check(p.stat().st_size==pin['bytes'] and sha(p)==pin['sha256'],'fictional image identity');images.append(p)
  assembler=ROOT/'tools/raster-acquisition-native-probe/fixtures/assemble.py';check(sha(assembler)==recipe['assemblerSHA256'],'existing image-only assembly source')
  spec=importlib.util.spec_from_file_location('fixed_assembler',assembler);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
  pdf=owned/'fictional.pdf';m.assemble(pdf,images);check(pdf.stat().st_size<=2_000_000 and sha(pdf)==PDF_SHA,'same existing full fictional PDF')
  env=os.environ.copy();env.update(TAKUPOKE_ROI_GO='ONE_FIXED_WINDOWS_PDF_MAX3_DETECTOR_CALLS',DOTNET_EnableDiagnostics='0',DOTNET_DbgEnableMiniDump='0',ORT_TELEMETRY_DISABLED='1')
  receipt=capture(['dotnet',str(D/'native/bin/Release/net10.0-windows10.0.26100.0/win-x64/RoiProbe.dll'),'--one-fixed-page',str(pdf),str(model),PDF_SHA],owned/'native.jsonl',owned/'native.stderr',env)
  emit_closed('native.jsonl',owned/'native.jsonl');emit_closed('native.stderr',owned/'native.stderr')
  check(receipt['exitCode']==0 and receipt['watchdogReason'] is None and not receipt['errors'],'native operational failure; no partial credit')
  report=assess(owned/'native.jsonl');print(json.dumps(report));status=0
 finally:
  shutil.rmtree(owned)
  print(json.dumps({'executionReceipt':receipt,'ownedScratchRemoved':not owned.exists(),'packetSHA256':packet,'sourceCommit':os.environ.get('GITHUB_SHA'),'downloadsNewModelIdentities':0,'activation':False}))
 return status
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--validate-only',action='store_true');a=p.parse_args();packet=validate();raise SystemExit(0 if a.validate_only else actual(packet))
