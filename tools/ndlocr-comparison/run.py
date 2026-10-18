#!/usr/bin/env python3
"""Fixed NDLOCR-Lite component comparison on consumed development pixels."""
import argparse
from collections import defaultdict
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
PIXELS = ROOT.parent / 'vision-ocr-comparison/fictional-pixels.json'
GOLD = ROOT.parent / 'vision-ocr-comparison/oracle.json'
REVISION = 'd001dad2013ceabb5736ff8d637474e443eb73c2'
PIXEL_SHA = '6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff'
MODELS = [
    {'id': 'ndl-30', 'filename': 'parseq-ndl-24x256-30-tiny-189epoch-tegaki3-r8data-202604.onnx',
     'sha256': '9e651bae4c1a4d5254da1127e86e82e21ef62d5339b37e62d4a3d3d30831772d',
     'bytes': 36457393, 'width': 256, 'maximumCharacters': 30},
    {'id': 'ndl-50', 'filename': 'parseq-ndl-24x384-50-tiny-300epoch-tegaki3-r8data-202604.onnx',
     'sha256': '49cea9db4552f19eb05c8ee202fcf74714977749b2f4c9376b127fde41b07a99',
     'bytes': 37808553, 'width': 384, 'maximumCharacters': 50},
    {'id': 'ndl-100', 'filename': 'parseq-ndl-24x768-100-tiny-153epoch-tegaki3-r8data-202604.onnx',
     'sha256': '06462b0dbd5b0b8508545c8c3d485cf20dbf4ffa652fe145e69c9e7457080602',
     'bytes': 42588187, 'width': 768, 'maximumCharacters': 100}]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, obj):
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')


def distance(a, b):
    row = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        new = [i]
        for j, y in enumerate(b, 1):
            new.append(min(new[-1] + 1, row[j] + 1, row[j - 1] + (x != y)))
        row = new
    return row[-1]


def score(records, oracle):
    gold = {r['id']: r for r in oracle}
    expected = {(m['id'], key) for m in MODELS for key in gold}
    keys = [(r['model'], r['id']) for r in records]
    if len(gold) != 192 or len(keys) != len(set(keys)) or set(keys) != expected:
        raise ValueError('Incomplete, extra or duplicated comparison')
    aggregates = defaultdict(lambda: {'rows': 0, 'exact': 0, 'wrong': 0,
                                      'editDistance': 0, 'referenceCharacters': 0,
                                      'wrongWithMinimumTokenProbabilityAtLeast08': 0,
                                      'missingEos': 0})
    for record in records:
        item = gold[record['id']]
        for category in (item['kind'], 'body' if item['kind'] in ('subject', 'teacher', 'room') else 'nonbody'):
            a = aggregates[(record['model'], item['cohort'], category)]
            exact = record['text'] == item['literal']
            a['rows'] += 1
            a['exact'] += int(exact)
            a['wrong'] += int(not exact)
            a['editDistance'] += distance(record['text'], item['literal'])
            a['referenceCharacters'] += len(item['literal'])
            a['missingEos'] += int(not record['eosObserved'])
            a['wrongWithMinimumTokenProbabilityAtLeast08'] += int(not exact and record['minimumTokenProbabilityIncludingEos'] is not None and record['minimumTokenProbabilityIncludingEos'] >= .8)
    return [{'model': model, 'cohort': cohort, 'kind': kind, **value}
            for (model, cohort, kind), value in sorted(aggregates.items())]


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--upstream', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    revision = subprocess.check_output(['git', '-C', str(args.upstream), 'rev-parse', 'HEAD'], text=True).strip()
    if revision != REVISION or digest(PIXELS) != PIXEL_SHA:
        raise ValueError('Pinned upstream/pixels mismatch')
    args.output.mkdir(parents=True, exist_ok=False)
    freeze = {'recipe': 'ndlocr-native-parseq-consumed-pixels-v1', 'upstreamRevision': revision,
              'pixelsSHA': PIXEL_SHA, 'oracleSHA': digest(GOLD), 'models': MODELS,
              'upstreamSources': {name: digest(args.upstream / name) for name in
                                  ('src/parseq.py', 'src/config/NDLmoji.yaml', 'LICENCE')},
              'sources': {name: digest(ROOT / name) for name in ('run.py', 'worker.py')},
              'callsPlanned': 576, 'providers': ['CPUExecutionProvider'], 'threads': 1,
              'preprocessing': 'Native PARSEQ.preprocess, RGB input as upstream PIL; rotation, cv2 bilinear, BGR [-1,1]',
              'decode': 'Native first-EOS autoregressive argmax, no CTC repeat merging; exact raw literal',
              'candidateSelection': 'none; three independent fixed arms, no gold or agreement routing',
              'confidence': 'Uncalibrated PARSeq token softmax including EOS; not a Paddle CTC floor',
              'qualification': 'unassessed', 'scope': 'Consumed recognition-only development pixels; zero new independent document credit'}
    write(args.output / 'freeze.json', freeze)
    env = {k: v for k, v in os.environ.items() if not any(x in k.upper() for x in ('TOKEN', 'SECRET', 'PASSWORD', 'CREDENTIAL'))}
    env['ORT_TELEMETRY_DISABLED'] = '1'
    print(json.dumps({'freezeSHA': digest(args.output / 'freeze.json'), 'callsPlanned': 576}), flush=True)
    raw = args.output / 'observations.jsonl'
    result = subprocess.run([sys.executable, str(ROOT / 'worker.py'), '--upstream', str(args.upstream),
                             '--freeze', str(args.output / 'freeze.json'), '--pixels', str(PIXELS),
                             '--output', str(raw)], env=env, capture_output=True, text=True, encoding='utf-8', timeout=1860)
    (args.output / 'worker-stdout.txt').write_text(result.stdout, encoding='utf-8', newline='\n')
    (args.output / 'worker-stderr.txt').write_text(result.stderr, encoding='utf-8', newline='\n')
    receipt = {'returnCode': result.returncode, 'rawSHA': digest(raw) if raw.exists() else None,
               'workerStdoutSHA': digest(args.output / 'worker-stdout.txt'), 'workerStderrSHA': digest(args.output / 'worker-stderr.txt')}
    write(args.output / 'raw-receipt.json', receipt)
    if result.returncode:
        raise RuntimeError('Worker execution failure; retained outputs remain unassessed')
    if digest(GOLD) != freeze['oracleSHA']:
        raise ValueError('Oracle identity changed')
    # Oracle is first opened as JSON only after every native call has exited.
    records = [json.loads(line) for line in raw.read_text(encoding='utf-8').splitlines()]
    report = {**freeze, **receipt, 'callsCompleted': len(records),
              'aggregates': score(records, json.loads(GOLD.read_text(encoding='utf-8'))),
              'activation': False, 'documentsQualified': 0, 'adoptions': 0}
    write(args.output / 'report.json', report)
    print(json.dumps({'callsCompleted': len(records), 'rawSHA': receipt['rawSHA'],
                      'aggregates': report['aggregates'], 'qualification': 'unassessed'}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
