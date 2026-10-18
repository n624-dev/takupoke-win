#!/usr/bin/env python3
"""Posthoc alphabet coverage; never route, crop, infer or repair a field."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def write(p, value):
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dictionary', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    a = parser.parse_args()
    a.output.mkdir(parents=True, exist_ok=False)
    oracle = ROOT/'tools/vision-ocr-comparison/oracle.json'
    raw = ROOT/'research-results/ocr-english-v5-clean-lines-20261014/observations.jsonl'
    dictionary = json.loads(a.dictionary.read_text())
    if len(dictionary) != 438 or any(len(symbol) > 1 for symbol in dictionary):
        raise ValueError('Expected full English single-codepoint alphabet')
    freeze = {'recipe': 'english438-whole-field-representability-consumed-audit-v1',
              'dictionarySha256': sha(a.dictionary), 'oracleSha256': sha(oracle),
              'closedReaderRawSha256': sha(raw), 'sourceSha256': sha(Path(__file__)),
              'scope': 'Posthoc expected-literal alphabet coverage only; no inference, field routing, source crop, repair, dictionary reduction or quality selection.',
              'additionalModelCalls': 0, 'qualification': False}
    write(a.output/'freeze.json', freeze)
    alphabet = set(dictionary)
    expected = json.loads(oracle.read_text())
    readers = [json.loads(line) for line in raw.read_text().splitlines()]
    if len(expected) != 192 or {r['id'] for r in expected} != {r['id'] for r in readers}:
        raise ValueError('Closed corpus identity mismatch')
    groups = {}
    for row in expected:
        group = groups.setdefault(row['kind'], {'rows': 0, 'wholeLiteralRepresentable': 0,
                                                'wholeLiteralUnrepresentable': 0})
        group['rows'] += 1
        possible = all(char in alphabet for char in row['literal'])
        group['wholeLiteralRepresentable'] += possible
        group['wholeLiteralUnrepresentable'] += not possible
    write(a.output/'report.json', {'groups': groups, 'additionalModelCalls': 0, 'qualification': False,
          'conclusion': 'An English438 specialist cannot replace Japanese-containing whole-field acquisition. ASCII suffix scores are not whole-field correctness. A useful separate branch must preserve Japanese primary and prove field-domain/geometry independently; oracle strings cannot route or cut a source field.'})
    print(json.dumps(groups), flush=True)


if __name__ == '__main__':
    main()
