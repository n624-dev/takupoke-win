#!/usr/bin/env python3
"""Download only pinned public GOT-OCR artifacts before any pixel processing."""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request

REPO='stepfun-ai/GOT-OCR-2.0-hf'
REV='d3017ef2c2c1395888c8d635c5e0508bcb0ac78d'


def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);args=p.parse_args()
    args.output.mkdir(parents=True,exist_ok=False)
    with urllib.request.urlopen(f'https://huggingface.co/api/models/{REPO}/tree/{REV}',timeout=30) as r:entries=json.load(r)
    receipt={'repository':REPO,'revision':REV,'files':[],'source':'Public model assets only; no PDF/PNG/OCR/prompt transmitted'}
    names={'README.md','config.json','generation_config.json','model.safetensors','preprocessor_config.json',
           'special_tokens_map.json','tokenizer.json','tokenizer_config.json'}
    for e in entries:
        if e['path'] not in names:continue
        path=args.output/e['path'];digest=hashlib.sha256();blob=hashlib.sha1(b'blob '+str(e['size']).encode()+b'\0');size=0
        with urllib.request.urlopen(f'https://huggingface.co/{REPO}/resolve/{REV}/{e["path"]}',timeout=120) as r,path.open('xb') as f:
            while chunk:=r.read(1024*1024):
                size+=len(chunk)
                if size>e['size']:raise ValueError('Asset exceeds pinned size')
                f.write(chunk);digest.update(chunk);blob.update(chunk)
        expected=e.get('lfs',{}).get('oid') if e.get('lfs') else e['oid']
        measured=digest.hexdigest() if e.get('lfs') else blob.hexdigest()
        if size!=e['size'] or measured!=expected:raise ValueError('Public immutable asset identity failed')
        receipt['files'].append({'file':e['path'],'bytes':size,'sha256':digest.hexdigest()})
        print(json.dumps(receipt['files'][-1]),flush=True)
    if {e['file'] for e in receipt['files']}!=names:raise ValueError('Incomplete assets')
    (args.output/'download-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8',newline='\n')


if __name__=='__main__':main()
