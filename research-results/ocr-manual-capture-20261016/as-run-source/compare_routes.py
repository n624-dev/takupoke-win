#!/usr/bin/env python3
"""Compare closed pre-change/candidate runtime observations, never raw Japanese alone."""
import argparse
import collections
import json
from pathlib import Path

from run import distance, sha, write


def compare(study):
    freeze = json.loads((study/'freeze.json').read_text())
    baseline = json.loads((study/'baseline-freeze.json').read_text())
    current_receipt = json.loads((study/'worker-receipt.json').read_text())
    old_receipt = json.loads((study/'baseline-receipt.json').read_text())
    if current_receipt['exitCode'] or old_receipt['exitCode']:
        raise ValueError('Both workers must close successfully')
    if sha(study/'raw.jsonl') != current_receipt['raw']['sha256'] or sha(study/'baseline-raw.jsonl') != old_receipt['rawSHA256']:
        raise ValueError('Closed raw output changed')
    if sha(study/'inputs.json') != freeze['inputs']['sha256'] or sha(study/'inputs.json') != baseline['inputSHA256']:
        raise ValueError('Workers did not receive identical frozen input')
    if sha(study/'oracle.json') != freeze['oracle']['sha256']:
        raise ValueError('Reference changed')
    for record in freeze['reader']:
        if sha(study/record['file']) != record['sha256']:
            raise ValueError('Candidate reader changed')
    for name, digest in baseline['readerFiles'].items():
        if sha(study/'baseline-reader'/name) != digest:
            raise ValueError('Baseline reader changed')
    gold = {r['id']: r for r in json.loads((study/'oracle.json').read_text())}
    def read(name):
        records = [json.loads(line) for line in (study/name).read_text().splitlines()]
        selected = [r for r in records if r['model'] == 'actual-code-route']
        if len(selected) != len(gold) or len({r['id'] for r in selected}) != len(gold):
            raise ValueError('Incomplete/duplicate runtime observations')
        return {r['id']: r for r in selected}
    before, after = read('baseline-raw.jsonl'), read('raw.jsonl')
    groups = collections.defaultdict(list)
    for ordinal, g in gold.items():
        groups[g['font'],g['group'],g['margin']].append((g,before[ordinal],after[ordinal]))
    scores = []
    for (font,group,margin), items in sorted(groups.items()):
        row = dict(font=font, group=group, margin=margin, count=len(items))
        for label,index in [('before',1),('after',2)]:
            row[label] = dict(exact=sum(r[index]['text']==r[0]['value'] for r in items),
                edits=sum(distance(r[0]['value'],r[index]['text']) for r in items),
                wrongAbovePoint8=sum(r[index]['text']!=r[0]['value'] and bool(r[index]['pieces']) and
                    min(p['Confidence'] for p in r[index]['pieces'])>=.8 for r in items),
                specialistCalls=sum(r[index]['recognizerCalls'] for r in items))
        row['gains'] = sum(b['text']!=g['value'] and a['text']==g['value'] for g,b,a in items)
        row['regressions'] = sum(b['text']==g['value'] and a['text']!=g['value'] for g,b,a in items)
        scores.append(row)
    report = dict(scope='PDF-free isolated-code recognition. Fresh strings in four previously observed fonts. Paired margins are separate conditions, never independent sample counts.',
        baselineSourceCommit=baseline['baselineSourceCommit'], currentRawSHA256=sha(study/'raw.jsonl'),
        baselineRawSHA256=sha(study/'baseline-raw.jsonl'), fieldsPerMargin=384, blankImages=4, scores=scores,
        humanCorrectionTimeMeasured=False, productionQualification=False, modelWeightsChanged=False)
    write(study/'paired-routes.json', report)
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--study',type=Path,required=True)
    args = parser.parse_args()
    result = compare(args.study.resolve())
    for margin in [2,6]:
        rows = [r for r in result['scores'] if r['margin']==margin]
        print(json.dumps(dict(margin=margin, fields=sum(r['count'] for r in rows),
            before=sum(r['before']['exact'] for r in rows),after=sum(r['after']['exact'] for r in rows),
            gains=sum(r['gains'] for r in rows),regressions=sum(r['regressions'] for r in rows))))
