"""Bound the one owned worker, retain exact partial bytes, stdout-only transport."""
import argparse, base64, hashlib, json, os, signal, subprocess, sys, time, zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent
def file_sha(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        while True:
            b = f.read(65536)
            if not b: break
            h.update(b)
    return h.hexdigest()

def main():
    a = argparse.ArgumentParser(); a.add_argument('--owned', required=True, type=Path); args = a.parse_args()
    owned = args.owned.resolve()
    if not (owned/'.owned-easyocr-row-probe').is_file(): raise ValueError('Owned marker absent')
    raw = owned/'native-raw.log'; stderr = owned/'native-stderr.log'; start = time.monotonic()
    reason = None; peak = 0; samples = 0
    with raw.open('xb') as out, stderr.open('xb') as err:
        child = subprocess.Popen([sys.executable, str(ROOT/'worker.py'), '--owned', str(owned)], stdout=out, stderr=err, start_new_session=True)
        try:
            while child.poll() is None:
                try:
                    lines = Path('/proc/'+str(child.pid)+'/status').read_text().splitlines()
                    rss = next(int(x.split()[1])*1024 for x in lines if x.startswith('VmRSS:'))
                    peak = max(peak, rss); samples += 1
                except (FileNotFoundError, StopIteration): pass
                if time.monotonic()-start > 600: reason = 'walltime-600s'
                elif peak > 2*1024**3: reason = 'sampled-owned-worker-RSS-2GiB'
                elif raw.stat().st_size > 32*1024**2: reason = 'sampled-raw-32MiB'
                if reason:
                    os.killpg(child.pid, signal.SIGTERM)
                    try: child.wait(timeout=5)
                    except subprocess.TimeoutExpired: os.killpg(child.pid, signal.SIGKILL)
                    break
                time.sleep(.1)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()
    execution = {'exitCode':child.returncode,'stopReason':reason,'elapsedSeconds':time.monotonic()-start,
                 'sampledOwnedWorkerPeakRSSBytes':peak,'RSSsamples':samples,
                 'memoryScope':'sampled own single worker process RSS; dataloader workers0, not global RAM or device minimum',
                 'rawBytes':raw.stat().st_size,'rawSHA256':file_sha(raw),'stderrBytes':stderr.stat().st_size,'stderrSHA256':file_sha(stderr),
                 'recipeSHA256':file_sha(ROOT/'recipe.json'),'scope':'single170-row uniform EasyOCR only; partial bytes pinned on every termination'}
    (owned/'execution.json').write_text(json.dumps(execution, indent=2)+'\n'); print(json.dumps({'type':'execution',**execution}), flush=True)
    # Transport exact bytes rather than a lossy reserialized/Unicode console view.
    for path in [raw, stderr]:
        if path.stat().st_size > 40*1024**2:
            print(json.dumps({'type':'transport-unavailable','file':path.name,'sha256':file_sha(path),'bytes':path.stat().st_size}), flush=True)
            continue
        b = path.read_bytes(); encoded = base64.b64encode(zlib.compress(b, 9)).decode()
        print('EASYOCR_BYTES_BEGIN '+json.dumps({'file':path.name,'originalBytes':len(b),'originalSHA256':file_sha(path),'encoding':'zlib-base64'}), flush=True)
        for index, offset in enumerate(range(0, len(encoded), 24000)):
            print('EASYOCR_BYTES_CHUNK '+str(index)+' '+encoded[offset:offset+24000], flush=True)
        print('EASYOCR_BYTES_END '+path.name, flush=True)
    return 1 if child.returncode or reason else 0

if __name__ == '__main__': raise SystemExit(main())
