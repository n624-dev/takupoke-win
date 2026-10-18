#!/usr/bin/env python3
"""Pinned English best recognizer on ordinal source PNGs only."""
import argparse
import base64
import csv
import hashlib
import io
import json
import os
from pathlib import Path
import resource
import subprocess
import time


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--freeze', type=Path, required=True)
    p.add_argument('--pixels', type=Path, required=True)
    p.add_argument('--assets', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    freeze = json.loads(args.freeze.read_text(encoding='utf-8'))
    if sha(args.pixels) != freeze['pixelsSHA'] or sha(Path(freeze['executable'])) != freeze['executableSHA']:
        raise ValueError('Input/runtime changed')
    if sha(args.assets/'eng.traineddata') != freeze['modelSHA']:
        raise ValueError('Pinned English model changed')
    packet = json.loads(args.pixels.read_text(encoding='utf-8'))
    ids = set(freeze['ordinals'])
    rows = [r for r in packet['rows'] if r['id'] in ids]
    if len(rows) != len(ids):
        raise ValueError('Fixed ordinals changed')
    env = {k:v for k,v in os.environ.items() if not any(s in k.upper() for s in ('TOKEN','SECRET','PASSWORD','CREDENTIAL'))}
    env['OMP_THREAD_LIMIT'] = '1'
    with (args.output/'observations.jsonl').open('x',encoding='utf-8',newline='\n') as output:
        for row in rows:
            data = base64.b64decode(row['pngBase64'],validate=True)
            if hashlib.sha256(data).hexdigest() != row['pngSha256']:
                raise ValueError('PNG identity changed')
            source = args.output/(row['id']+'.png')
            source.write_bytes(data)
            stem = args.output/row['id']
            start = time.monotonic()
            native = subprocess.run([freeze['executable'],str(source),str(stem),'--tessdata-dir',str(args.assets),
                                     '-l','eng','--oem','1','--psm','7',
                                     '-c','tessedit_create_txt=1','-c','tessedit_create_tsv=1'],env=env,
                                    capture_output=True,timeout=30)
            if native.returncode:
                raise RuntimeError('Native recognizer failed, no negative credit')
            raw = stem.with_suffix('.txt').read_bytes()
            text = raw.decode('utf-8')
            # Tesseract single-line TXT protocol emits one terminal LF. Keep
            # native internal/leading/trailing spaces, case and punctuation.
            if text.endswith('\n'):
                text = text[:-1]
            tsv = stem.with_suffix('.tsv').read_text(encoding='utf-8')
            words = [w for w in csv.DictReader(io.StringIO(tsv),delimiter='\t') if w['level']=='5']
            confidence = [float(w['conf']) for w in words]
            record = {'id':row['id'],'pngSHA':row['pngSha256'],'text':text,
                      'nativeTxt':raw.decode('utf-8'),'nativeTsv':tsv,
                      'minimumWordConfidence':min(confidence) if confidence else None,
                      'words':words,'nativeStderr':native.stderr.decode('utf-8'),
                      'seconds':time.monotonic()-start}
            output.write(json.dumps(record,ensure_ascii=False,separators=(',',':'))+'\n')
            output.flush()
            source.unlink();stem.with_suffix('.txt').unlink();stem.with_suffix('.tsv').unlink()
    print(json.dumps({'callsCompleted':len(rows),'peakChildRssBytes':resource.getrusage(resource.RUSAGE_CHILDREN).ru_maxrss*1024,
                      'scope':'Linux child peak; native word positions, not CTC evidence or full document qualification'}))


if __name__ == '__main__':
    main()
