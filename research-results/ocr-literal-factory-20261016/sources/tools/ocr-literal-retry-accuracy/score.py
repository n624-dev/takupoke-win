"""Assess both CLOSED full native workers. No values are supplied to readers."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ocr-practical-documents'))
from score import assess, slot, distance

sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
write = lambda p, v: p.write_text(json.dumps(v, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--study', type=Path, required=True)
    p.add_argument('--dotnet', required=True)
    a = p.parse_args()
    root = a.study
    freeze = json.loads((root / 'freeze.json').read_text())
    assert sha(root / 'oracle.json') == freeze['oracleSHA256']
    gold = {g['id']: g for g in json.loads((root / 'oracle.json').read_text())}
    protocol = json.loads(Path(__file__).with_name('protocol.json').read_text())
    assert sha(Path(__file__).with_name('protocol.json')) == freeze['protocolSHA256']
    for arm in ['baseline', 'candidate']:
        receipt = json.loads((root / (arm + '-receipt.json')).read_text())
        assert receipt['exitCode'] == 0 and sha(root / (arm + '-observations.jsonl')) == receipt['rawSHA256']
    subprocess.run([a.dotnet, 'build', str(Path(__file__).with_name('Inspect.csproj')), '-c', 'Release', '--nologo'], check=True)
    dll = ROOT / 'tools/ocr-literal-retry-accuracy/bin/Release/net10.0/Inspect.dll'
    write(root / 'inspection-freeze.json', dict(files={str(q.relative_to(dll.parent)): sha(q) for q in dll.parent.rglob('*') if q.is_file()},
        sourceSHA256=sha(ROOT / 'tools/ocr-acquisition-replay/InspectAppSchema.cs'), oracleAvailableToInspector=False))
    rows, inspected, raws, group_metrics = [], {}, {}, {}
    native = {d['id']: d for d in json.loads((root / 'native-inputs.json').read_text())['documents']}
    for arm in ['baseline', 'candidate']:
        raw = root / (arm + '-observations.jsonl')
        output = root / (arm + '-inspection.jsonl')
        subprocess.run([a.dotnet, str(dll), str(raw), str(root / 'native-inputs.json'), str(output)], check=True)
        inspected[arm] = {x['id']: x for x in map(json.loads, output.read_text().splitlines())}
        raws[arm] = {x['id']: x for x in map(json.loads, raw.read_text().splitlines())}
        assert len(inspected[arm]) == len(raws[arm]) == len(gold) == 6
        for id, r in raws[arm].items():
            g, i = gold[id], inspected[arm][id]
            x = assess(r, i, g)
            expected = {(*slot(dict(ClassName=l['className'], Day=str(l['weekday']), Period=l['period'])), role): v
                        for l in g['timetable']['lessons'] for role, v in l['names'].items()}
            tags = {(*slot(dict(ClassName=l['className'], Day=str(l['weekday']), Period=l['period'])), role): tag
                    for l in g['timetable']['lessons'] for role, tag in l['tags'].items()}
            found = {(*slot(f['slot']), f['role']): f for f in i['fields'] if f['lessonIndex'] == 0}
            wrong = {f['cellId'] + '|' + str(f['lessonIndex']) + '|' + f['role'] for k, f in found.items() if k in expected and f['Value'] != expected[k]}
            role = {0: 'subject', 1: 'teacher', 2: 'room'}
            mandatory = {t['CellId'] + '|' + str(t['LessonIndex']) + '|' + role[t['Role']] for t in i.get('reviewTargets') or []}
            missing_edits = sum(len(v) for k, v in expected.items() if k not in found)
            complete = len(r['pageReports']) == len(native[id]['pages']) == 5
            if r.get('document'):
                assert [(p['Page'], p['RasterHash']) for p in r['document']['Capture']['Pages']] == [(p['page'], p['pixels']['sha256']) for p in native[id]['pages']]
            x.update(arm=arm, font=g['font'], expect=g['expect'], acquisitionComplete=complete,
                     referenceCorrectionAndMandatoryReviewUnion=len(wrong | mandatory),
                     coverageAwareCharacterEdits=x['rawCharacterEdits'] + missing_edits,
                     allLiteralErrorsCorrectableWithinCurrentBudget=x['completeSlotCoverage'] and x['missingFields'] == 0 and x['unexpectedFields'] == 0 and x['duplicateFields'] == 0 and
                        (i.get('reviewPlanEligible') or r['outcome'] == 'validated-preview') and len(wrong | mandatory) <= 128)
            if g['expect'] == 'refuse':
                x['negativeOutcome'] = 'refused' if r['formal'] is None and not i.get('reviewPlanEligible') else 'returned'
            rows.append(x)
            if g['expect'] == 'refuse':
                continue
            groups = {}
            for key, expected_value in expected.items():
                tag = tags[key]
                value = found.get(key, {}).get('Value')
                metrics = groups.setdefault(tag, dict(expected=0, exact=0, edits=0))
                metrics['expected'] += 1
                metrics['exact'] += value == expected_value
                metrics['edits'] += distance(value or '', expected_value)
            group_metrics[(arm, id)] = groups
    pairs, groups, changes = [], [], []
    for id, g in gold.items():
        if g['expect'] == 'refuse':
            continue
        before = next(x for x in rows if x['arm'] == 'baseline' and x['id'] == id)
        after = next(x for x in rows if x['arm'] == 'candidate' and x['id'] == id)
        pairs.append(dict(id=id, font=g['font'], beforeExact=before['exactFields'], afterExact=after['exactFields'],
                          beforeCoverageAwareEdits=before['coverageAwareCharacterEdits'], afterCoverageAwareEdits=after['coverageAwareCharacterEdits']))
        for tag, old in group_metrics[('baseline', id)].items():
            new = group_metrics[('candidate', id)][tag]
            groups.append(dict(id=id, font=g['font'], tag=tag, expected=old['expected'], beforeExact=old['exact'], afterExact=new['exact'], beforeEdits=old['edits'], afterEdits=new['edits']))
        fields = {arm: {(*slot(f['slot']), f['role']): f for f in inspected[arm][id]['fields'] if f['lessonIndex'] == 0} for arm in ['baseline', 'candidate']}
        for lesson in g['timetable']['lessons']:
            for role, expected in lesson['names'].items():
                key = (lesson['className'], str(lesson['weekday']), lesson['period'], role)
                before = fields['baseline'].get(key, {}).get('Value')
                after = fields['candidate'].get(key, {}).get('Value')
                if before != after:
                    changes.append(dict(id=id, key=key, expected=expected, before=before, after=after, gain=before != expected and after == expected, loss=before == expected and after != expected))
    criteria = protocol['criteria']
    positives = [x for x in rows if x['arm'] == 'candidate' and x['expect'] != 'refuse']
    negatives = [x for x in rows if x['arm'] == 'candidate' and x['expect'] == 'refuse']
    quality = len(positives) == 2 and len(negatives) == 4 and all(x['allLiteralErrorsCorrectableWithinCurrentBudget'] and x['exactFields'] / x['expectedFields'] >= criteria['minimumExactFieldRate'] and x['conditionalCER'] <= criteria['maximumCERPerFont'] and x['processPeakWorkingSetBytes'] <= criteria['maximumRSSBytes'] for x in positives) and all(x['acquisitionComplete'] and x['negativeOutcome'] == 'refused' for x in negatives)
    nonregression = all(p['afterExact'] >= p['beforeExact'] and p['afterCoverageAwareEdits'] <= p['beforeCoverageAwareEdits'] for p in pairs)
    subgroup = all(p['afterExact'] >= p['beforeExact'] and p['afterEdits'] <= p['beforeEdits'] for p in groups)
    development = protocol['development']
    strict = sum(p['afterExact'] - p['beforeExact'] for p in pairs) + sum(development['candidateFields']) - sum(development['baselineFields']) > 0
    report = dict(recipe=protocol['recipe'], qualityCriteriaMet=quality, perFontNonregressionMet=nonregression,
                  perSubgroupNonregressionMet=subgroup, combinedStrictImprovementMet=strict, paired=pairs, subgroups=groups,
                  observations=rows, development=development, developmentIndependentAccuracyCredit=False,
                  freshGainFields=sum(v['gain'] for v in changes), freshLossFields=sum(v['loss'] for v in changes),
                  rawSHA256={arm: sha(root / (arm + '-observations.jsonl')) for arm in ['baseline', 'candidate']},
                  limits='Prospectively fixed new-literal holdout in consumed font files and geometry; no unseen-font/family/pretraining, general real-school accuracy, human-time or unattended-adoption claim.')
    write(root / 'field-changes.json', changes)
    write(root / 'report.json', report)
    print(json.dumps(dict(qualityCriteriaMet=quality, perFontNonregressionMet=nonregression, perSubgroupNonregressionMet=subgroup,
                         combinedStrictImprovementMet=strict, freshGains=report['freshGainFields'], freshLosses=report['freshLossFields'])), flush=True)
    if not all([quality, nonregression, subgroup, strict]):
        raise SystemExit('Prospective criteria failed; retain raw outputs and keep deployed default unchanged')


if __name__ == '__main__':
    main()
