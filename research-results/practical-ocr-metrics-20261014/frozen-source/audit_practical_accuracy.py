#!/usr/bin/env python3
"""Closed-reader character error and review burden, without changing raw text."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
READERS = [('v5-mobile', 'ocr-model-color-frozen-20261013/linux-observations.jsonl'),
           ('v6-medium', 'ocr-model-color-frozen-20261013/linux-observations.jsonl'),
           ('glm-ocr-fp32', 'glm-ocr-fp32-clean-lines-20261014/observations.jsonl')]


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def write(p, value):
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')


def distance(left, right):
    previous = list(range(len(right)+1))
    for row, char in enumerate(left, 1):
        current = [row]
        for column, other in enumerate(right, 1):
            current.append(min(previous[column]+1, current[-1]+1, previous[column-1]+(char != other)))
        previous = current
    return previous[-1]


def without_space(text):
    return ''.join(char for char in text if not char.isspace())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    oracle = ROOT/'tools/vision-ocr-comparison/oracle.json'
    freeze = {'recipe': 'practical-character-error-and-review-burden-consumed-audit-v1',
              'sourceSha256': sha(Path(__file__)), 'oracleSha256': sha(oracle),
              'readers': {model: sha(ROOT/'research-results'/file) for model, file in READERS},
              'scope': 'Posthoc consumed component comparison, zero inference. Raw literal exact and CER remain primary. Whitespace-only differences are a separate diagnostic, never value repair or acceptance. No perfect/zero-error deployment requirement is imposed.',
              'confidenceScope': 'CTC minimum .8 flags reported separately from GLM autoregressive tokens; neither is calibrated field accuracy. Not a user-edit count or whole-document completeness proof.',
              'additionalModelCalls': 0, 'modelActivation': False}
    write(args.output/'freeze.json', freeze)
    gold = {r['id']: r for r in json.loads(oracle.read_text())}
    reports = []
    for model, file in READERS:
        records = [json.loads(line) for line in (ROOT/'research-results'/file).read_text().splitlines()]
        if model != 'glm-ocr-fp32':
            records = [r for r in records if r['model'] == model and r['condition'] == 'bgr']
        if len(records) != 192 or {r['id'] for r in records} != set(gold):
            raise ValueError('Closed corpus identity mismatch')
        groups = {}
        for r in records:
            expected = gold[r['id']]
            if expected['kind'] == 'blank':
                continue
            key = 'body' if expected['kind'] in ('subject', 'teacher', 'room') else 'header'
            for group_name in (key, expected['cohort']+'/'+expected['kind']):
                group = groups.setdefault(group_name, dict(rows=0, rawExact=0, rawCharacterEdits=0, sourceCharacters=0,
                    whitespaceOnlyMismatch=0, nonWhitespaceEdits=0, sourceNonWhitespaceCharacters=0,
                    ctcFlagged=0, ctcFlaggedWrong=0, ctcUnflaggedWrong=0, ctcFlaggedCorrect=0))
                source = expected['literal']
                exact = source == r['text']
                group['rows'] += 1
                group['rawExact'] += exact
                group['rawCharacterEdits'] += distance(source, r['text'])
                group['sourceCharacters'] += len(source)
                clean_source, clean_reading = without_space(source), without_space(r['text'])
                group['whitespaceOnlyMismatch'] += not exact and clean_source == clean_reading
                group['nonWhitespaceEdits'] += distance(clean_source, clean_reading)
                group['sourceNonWhitespaceCharacters'] += len(clean_source)
                if model != 'glm-ocr-fp32':
                    confidence = r['minimumConfidence']
                    flagged = confidence is None or confidence < .8 or r['unsupportedPadding']
                    group['ctcFlagged'] += flagged
                    group['ctcFlaggedWrong'] += flagged and not exact
                    group['ctcUnflaggedWrong'] += not flagged and not exact
                    group['ctcFlaggedCorrect'] += flagged and exact
        for g in groups.values():
            g['rawCer'] = g['rawCharacterEdits']/g['sourceCharacters'] if g['sourceCharacters'] else None
            g['nonWhitespaceCerDiagnostic'] = g['nonWhitespaceEdits']/g['sourceNonWhitespaceCharacters'] if g['sourceNonWhitespaceCharacters'] else None
        reports.append({'model': model, 'groups': groups})
    if sha(oracle) != freeze['oracleSha256']:
        raise ValueError('Oracle identity changed')
    write(args.output/'report.json', {'readers': reports, 'additionalModelCalls': 0,
          'limits': freeze['scope']+' '+freeze['confidenceScope']})
    for report in reports:
        print(json.dumps({'model': report['model'], 'body': report['groups']['body']}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
