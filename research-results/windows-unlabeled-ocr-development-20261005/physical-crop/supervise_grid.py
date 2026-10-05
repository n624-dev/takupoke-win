"""One finite physical crop candidate. Pins are checked before any native call."""
import subprocess,time,os,json,hashlib,signal
from pathlib import Path
root=Path(__file__).resolve().parent
recipe=json.loads((root/'recipe-frozen.json').read_text())
for p in recipe['pins']:
 path=Path(p['path'])
 if path.stat().st_size!=p['bytes'] or hashlib.sha256(path.read_bytes()).hexdigest()!=p['sha256']:raise SystemExit('Frozen file changed: '+str(path))
command=[str(root.parent/'env/bin/python'),str(root/'run_grid.py')]
start=time.monotonic();peak=0;reason=None;samples=0
out=root/'native-raw.log';err=root/'native-stderr.log'
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
def sha(path):
 h=hashlib.sha256()
 with path.open('rb') as f:
  for chunk in iter(lambda:f.read(1024*1024),b''):h.update(chunk)
 return h.hexdigest()
with out.open('xb') as o,err.open('xb') as e:
 child=subprocess.Popen(command,stdout=o,stderr=e,start_new_session=True,cwd=root)
 try:
  while child.poll() is None:
   rss=tree_rss(child.pid);peak=max(peak,rss);samples+=1
   if rss>3*1024**3:reason='own process-tree sampled RSS exceeds 3GiB'
   if time.monotonic()-start>420:reason='one image/90-region arm wall time exceeds 420s'
   if out.stat().st_size>32*1024**2:reason='raw output exceeds 32MiB (polling soft cap)'
   if reason:
    os.killpg(child.pid,signal.SIGTERM)
    try:child.wait(timeout=5)
    except subprocess.TimeoutExpired:os.killpg(child.pid,signal.SIGKILL)
    break
   time.sleep(.1)
 finally:
  if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
  child.wait()
receipt={'recipeSHA256':sha(root/'recipe-frozen.json'),'command':command,'exitCode':child.returncode,'stopReason':reason,'elapsedSeconds':time.monotonic()-start,'sampledOwnedProcessTreePeakRSSBytes':peak,'RSSsamples':samples,'memoryScope':'sampled sum own process tree RSS; shared pages may double count; not device minimum or global RAM','rawBytes':out.stat().st_size,'rawSHA256':sha(out),'stderrBytes':err.stat().st_size,'stderrSHA256':sha(err),'inferenceScope':'one known development image, fixed physical-region candidate only; old frame arms not repeated'}
(root/'execution.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n');print(json.dumps(receipt))
