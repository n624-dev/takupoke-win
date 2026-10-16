"""Compare closed factory observations with a qualified, consumed native route."""
import argparse
import hashlib
import json
import math
from pathlib import Path


def canonical(value):
    # Compare actual probabilities separately, including native floor decisions.
    # SourceSnapshot is a hash derived from those actual source probabilities;
    # the real builder/engine validates the new receipt, never rewrites it.
    if isinstance(value, dict):
        return {k: canonical(v) for k, v in value.items()
                if k not in {'Confidence', 'NativeConfidence', 'SourceSnapshot', 'scores'}}
    if isinstance(value, list):
        return [canonical(v) for v in value]
    return value


def digest(value):
    data = json.dumps(canonical(value), ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()
    return hashlib.sha256(data).hexdigest()


def signature(row):
    pages = []
    for page in row['pageReports']:
        scores = page['scores']
        ctc = [p['Confidence'] for o in page['primaryObservations'] for p in o['Pieces']]
        pages.append(dict(page=page['number'], literalGeometryAndCallsSHA256=digest(page),
                          nativeScores=scores, nativeFloor=[s < .8 for s in scores],
                          allObservedCtcProbabilities=ctc))
    return dict(id=row['id'], stage=row['stage'], outcome=row['outcome'], failure=row['failure'],
                documentSHA256=digest(row['document']), formalSHA256=digest(row['formal']), pages=pages)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--actual', type=Path, required=True)
    parser.add_argument('--reference', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    reference = json.loads(args.reference.read_text())
    expected = {r['id']: r for r in reference['expected']}
    rows = list(map(json.loads, args.actual.read_text().splitlines()))
    assert len(rows) == len(expected) == 2 and {r['id'] for r in rows} == set(expected)
    results = []
    for row in rows:
        actual, old = signature(row), expected[row['id']]
        assert len(actual['pages']) == len(old['pages']) == 5
        differences = []
        same = all(actual[k] == old[k] for k in ['stage', 'outcome', 'failure', 'documentSHA256', 'formalSHA256'])
        for a, b in zip(actual['pages'], old['pages']):
            same &= all(a[k] == b[k] for k in ['page', 'literalGeometryAndCallsSHA256', 'nativeFloor'])
            for key in ['nativeScores', 'allObservedCtcProbabilities']:
                same &= len(a[key]) == len(b[key])
                same &= all(math.isfinite(v) and 0 <= v <= 1 for v in a[key])
                differences.extend(abs(v - w) for v, w in zip(a[key], b[key]))
        difference = max(differences, default=0)
        passed = same and difference <= reference['maximumNativeProbabilityDifference']
        results.append(dict(id=row['id'], passed=passed, sameLiteralsGeometryCallsDocumentAndNativeFloor=same,
                            maximumNativeProbabilityDifference=difference))
    receipt = dict(passed=all(r['passed'] for r in results), referenceRun=reference['referenceRun'],
                   referenceSource=reference['referenceSource'], pages=10, documents=results,
                   actualRawSHA256=hashlib.sha256(args.actual.read_bytes()).hexdigest(),
                   independentAccuracyCredit=False,
                   scope='Actual default factory versus qualified optional path on the exact consumed native originals; probabilities compared without changing them. No new accuracy/human adoption claim.')
    args.output.write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps(receipt), flush=True)
    if not receipt['passed']:
        raise SystemExit('Factory route differs; keep all original observations and do not release')


if __name__ == '__main__':
    main()
