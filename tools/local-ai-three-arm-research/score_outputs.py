"""Separate evaluator; never imported by the model worker."""
import json
from pathlib import Path
from protocol import FIELDS, HEADERS, decode_blind, decode_candidate, digest

ROOT = Path(__file__).resolve().parent

def assess(inputs, oracle, records):
    by = {}
    invalid_records = []
    expected = {(t['id'], stage) for t in inputs['tasks'] for stage in ('arm2_text', 'arm3_blind', 'arm3_compare')}
    for row in records:
        if not isinstance(row, dict):
            invalid_records.append({'error': 'non-object completion'})
            continue
        key = (row.get('taskID'), row.get('stage'))
        if key not in expected:
            invalid_records.append({'error': 'unknown/malformed completion identity', 'record': row})
            continue
        if key in by:
            by[key] = {'disposition': 'OPERATIONAL_UNASSESSED', 'error': 'DUPLICATE_COMPLETION_NO_SELECTION'}
            invalid_records.append({'error': 'duplicate completion identity', 'taskID': key[0], 'stage': key[1]})
            continue
        by[key] = row
    report = []
    truth = {t['id']: t for t in oracle['tasks']}
    for task in inputs['tasks']:
        t = truth[task['id']]
        baseline = [r['rawText'] for r in task['acquisitionRows']]
        out = {'id': task['id'], 'arm1': {'aiCalls': 0, 'nativeLiteralLines': baseline,
            'literalExact': baseline == t['literalLines'], 'codeRoleBinding': 'UNASSESSED_NO_CURRENT_WHOLE_DOCUMENT_EXECUTION',
            'nativeChunkRefusals': sum(r['currentSafetyFloorPassed'] is False for r in task['acquisitionRows'])},
            'productionSourceProofGate': 'REFUSED_NO_PRODUCTION_ROLE_AND_WHOLE_DOCUMENT_CERTIFICATE',
            'coreValidatorInvoked': False, 'productionAdoption': False}
        for stage in ('arm2_text', 'arm3_blind', 'arm3_compare'):
            row = by.get((task['id'], stage))
            a = {'state': 'UNASSESSED', 'operationalError': None, 'candidate': None, 'literalExact': None,
                 'roleIdUnionExact': None, 'parallelCountExact': None, 'tupleBindingExact': None,
                 'tupleBindingAssessment': 'UNASSESSED', 'formalAccepted': False}
            if row is None:
                a['operationalError'] = 'EXPECTED_COMPLETION_NOT_RETURNED'
            elif row.get('disposition') == 'OPERATIONAL_UNASSESSED':
                a['operationalError'] = row.get('error')
            else:
                try:
                    c = decode_blind(row.get('raw')) if stage == 'arm3_blind' else decode_candidate(row.get('raw'), task, stage == 'arm3_compare')
                    a['state'], a['candidate'] = c['state'], c
                    if c['state'] == 'CANDIDATE':
                        if stage == 'arm3_blind':
                            a['literalExact'] = c['lines'] == t['literalLines']
                        else:
                            union = {k: c['headers'][k] for k in HEADERS}
                            for k in FIELDS:
                                union[k] = [x for lesson in c['lessons'] for x in lesson[k]]
                            order = {s['id']: i for i, s in enumerate(task['sources'])}
                            union = {k: sorted(v, key=lambda x: order[x]) for k, v in union.items()}
                            a['roleIdUnionExact'] = union == t['expectedOwnedRoleIDs']
                            a['parallelCountExact'] = len(c['lessons']) == t['expectedParallelCount']
                            # Values come ONLY from original OCR IDs. No correction
                            # from blind strings, expected paint, or other answers.
                            sources = {s['id']: s for s in task['sources']}
                            tuples=[{k:''.join(sources[x]['text'] for x in lesson[k]) for k in FIELDS} for lesson in c['lessons']]
                            reconstructed=[lesson[k] for lesson in tuples for k in FIELDS]
                            a['originalIDValues'] = reconstructed
                            a['originalIDTuples']=tuples
                            if t.get('expectedTuples') is not None:
                                a['tupleBindingExact']=tuples==t['expectedTuples']
                                a['tupleBindingAssessment']='ASSESSED_ORIGINAL_ORACLE_FULL_TUPLES'
                            else:a['tupleBindingAssessment']='UNASSESSED_NO_BOUND_ORIGINAL_ORACLE_TUPLE'
                            a['sourceProofDisposition'] = 'CANDIDATE_SOURCE_PROOF_REFUSED'
                    else:
                        a['abstentionIsNotEmptyProof'] = True
                        a['emptyImageNONECorrect'] = stage == 'arm3_blind' and c['state'] == 'NONE' and not t['literalLines']
                except (ValueError, TypeError, KeyError) as exc:
                    a['state'] = 'SCHEMA_REJECTED'
                    a['operationalError'] = None
                    a['schemaError'] = str(exc)
            out[stage] = a
        report.append(out)
    summary = {'arm1': {'tasks': len(report), 'literalExact': sum(t['arm1']['literalExact'] for t in report), 'codeBindingUNASSESSED': len(report)}}
    for stage in ('arm2_text', 'arm3_blind', 'arm3_compare'):
        values = [t[stage] for t in report]
        summary[stage] = {'expected': len(values), 'states': {state: sum(a['state'] == state for a in values) for state in ('CANDIDATE', 'NONE', 'UNKNOWN', 'SCHEMA_REJECTED', 'UNASSESSED')},
                          'literalExact': sum(a['literalExact'] is True for a in values),
                          'literalWrong': sum(a['literalExact'] is False for a in values),
                          'roleIdUnionExact': sum(a['roleIdUnionExact'] is True for a in values),
                          'roleIdUnionWrong': sum(a['roleIdUnionExact'] is False for a in values),
                          'tupleBindingExact':sum(a['tupleBindingExact'] is True for a in values),
                          'tupleBindingWrong':sum(a['tupleBindingExact'] is False for a in values),
                          'tupleBindingUNASSESSED':sum(a['tupleBindingExact'] is None for a in values),
                          'emptyImageNONECorrectWithoutEmptyProof': sum(a.get('emptyImageNONECorrect') is True for a in values),
                          'formalAccepted': 0}
    return {'scope': 'PREVIOUSLY_CONSUMED_FICTIONAL_DEVELOPMENT_COMPONENTS; NOT_UNTOUCHED_HOLDOUT_OR_MODEL_QUALIFICATION',
        'matchedArms': ['OCR_CODE_AI0', 'SAME_OCR_TEXT_AI_IDS', 'SAME_TEXT_ARM_WITH_BLIND_ROI_THEN_COMPARE'],
        'tasks': report, 'summary': summary, 'expectedCalls': len(expected), 'returnedCalls': len(by),
        'missingCallsUNASSESSED': len(expected) - len(by), 'invalidCompletionRecords': invalid_records, 'nativeWholeDocumentProtectiveGate': False,
        'nativeWholeDocumentConfidenceRefusedChunks': 133, 'nativeCorrectWholeRowsRefusedDifferentDenominator': 127,
        'coreValidatorVersionRequired': 9, 'coreValidatorActuallyInvoked': False,
        'wholeDocumentFormalSlots': {'expected': 1270, 'assessed': 0, 'UNASSESSED': 1270},
        'wholeDocumentFormalClocks': {'expected': 70, 'assessed': 0, 'UNASSESSED': 70},
        'actualProductionAdoptionAttempted': False, 'qualifiedGenAIModels': [],
        'candidateValuesCannotReplaceOriginalEvidence': True}

def main():
    inputs = json.loads((ROOT / 'inputs.json').read_text())
    oracle = json.loads((ROOT / 'oracle-evaluation-only.json').read_text())
    records = []
    path = ROOT / 'responses.jsonl'
    if path.exists():
        for line in path.read_text().splitlines():
            # Partial writes are operational evidence, not empty proposals.
            try:
                records.append(json.loads(line))
            except json.JSONDecodeError:
                records.append({'taskID': '__MALFORMED_OUTPUT__', 'stage': str(len(records)), 'error': 'malformed completion'})
    report = assess(inputs, oracle, records)
    report['inputsSHA256'] = digest(ROOT / 'inputs.json')
    report['oracleSHA256'] = digest(ROOT / 'oracle-evaluation-only.json')
    (ROOT / 'comparison-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')

if __name__ == '__main__':
    main()
