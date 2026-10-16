#!/usr/bin/env python3
"""Fixed Japanese G2 CPU first-pass comparison; worker never receives literals."""
import argparse
import base64
import hashlib
import importlib
import importlib.metadata
import io
import json
from pathlib import Path
import resource
import subprocess
import sys
import time
import types

ROOT = Path(__file__).resolve().parents[2]
PIXELS = ROOT / 'tools/vision-ocr-comparison/fictional-pixels.json'
GOLD = ROOT / 'tools/vision-ocr-comparison/oracle.json'
PIXEL_SHA = '6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff'
WEIGHT_SHA = 'afbb32b0485297de5a7c1f59aeecac12c69f32b15cedd3c3d8c2e8529de85af3'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')


def worker(out, assets):
    import numpy as np
    import torch
    sys.path.insert(0, str(ROOT/'tools/ocr-synthetic-training/ascii-foundation-ctc'))
    from baseline import tensor
    sys.path.insert(0, str(ROOT/'tools/ocr-synthetic-training'))
    from image_tensor import greedy
    receipt = json.loads((assets/'download-receipt.json').read_text())
    pkg = Path(importlib.metadata.distribution('easyocr').locate_file('easyocr'))
    for name, expected in receipt['codeFiles'].items():
        if sha(pkg/name) != expected: raise ValueError('Official source changed')
    weights = assets/receipt['modelConfig']['filename']
    if sha(weights) != WEIGHT_SHA: raise ValueError('Pinned weights changed')
    module = types.ModuleType('pinned_japanese_g2'); module.__path__ = [str(pkg/'model')]
    sys.modules[module.__name__] = module
    Model = importlib.import_module('pinned_japanese_g2.vgg_model').Model
    dictionary = ['']+list(receipt['modelConfig']['characters'])
    if len(dictionary) != 2215: raise ValueError('Dictionary changed')
    torch.set_num_threads(2); torch.set_num_interop_threads(1)
    model = Model(input_channel=1, output_channel=256, hidden_size=256, num_class=len(dictionary)).eval()
    state = torch.load(weights, map_location='cpu', weights_only=True)
    if not isinstance(state, dict) or not all(isinstance(v, torch.Tensor) and k.startswith('module.') for k,v in state.items()):
        raise ValueError('Unexpected checkpoint')
    model.load_state_dict({k[7:]:v for k,v in state.items()}, strict=True)
    inputs = json.loads((out/'inputs.json').read_text())
    started = time.monotonic()
    with (out/'observations.jsonl').open('x', encoding='utf-8', newline='\n') as stream, torch.inference_mode():
        for n,row in enumerate(inputs, 1):
            path = out/row['file']
            if sha(path) != row['sha256']: raise ValueError('Pixels changed')
            array, valid = tensor(path); timer = time.monotonic()
            probability = model(torch.from_numpy(array), None).softmax(-1)[0].numpy()
            elapsed = time.monotonic()-timer
            pieces = greedy(probability, dictionary)
            supported = all(p['End']*array.shape[-1] <= valid*probability.shape[0] for p in pieces)
            indices = probability.argmax(-1); nonblank = probability.max(-1)[indices != 0]
            official = float(np.prod(nonblank)**(2/np.sqrt(len(nonblank)))) if len(nonblank) else 0
            record = {'id':row['id'], 'text':''.join(p['Text'] for p in pieces), 'pieces':pieces,
                      'minimumConfidence':min((p['Confidence'] for p in pieces), default=0),
                      'officialSequenceConfidence':official, 'unsupportedPadding':not supported,
                      'inputWidth':array.shape[-1], 'validWidth':valid, 'timeSteps':probability.shape[0],
                      'inferenceMilliseconds':elapsed*1000}
            stream.write(json.dumps(record, ensure_ascii=False)+'\n'); stream.flush()
            if n%48 == 0: print(json.dumps({'completed':n}), flush=True)
    write(out/'worker-report.json', {'callsCompleted':len(inputs), 'elapsedSeconds':time.monotonic()-started,
          'peakRssBytes':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss*1024,
          'parameters':sum(p.numel() for p in model.parameters()), 'qualification':False})


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--assets', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True); parser.add_argument('--python')
    parser.add_argument('--worker', action='store_true'); args = parser.parse_args()
    if args.worker: worker(args.output, args.assets); return
    from PIL import Image
    out = args.output; out.mkdir(parents=True, exist_ok=False)
    if sha(PIXELS) != PIXEL_SHA: raise ValueError('Frozen corpus changed')
    receipt = json.loads((args.assets/'download-receipt.json').read_text())
    if sha(args.assets/receipt['modelConfig']['filename']) != WEIGHT_SHA: raise ValueError('Wrong model')
    rows = []
    for row in json.loads(PIXELS.read_text())['rows']:
        data = base64.b64decode(row['pngBase64'], validate=True)
        if hashlib.sha256(data).hexdigest() != row['pngSha256']: raise ValueError('PNG mismatch')
        image = Image.open(io.BytesIO(data)); image.load()
        if image.size != (row['width'], row['height']): raise ValueError('Shape mismatch')
        path = out/(row['id']+'.png'); path.write_bytes(data)
        rows.append({'id':row['id'], 'file':path.name, 'sha256':sha(path)})
    if len(rows) != 192 or len({r['id'] for r in rows}) != 192: raise ValueError('Corpus incomplete')
    write(out/'inputs.json', rows)
    sources = [Path(__file__), ROOT/'tools/ocr-synthetic-training/ascii-foundation-ctc/baseline.py',
               ROOT/'tools/ocr-synthetic-training/image_tensor.py']
    freeze = {'recipe':'easyocr-japanese-g2-consumed-clean-lines-v1', 'model':receipt,
              'pixelsSha256':PIXEL_SHA, 'oracleSha256':sha(GOLD), 'inputsSha256':sha(out/'inputs.json'),
              'sources':{str(p.relative_to(ROOT)):sha(p) for p in sources}, 'callsPlanned':192,
              'condition':'Official FP32 Gray64, PIL bicubic, width multiple64, last-column padding; strict tensor-only loading; full2215 dictionary greedy CTC; no detector, lexicon, filtering, contrast retry or quantization',
              'scope':'Consumed fictional clean-line component development; no final/full-document/Windows qualification',
              'qualification':False, 'modelActivation':False}
    write(out/'freeze.json', freeze)
    with (out/'worker-stdout.txt').open('w') as stdout, (out/'worker-stderr.txt').open('w') as stderr:
        child = subprocess.run([args.python, str(Path(__file__).resolve()), '--worker', '--assets',str(args.assets),
                                '--output',str(out)], stdout=stdout, stderr=stderr, timeout=900)
    raw = out/'observations.jsonl'
    write(out/'raw-receipt.json', {'exitCode':child.returncode, 'bytes':raw.stat().st_size if raw.exists() else None,
                                 'sha256':sha(raw) if raw.exists() else None})
    if child.returncode: raise RuntimeError('Worker failed; no precision credit')
    if sha(GOLD) != freeze['oracleSha256'] or any(sha(ROOT/p) != h for p,h in freeze['sources'].items()):
        raise ValueError('Oracle/source changed during inference')
    # Only the closed raw output is scored; literals never enter the subprocess.
    gold = {r['id']:r for r in json.loads(GOLD.read_text())}
    records = [json.loads(line) for line in raw.read_text().splitlines()]
    if len(records) != 192 or {r['id'] for r in records} != set(gold): raise ValueError('Incomplete calls')
    groups = {}
    for r in records:
        g = gold[r['id']]; key = g['cohort']+'/'+g['kind']
        count = groups.setdefault(key, {'rows':0, 'exact':0, 'correctAboveFloor':0, 'incorrectAboveFloor':0, 'unsupportedPadding':0})
        exact = r['text'] == g['literal']; above = bool(r['text']) and r['minimumConfidence'] >= .8 and not r['unsupportedPadding']
        count['rows'] += 1; count['exact'] += exact; count['correctAboveFloor'] += exact and above
        count['incorrectAboveFloor'] += not exact and above; count['unsupportedPadding'] += r['unsupportedPadding']
    report = {'freezeSha256':sha(out/'freeze.json'), 'rawSha256':sha(raw), 'callsCompleted':len(records), 'groups':groups,
              'worker':json.loads((out/'worker-report.json').read_text()), 'qualification':False, 'modelActivation':False,
              'limits':'CTC min-piece confidence is uncalibrated; scores do not establish source ownership or document acceptance. Timing is Linux CPU under concurrent work.'}
    write(out/'report.json', report); print(json.dumps(report, ensure_ascii=False), flush=True)


if __name__ == '__main__': main()
