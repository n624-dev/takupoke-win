"""Freeze old fictional DEVELOPMENT pixels/primary observations, never new-image credit."""
import argparse
import gzip
import hashlib
import json
import os
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRATCH = ROOT.parent
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p, value): p.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n')
def asset(p, root): return dict(file=str(p.relative_to(root)), bytes=p.stat().st_size, sha256=sha(p))

def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--kind',choices=['native','standalone','prefix'],required=True)
    a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
    manifest={}
    files=dict(v1=SCRATCH/'ocr-manual-tail-20261016-v1/deployed-trained-english-v5.onnx',v2=ROOT/'model-artifacts/ascii-english-v5-confusion-v2/model.onnx',dictionary=ROOT/'model-artifacts/ascii-english-v5-font-balanced-v1/dictionary.json',japanese=SCRATCH/'ocr-manual-tail-20261016-v1/japanese-v6.onnx',japaneseDictionary=SCRATCH/'ocr-manual-tail-20261016-v1/japanese-v6.json')
    for key,src in files.items():
        q=out/(key+src.suffix);os.link(src,q);manifest[key]=asset(q,out)
    rows=[];gold=[]
    if a.kind=='native':
        records=ROOT/'research-results/ocr-manual-tail-20261016/native-windows/study'
        raw=json.loads(gzip.decompress((records/'hybrid-observations.jsonl.gz').read_bytes()).splitlines()[0])
        inspection=json.loads(gzip.decompress((records/'hybrid-inspection.jsonl.gz').read_bytes()).splitlines()[0])
        oracle=json.loads((records/'oracle.json').read_text())[0]
        page=raw['pageReports'][0];pixels=SCRATCH/'ocr-native-tail-20261016-v1/diagnostic-fictional-page.bgra'
        assert sha(pixels)==raw['document']['Capture']['Pages'][0]['RasterHash']
        q=out/'native-first-page.bgra';os.link(pixels,q)
        sources={s['Id']:s for s in raw['document']['Sources']}
        expected={(l['className'],str(l['weekday']),l['period'],role):v for l in oracle['timetable']['lessons'] for role,v in l['names'].items()}
        fields={}
        for f in inspection['fields']:
            if sources[f['Evidence'][0]]['Page']!=1:continue
            lineids={sources[e]['SourceLine'] for e in f['Evidence']};assert len(lineids)==1
            slot=f['slot'];key=(slot['ClassName'],str(slot['Day']),slot['Period'],f['role'])
            fields.setdefault(next(iter(lineids)),[]).append(dict(role=f['role'],before=f['Value'],expected=expected[key],slot=slot))
        for n,observation in enumerate(page['primaryObservations']):
            id=f'native-line-{n:03}'
            rows.append(dict(id=id,width=page['Width'],height=page['Height'],pixels=asset(q,out),crop=observation['RecognitionCrop'],primary=observation,rules=page['rules']))
            baseline=''.join(g['Text'] for g in sorted((g for g in page['glyphs'] if g['SourceLine']==n),key=lambda g:g['SourceOrder']))
            gold.append(dict(id=id,beforeLine=baseline,fields=fields.get(n,[])))
        scope='Development only: all original primary line crops on first page of old native fictional font0 positive. No crop chosen using errors/gold. Not full-document evidence.'
    else:
        old=SCRATCH/('ocr-manual-capture-20261016-v1' if a.kind=='standalone' else 'ocr-manual-tail-20261016-v1')
        inputs=json.loads((old/'inputs.json').read_text());oracle={r['id']:r for r in json.loads((old/'oracle.json').read_text()) if r['margin']==2 or not r['value']}
        for row in inputs['rows']:
            if row['id'] not in oracle:continue
            src=old/row['pixels']['file'];q=out/src.name;os.link(src,q)
            assert sha(q)==row['pixels']['sha256']
            rows.append(dict(id=row['id'],width=row['width'],height=row['height'],pixels=asset(q,out)))
            gold.append(oracle[row['id']])
        scope='Consumed development images/literals/fonts from prior manual capture study, margin2 plus blank controls. No fresh accuracy credit.'
    manifest.update(images=rows,scope=scope,kind=a.kind)
    write(out/'inputs.json',manifest);write(out/'oracle.json',gold)
    write(out/'protocol.json',dict(scope=scope,models=['v1','v2','equal-posterior-mean-v1-v2'],decoders=['greedy','prefixbeam8-frame8-with-forced-alignment'],lengthPolicies=['same-length','bounded-code'],cropPolicies=['original','all-original-ink-bbox-plus2-within-original-crop'],fallback='actual-current-v1-manual-route',rejection='max for mean, original for individuals; <=.05',vocabulary=None,patternPrior=None,coefficients=[.5,.5],sourceConfidence='original aligned frame probabilities, never clamped/promoted',goldAvailableToWorker=False))
    print(json.dumps(dict(output=str(out),rows=len(rows),scope=scope)))

if __name__=='__main__':main()
