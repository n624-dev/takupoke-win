"""Exclusive preparation from retained inference-only fictional bytes."""
import hashlib
import json
from pathlib import Path
from contract import prepare_plan, Refusal

ROOT = Path(__file__).resolve().parent
INPUT_SHA = '54866e2784434e266c21e5d5257728c3cb63f657a092fa14c8b9083aa24183b7'


def main():
    raw = (ROOT / 'inputs.json').read_bytes()
    if len(raw) > 256 * 1024 or hashlib.sha256(raw).hexdigest() != INPUT_SHA:
        raise Refusal('EXACT_RETAINED_FICTIONAL_INPUT_REQUIRED')
    plan = prepare_plan(json.loads(raw)['tasks'])
    with (ROOT / 'caller-plan.json').open('x', encoding='utf-8') as stream:
        stream.write(json.dumps(plan, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'maximumTotalCalls': 2, 'cases': [r['binding']['taskID'] for r in plan['records']],
                      'modelImports': 0, 'engineCalls': 0, 'oracleReads': 0, 'productionAdoption': False}))


if __name__ == '__main__':
    main()
