"""Run frozen finite recipe in one owned process group, preserve raw partial bytes."""
import subprocess,time,os,json,hashlib,sys,signal
from pathlib import Path
root=Path(__file__).resolve().parent
engine=sys.argv[1]
commands={
 'ndl':[str(root/'env/bin/python'),str(root/'run_ndl.py'),str(root)],
 'tess':[sys.executable,str(root/'run_tess.py'),str(root)],
 'paddle':['/tmp/takupoke-tools/dotnet/dotnet',str(root/'paddle-probe/bin/Debug/net10.0/Takupoke.Integration.Tests.dll'),str(root)],
}
if engine not in commands:raise SystemExit('unknown engine')
# Native worker sees only the PNG/pixels envelope; no literal/scoring oracle.
recipe=json.loads((root/'comparison-recipe-frozen.json').read_text())
for pin in recipe['pins']:
 p=Path(pin['path'])
 if hashlib.sha256(p.read_bytes()).hexdigest()!=pin['sha256']:raise SystemExit('Frozen file changed: '+str(p))
start=time.monotonic();peak=0;reason=None;samples=0
out=root/(engine+'-raw.log');err=root/(engine+'-stderr.log')
def tree_rss(pid):
 ids={pid};proc={}
 for path in Path('/proc').glob('[0-9]*/status'):
  try:
   text=path.read_text();parent=int(next(l.split()[1] for l in text.splitlines() if l.startswith('PPid:')));rss=next((int(l.split()[1])*1024 for l in text.splitlines() if l.startswith('VmRSS:')),0);proc[int(path.parent.name)]=(parent,rss)
  except (OSError,ValueError,StopIteration):continue
 for _ in range(64):
  added={p for p,(pp,r) in proc.items() if pp in ids}-ids
  if not added:break
  ids|=added
 return sum(proc[p][1] for p in ids if p in proc)
with out.open('xb') as o,err.open('xb') as e:
 child=subprocess.Popen(commands[engine],stdout=o,stderr=e,start_new_session=True,cwd=root)
 try:
  while child.poll() is None:
   rss=tree_rss(child.pid);peak=max(peak,rss);samples+=1
   if rss>3*1024**3:reason='owned process-tree sampled RSS limit 3GiB'
   if time.monotonic()-start>420:reason='whole two-image wall time 420s'
   if out.stat().st_size>32*1024**2:reason='raw log 32MiB limit'
   if reason:
    os.killpg(child.pid,signal.SIGTERM)
    try:child.wait(timeout=5)
    except subprocess.TimeoutExpired:os.killpg(child.pid,signal.SIGKILL)
    break
   time.sleep(.1)
 finally:
  if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
  child.wait()
receipt={'engine':engine,'command':commands[engine],'exitCode':child.returncode,'stopReason':reason,'elapsedSeconds':time.monotonic()-start,'sampledOwnedProcessTreePeakRSSBytes':peak,'RSSsamples':samples,'memoryScope':'sampled sum current own process-tree RSS, shared pages may be double counted; not global RAM or physical-device minimum','rawBytes':out.stat().st_size,'rawSHA256':hashlib.sha256(out.read_bytes()).hexdigest(),'stderrSHA256':hashlib.sha256(err.read_bytes()).hexdigest()}
(root/(engine+'-execution.json')).write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(receipt))
