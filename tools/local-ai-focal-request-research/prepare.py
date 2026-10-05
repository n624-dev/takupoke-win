"""Exclusive model-free plan writer; reads frozen inputs and blind records only."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
from request_contract import prepare_plan, validate_diagnostic, Refusal

ROOT = Path(__file__).resolve().parent
INPUT_SHA = '54866e2784434e266c21e5d5257728c3cb63f657a092fa14c8b9083aa24183b7'
BLIND_RECORDS_SHA = '416b10c97f22275f30d13fcd5a95c9a46066493b5f4ed193fc67053950768a06'


def pinned_json(path, expected, maximum):
    data = path.read_bytes()
    if len(data) > maximum or hashlib.sha256(data).hexdigest() != expected:
        raise Refusal('PINNED_INPUT_BYTES')
    return data


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--blind-records', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    data = pinned_json(ROOT.parent / 'local-ai-three-arm-research/inputs.json', INPUT_SHA, 256 * 1024)
    # These are existing consumed-development blind model diagnostics, not
    # original paint/evaluation truth. No new blind image call is planned.
    raw = pinned_json(args.blind_records.resolve(), BLIND_RECORDS_SHA, 128 * 1024)
    diagnostics = {}
    for line in raw.decode('utf-8').splitlines():
        record = json.loads(line)
        if record['stage'] != 'arm3_blind':
            continue
        if record['taskID'] in diagnostics:
            raise Refusal('DUPLICATE_BLIND_RECORD')
        candidate = record.get('candidate') or {'state': 'UNKNOWN', 'lines': []}
        diagnostics[record['taskID']] = validate_diagnostic(candidate)
    plan = prepare_plan(json.loads(data)['tasks'], diagnostics)
    plan['retainedInputSHA256'] = INPUT_SHA
    plan['retainedBlindResponsesSHA256'] = BLIND_RECORDS_SHA
    plan['retainedBlindMeaning'] = 'OLD_CONSUMED_DEVELOPMENT_DIAGNOSTIC_ONLY; NOT_FRESH_BLIND_ARM_OR_CORRECTION_EVIDENCE'
    # The execution seam receives the core plan without these provenance-only
    # fields; a future guarded SDK adapter must bind both full-file hashes.
    with args.output.open('x') as stream:
        stream.write(json.dumps(plan, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'selectedTasks': len(plan['records']), 'maximumCalls': plan['maximumCalls'],
                      'sourceEligibleMaximumCalls': plan['sourceEligibleMaximumCalls'],
                      'sourceRefusals': [r for r in plan['records'] if r['eligibility'] == 'REFUSED_BEFORE_MODEL'],
                      'modelImports': 0, 'engineCalls': 0, 'newImageCalls': 0}))


if __name__ == '__main__':
    main()
