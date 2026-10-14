"""Owned native Windows measurement; literals are scored after worker exit."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def write(p, value):
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')


def emit(out, names):
    for name in names:
        path = out/name
        if not path.exists(): continue
        print(json.dumps({'record':name,'sha256':sha(path),'bytes':path.stat().st_size},ensure_ascii=False),flush=True)
        # Keep exact CRLF/LF and bounded individual native lines in Actions logs.
        for index,line in enumerate(path.read_bytes().decode('utf-8').splitlines(keepends=True)):
            print(json.dumps({'record':name,'lineIndex':index,'contents':line},ensure_ascii=False),flush=True)


def main():
    p = argparse.ArgumentParser(); p.add_argument('--output', required=True, type=Path); a = p.parse_args()
    a.output.mkdir(parents=True, exist_ok=False)
    source = Path(__file__).parent
    pixels = ROOT/'tools/vision-ocr-comparison/fictional-pixels.json'
    gold = ROOT/'tools/vision-ocr-comparison/oracle.json'
    subprocess.run(['dotnet','build',str(source/'Probe.csproj'),'-c','Release','--verbosity','quiet'], check=True, timeout=180)
    reader = a.output/'reader'; shutil.copytree(source/'bin/Release/net10.0-windows10.0.26100.0/win-x64', reader)
    binaries = {str(f.relative_to(reader)):sha(f) for f in reader.rglob('*') if f.is_file()}
    freeze = {'recipe':'windows-system-ocr-consumed-original-lines-v1', 'pixelsSha256':sha(pixels),
              'oracleSha256':sha(gold), 'readerFiles':binaries, 'sources':{f.name:sha(f) for f in [source/'Program.cs', source/'Probe.csproj', Path(__file__)]},
              'condition':'Unchanged original BGRA pixels, explicit installed ja-JP/en-US engines, no resize/routing/retry or invented native confidence',
              'qualification':False, 'modelActivation':False}
    write(a.output/'freeze.json', freeze)
    raw = a.output/'observations.jsonl'
    with (a.output/'worker-stdout.txt').open('w', encoding='utf-8') as stdout, (a.output/'worker-stderr.txt').open('w', encoding='utf-8') as stderr:
        child = subprocess.run(['dotnet',str(reader/'Probe.dll'),str(pixels),str(raw)], stdout=stdout, stderr=stderr, timeout=900)
    receipt = {'exitCode':child.returncode, 'rawSha256':sha(raw) if raw.exists() else None, 'rawBytes':raw.stat().st_size if raw.exists() else None}
    write(a.output/'raw-receipt.json', receipt)
    if child.returncode:
        emit(a.output, ['freeze.json','raw-receipt.json','observations.jsonl','worker-stdout.txt','worker-stderr.txt'])
        raise RuntimeError('Native engine unavailable/failed; precision remains unassessed')
    if any(sha(reader/n) != h for n,h in binaries.items()) or sha(gold) != freeze['oracleSha256']: raise ValueError('Frozen identity changed')
    records = [json.loads(l) for l in raw.read_text(encoding='utf-8').splitlines()]
    expected = {r['id']:r for r in json.loads(gold.read_text(encoding='utf-8'))}
    languages = sorted({r['language'] for r in records})
    if not languages or len(records) != len(languages)*192 or len({(r['id'],r['language']) for r in records}) != len(records): raise ValueError('Missing/duplicate calls')
    groups = {}
    for r in records:
        g = expected[r['id']]; key = r['language']+'/'+g['cohort']+'/'+g['kind']
        group = groups.setdefault(key, {'rows':0,'singleLineExact':0,'emptyBlank':0,'multipleLines':0})
        group['rows'] += 1; group['singleLineExact'] += r['singleLineText'] == g['literal']
        group['emptyBlank'] += g['kind'] == 'blank' and not r['fullText'] and not r['lines']
        group['multipleLines'] += len(r['lines']) > 1
    report = {**receipt,'groups':groups,'languages':languages,'qualification':False,'nativeConfidenceAvailable':False}
    write(a.output/'report.json', report)
    # Only fictional outputs; bounded native records retained in workflow log.
    emit(a.output, ['freeze.json','raw-receipt.json','observations.jsonl','worker-stdout.txt','worker-stderr.txt','report.json'])


if __name__ == '__main__': main()
