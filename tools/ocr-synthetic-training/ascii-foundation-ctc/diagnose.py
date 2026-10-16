"""Read-only post-scoring diagnosis. Does not load a model or decode new pixels."""
from collections import Counter
import hashlib
import json
from pathlib import Path

ROOT = Path('/workspace/scratch/takupoke-win/research-results/ocr-synthetic-training-20261013/ascii-foundation-ctc-v1')
DEV = Path('/workspace/scratch/ascii-line-ctc-20261013-v1/development')


def edits(gold, prediction):
    """Minimum edit alignment, deterministic tie order substitution/delete/insert."""
    cost = [[0] * (len(prediction) + 1) for _ in range(len(gold) + 1)]
    for i in range(len(gold) + 1):
        cost[i][0] = i
    for j in range(len(prediction) + 1):
        cost[0][j] = j
    for i, left in enumerate(gold, 1):
        for j, right in enumerate(prediction, 1):
            cost[i][j] = min(cost[i-1][j-1] + (left != right), cost[i-1][j] + 1, cost[i][j-1] + 1)
    i, j = len(gold), len(prediction)
    result = []
    while i or j:
        if i and j and cost[i][j] == cost[i-1][j-1] + (gold[i-1] != prediction[j-1]):
            if gold[i-1] != prediction[j-1]:
                result.append((gold[i-1], prediction[j-1]))
            i -= 1
            j -= 1
        elif i and cost[i][j] == cost[i-1][j] + 1:
            result.append((gold[i-1], ''))
            i -= 1
        else:
            result.append(('', prediction[j-1]))
            j -= 1
    return list(reversed(result))


def main():
    manifest_path = DEV / 'manifest.json'
    rows = {row['id']: row for row in json.loads(manifest_path.read_text(encoding='utf-8'))['rows']}
    conditions = {}
    records_by_condition = {}
    for condition in ['baseline', 'framed']:
        raw = ROOT / f'{condition}-unjudged.jsonl'
        records = [json.loads(line) for line in raw.read_text(encoding='utf-8').splitlines()]
        summary = json.loads((ROOT / f'{condition}-summary.json').read_text(encoding='utf-8'))
        assert hashlib.sha256(raw.read_bytes()).hexdigest() == summary['rawSha256']
        assert len(records) == len(rows) == 512 and len({row['id'] for row in records}) == 512
        records_by_condition[condition] = {row['id']: row for row in records}
        all_edits, accepted_edits = Counter(), Counter()
        positive_groups = {}
        for group in ['code', 'lookalike', 'natural', 'printable']:
            group_rows = [r for r in records if rows[r['id']]['kind'] == group]
            assert len(group_rows) == 96
            positive_groups[group] = {'exact': 0, 'correctAccepted': 0, 'incorrectAccepted': 0, 'caseMismatchRows': 0, 'confusableMismatchRows': 0, 'caseMismatchAccepted': 0, 'confusableMismatchAccepted': 0}
            for row in group_rows:
                gold = rows[row['id']]['text']
                mismatch = edits(gold, row['prediction'])
                all_edits.update(mismatch)
                if row['accepted']:
                    accepted_edits.update(mismatch)
                case = any(a and b and a != b and a.lower() == b.lower() for a, b in mismatch)
                confusable = any(a and b and a != b and (a in 'Il1' and b in 'Il1' or a in 'O0' and b in 'O0') for a, b in mismatch)
                stats = positive_groups[group]
                stats['exact'] += not mismatch
                stats['correctAccepted'] += not mismatch and row['accepted']
                stats['incorrectAccepted'] += bool(mismatch) and row['accepted']
                stats['caseMismatchRows'] += case
                stats['confusableMismatchRows'] += confusable
                stats['caseMismatchAccepted'] += case and row['accepted']
                stats['confusableMismatchAccepted'] += confusable and row['accepted']
        wrong_accepted = [r for r in records if not rows[r['id']]['reject'] and r['accepted'] and r['prediction'] != rows[r['id']]['text']]
        conditions[condition] = {'rawSha256': summary['rawSha256'], 'positiveGroups': positive_groups, 'maximumWrongAcceptedMinimumConfidence': max(r['minimumConfidence'] for r in wrong_accepted), 'mostCommonEdits': [{'gold': a, 'prediction': b, 'count': count, 'acceptedCount': accepted_edits[(a, b)]} for (a, b), count in all_edits.most_common(20)]}
    before, after = records_by_condition['baseline'], records_by_condition['framed']
    positive_ids = [key for key, row in rows.items() if not row['reject']]
    exact = lambda record, key: record[key]['prediction'] == rows[key]['text']
    result = {'scope': 'Consumed-development diagnostic only. Edit alignment is descriptive; no output repair, inference, training, threshold tuning, final opening or activation.', 'manifestSha256': hashlib.sha256(manifest_path.read_bytes()).hexdigest(), 'conditions': conditions, 'framingComparison': {'improvements': sum(not exact(before, key) and exact(after, key) for key in positive_ids), 'regressions': sum(exact(before, key) and not exact(after, key) for key in positive_ids)}, 'limitations': 'No dedicated AI/Al/A1 paired header set was evaluated here. Arbitrary-lookalike and case errors cannot establish success or failure on a particular parent fixture. Ambiguous minimum-edit alignments do not prove glyph localization.'}
    (ROOT / 'development-diagnosis.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(result, ensure_ascii=False))


if __name__ == '__main__':
    main()
