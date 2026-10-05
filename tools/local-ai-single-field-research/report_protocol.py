"""Bounded post-cleanup protocol accounting; no role oracle or quality credit."""
from collections import Counter
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def main():
    path = ROOT / 'responses.jsonl'
    rows = []
    if path.exists():
        if path.stat().st_size > 1024 * 1024:
            raise RuntimeError('BOUNDED_RESPONSES_REQUIRED')
        rows = [json.loads(line) for line in path.read_text().splitlines()]
    if len(rows) > 1 or len({r['call'] for r in rows}) != len(rows):
        raise RuntimeError('EXACT_ONE_CALL_ACCOUNTING')
    report = {'completedCalls': len(rows), 'missingCallsUNASSESSED': 1 - len(rows),
              'dispositions': dict(Counter(r['disposition'] for r in rows)),
              'fieldRoleQuality': 'UNASSESSED_NO_EVALUATOR_READ', 'qualifiedGenAIModels': [],
              'originalInkOwnershipProof': 'ABSENT', 'roleAssignmentProof': 'ABSENT',
              'formalSlots': {'assessed': 0, 'UNASSESSED': 1270},
              'formalClocks': {'assessed': 0, 'UNASSESSED': 70}, 'productionAdoption': False}
    with (ROOT / 'protocol-report.json').open('x') as stream:
        stream.write(json.dumps(report, indent=2) + '\n')


if __name__ == '__main__':
    main()
