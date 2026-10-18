"""Score completed consumed-header outputs; run only after inference process exits."""
import hashlib
import json
from pathlib import Path

from diagnose import edits
from header_infer import ORDINALS, REPO, RESULTS


def main():
    complete = json.loads((RESULTS / 'inference-complete.json').read_text(encoding='utf-8'))
    raw = RESULTS / 'unjudged.jsonl'
    assert complete['completed'] == 48 and complete['oracleRead'] is False
    assert hashlib.sha256(raw.read_bytes()).hexdigest() == complete['rawSha256']
    records = [json.loads(line) for line in raw.read_text(encoding='utf-8').splitlines()]
    assert [row['id'] for row in records] == ORDINALS
    # This separate scorer is launched only after the oracle-free process exits.
    oracle_path = REPO / 'tools/vision-ocr-comparison/oracle.json'
    oracle = {row['id']: row for row in json.loads(oracle_path.read_text(encoding='utf-8'))}
    judged = []
    for row in records:
        target = oracle[row['id']]
        literal = target['literal']
        difference = edits(literal, row['prediction'])
        judged.append({'id': row['id'], 'cohort': target['cohort'], 'kind': target['kind'], 'literal': literal, 'prediction': row['prediction'], 'exact': literal == row['prediction'], 'accepted': row['accepted'], 'minimumConfidence': row['minimumConfidence'], 'officialSequenceConfidence': row['officialSequenceConfidence'], 'supported': row['supported'], 'paddingPieces': sum(not box['supported'] for box in row['sourceBoxes']), 'allOriginalNonwhiteRetained': row['source']['allOriginalNonwhiteRetained'], 'caseMismatch': any(a and b and a != b and a.lower() == b.lower() for a,b in difference), 'confusableMismatch': any(a and b and a != b and (a in 'Il1' and b in 'Il1' or a in 'O0' and b in 'O0') for a,b in difference)})
    headers = [row for row in judged if row['literal']]
    blanks = [row for row in judged if not row['literal']]
    assert len(headers) == 42 and len(blanks) == 6
    group_stats = lambda group: {'rows': len(group), 'exact': sum(row['exact'] for row in group), 'correctAccepted': sum(row['exact'] and row['accepted'] for row in group), 'incorrectAccepted': sum(not row['exact'] and row['accepted'] for row in group), 'caseMismatch': sum(row['caseMismatch'] for row in group), 'confusableMismatch': sum(row['confusableMismatch'] for row in group), 'paddingRows': sum(not row['supported'] for row in group)}
    summary = {'status': 'Consumed-development suitability only; unqualified/inactive', 'oracleOpenedAfterInferenceExit': True, 'oracleSha256': hashlib.sha256(oracle_path.read_bytes()).hexdigest(), 'rawSha256': complete['rawSha256'], 'header': group_stats(headers), 'cohorts': {cohort: group_stats([row for row in headers if row['cohort'] == cohort]) for cohort in sorted({row['cohort'] for row in headers})}, 'kinds': {kind: group_stats([row for row in headers if row['kind'] == kind]) for kind in sorted({row['kind'] for row in headers})}, 'blank': {'rows': len(blanks), 'nonemptyPredictions': sum(bool(row['prediction']) for row in blanks), 'accepted': sum(row['accepted'] for row in blanks), 'paddingRows': sum(not row['supported'] for row in blanks)}, 'allOriginalNonwhiteRetained': sum(row['allOriginalNonwhiteRetained'] for row in judged), 'incorrectRows': [row for row in judged if not row['exact']], 'limits': 'No training/final credit or production activation. Native EasyOCR sequence confidence and emitted CTC scores retained without calibration. Original-ink/cell ownership and contradictory lookalike renderings remain independent requirements. These images and literals are permanently excluded from future training.'}
    (RESULTS / 'judged.json').write_text(json.dumps(judged, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
    (RESULTS / 'summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(summary, ensure_ascii=False))


if __name__ == '__main__':
    main()
