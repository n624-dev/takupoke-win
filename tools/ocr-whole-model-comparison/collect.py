#!/usr/bin/env python3
"""Retain byte-exact closed synthetic records, never full pages or model weights."""
import argparse,hashlib,json,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser();p.add_argument('--run',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
out=a.output;out.mkdir(parents=True,exist_ok=False);files={}
for folder in [a.run,a.run/'study']:
    if not folder.exists():continue
    for source in sorted(folder.iterdir()):
        if not source.is_file() or source.suffix not in ('.json','.jsonl','.txt'):continue
        target=out/('study' if folder.name=='study' else 'run')/source.name;target.parent.mkdir(exist_ok=True);shutil.copyfile(source,target)
        data=target.read_bytes();assert data==source.read_bytes();files[str(target.relative_to(out))]={'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()}
freeze=a.run/'study/freeze.json'
debug=a.run/'study/final-0-positive-native-p1.bgra'
if debug.exists():
    target=out/'diagnostic-fictional-page.bgra';shutil.copyfile(debug,target)
    files[target.name]={'bytes':target.stat().st_size,'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),
                        'scope':'One wholly fictional native page for acquisition failure diagnosis; exclude from public source curation.'}
if freeze.exists():
    for relative,digest in json.loads(freeze.read_text(encoding='utf-8'))['sourceSHA256'].items():
        source=ROOT/relative;data=source.read_bytes();assert hashlib.sha256(data).hexdigest()==digest
        target=out/'sources'/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
        files[str(target.relative_to(out))]={'bytes':len(data),'sha256':digest}
(out/'curation-manifest.json').write_text(json.dumps({'scope':'Closed fictional outputs, hashes, metadata and frozen source snapshots only. Raw documents may contain source-bound crops of fictional pixels; no full-page raster, PDF, model weights, binaries or real school materials.','files':files},indent=2)+'\n',encoding='utf-8')
print(json.dumps({'curatedFiles':len(files),'output':str(out)}))
