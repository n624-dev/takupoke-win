"""Portable model-free single-field research boundary; no evaluation imports."""
from copy import deepcopy
import hashlib
import json
from pathlib import Path
from source_contract import (Refusal, canonical, fingerprint, inspect_source,
                             _exact_keys, MAX_IDS, MAX_RESPONSE_BYTES)

FIELDS = ('subject', 'teacher', 'room')
STATES = ('PRESENT', 'EMPTY', 'UNREADABLE', 'MISSING', 'AMBIGUOUS', 'UNKNOWN', 'REFUSED')
CONDITIONS = ('gemma-single-field', 'qwen-single-field')
SELECTION_SEED = 'single-field-v1:'
MAX_TASKS = 4
MAX_CALLS_PER_MODEL = 4
PURPOSE = 'SINGLE_FIELD_ID_PROPOSAL'
INSTRUCTION = (Path(__file__).parent / 'prompt.txt').read_text(encoding='utf-8')
GEOMETRY = 'ESTIMATED_CTC_FULL_CONTAINMENT_ONLY_NOT_ORIGINAL_INK_OWNERSHIP'


def checked_source(task):
    source = inspect_source(task)
    if task['promptInput']['coordinateScope'] != 'original pixels; CTC character intervals ESTIMATED, not ink boxes':
        raise Refusal('EXPLICIT_ESTIMATED_CTC_PROVENANCE_REQUIRED')
    if task['sourceProof'] != 'NO_PRODUCTION_ROLE_OR_WHOLE_DOCUMENT_CERTIFICATE; CANDIDATE_ONLY':
        raise Refusal('NO_UNVERIFIED_SOURCE_CERTIFICATE')
    if task['developmentConsumed'] is not True:
        raise Refusal('ONLY_CONSUMED_FICTIONAL_DEVELOPMENT')
    rows = task['acquisitionRows']
    if type(rows) is not list or any(type(r) is not dict for r in rows):
        raise Refusal('ACQUISITION_LINEAGE_MISSING')
    by_chunk = {}
    for row in rows:
        _exact_keys(row, ('id', 'rawText', 'currentSafetyFloorPassed', 'belowCurrentConfidenceFloor', 'assessed'), 'ACQUISITION_ROW_KEYS')
        if (type(row['id']) is not int or row['id'] in by_chunk or type(row['rawText']) is not str or
                row['assessed'] is not True or row['currentSafetyFloorPassed'] is not True or
                row['belowCurrentConfidenceFloor'] is not False):
            raise Refusal('ACQUISITION_ROW_UNASSESSED')
        by_chunk[row['id']] = row
    focal = [s for s in task['sources'] if s['owner'] == 'cell']
    for s in focal:
        if (s['fromOcr'] is not True or type(s['chunkID']) is not int or s['chunkID'] not in by_chunk or
                type(s['ctcStart']) is not int or
                type(s['ctcEnd']) is not int or not 0 <= s['ctcStart'] < s['ctcEnd']):
            raise Refusal('ACTUAL_RETAINED_CTC_LINEAGE_REQUIRED')
    if task['originalChunkIDs'] != list(by_chunk) or set(by_chunk) != {s['chunkID'] for s in focal}:
        raise Refusal('INCOMPLETE_ORIGINAL_CHUNK_INVENTORY')
    for chunk, row in by_chunk.items():
        if ''.join(s['text'] for s in focal if s['chunkID'] == chunk) != row['rawText']:
            raise Refusal('ORIGINAL_CHUNK_TEXT_REWRITE')
    return source


def caller_binding(task, field):
    if field not in FIELDS:
        raise Refusal('CALLER_REQUESTED_FIELD_REQUIRED')
    source = checked_source(task)
    return {'taskID': task['id'], 'requestedField': field, 'sourceSnapshotSHA256': fingerprint(task),
            'selectableFocalIds': source['focalIds'], 'geometryProof': GEOMETRY,
            'assignmentCertificate': 'ABSENT', 'emptyProof': None}


def request(task, binding):
    _exact_keys(binding, ('taskID', 'requestedField', 'sourceSnapshotSHA256', 'selectableFocalIds',
                          'geometryProof', 'assignmentCertificate', 'emptyProof'), 'CALLER_BINDING_KEYS')
    if binding != caller_binding(task, binding['requestedField']):
        raise Refusal('CALLER_SOURCE_SCOPE_OR_FIELD_REWRITE')
    source = checked_source(task)
    return {'version': 1, 'purpose': PURPOSE, 'requestedField': binding['requestedField'],
            'callerQueryIsRoleCertificate': False, 'selectableFocalIds': deepcopy(binding['selectableFocalIds']),
            'focal': {'boxLTRB': source['focalBoxLTRB'], 'sources': [s for s in source['sources'] if s['owner'] == 'cell']},
            'context': {'use': 'READ_ONLY_NOT_SELECTABLE', 'boxesLTRB': source['contextBoxesLTRB'],
                        'sources': [s for s in source['sources'] if s['owner'] == 'context']},
            'sourceArrayOrder': [s['id'] for s in source['sources']],
            'coordinateFormats': {'source': 'XYWH original pixels; estimated CTC intervals', 'closedBox': 'LTRB original pixels'},
            'sourceProof': GEOMETRY, 'originalInkOwnershipProof': 'ABSENT',
            'roleAssignmentProof': 'ABSENT', 'emptyProof': None, 'productionAdoption': False}


def schema(task, binding):
    data = request(task, binding)
    return {'type': 'object', 'additionalProperties': False, 'required': ['state', 'ids'],
            'properties': {'state': {'type': 'string', 'enum': list(STATES)},
                           'ids': {'type': 'array', 'maxItems': MAX_IDS, 'uniqueItems': True,
                                   'items': {'type': 'string', 'enum': data['selectableFocalIds']}}}}


def specification(task, binding):
    payload = request(task, binding)
    prompt = INSTRUCTION + '\nREQUEST_DATA:\n' + canonical(payload).decode('utf-8')
    semantic_schema = schema(task, binding)
    return {'taskID': task['id'], 'requestedField': binding['requestedField'], 'prompt': prompt,
            'promptSHA256': hashlib.sha256(prompt.encode('utf-8')).hexdigest(),
            'schema': semantic_schema, 'semanticSchemaSHA256': fingerprint(semantic_schema),
            'task': deepcopy(task), 'binding': deepcopy(binding), 'freshConversationRequired': True}


def decode(raw, task, binding):
    request(task, binding)
    if type(raw) is not str or len(raw.encode('utf-8')) > MAX_RESPONSE_BYTES:
        raise Refusal('RESPONSE_LIMIT')
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise Refusal('DUPLICATE_PROPERTY')
            result[key] = value
        return result
    value = json.loads(raw, object_pairs_hook=pairs,
                       parse_constant=lambda _: (_ for _ in ()).throw(Refusal('NONFINITE_JSON')))
    _exact_keys(value, ('state', 'ids'), 'RESPONSE_KEYS')
    if value['state'] not in STATES or type(value['ids']) is not list or len(value['ids']) > MAX_IDS:
        raise Refusal('RESPONSE_SHAPE')
    ids = value['ids']
    if any(type(sid) is not str or sid not in binding['selectableFocalIds'] for sid in ids):
        raise Refusal('FOREIGN_OR_CONTEXT_ID')
    if len(ids) != len(set(ids)):
        raise Refusal('DUPLICATE_ID')
    if value['state'] == 'EMPTY':
        # No acquisition emptiness proof exists in this study. A model claim,
        # omitted role label, missing field, or no OCR IDs can never create it.
        raise Refusal('EMPTY_NOT_SOURCE_PROVEN')
    if (value['state'] == 'PRESENT') != bool(ids):
        raise Refusal('PRESENT_OR_ABSTENTION_ID_CONSISTENCY')
    selected = set(ids)
    ordered = [s for s in task['sources'] if s['id'] in selected]
    return {'state': value['state'], 'ids': [s['id'] for s in ordered],
            'value': ''.join(s['text'] for s in ordered), 'assignmentCertificate': 'ABSENT',
            'roleCorrectness': 'UNASSESSED', 'productionAdoption': False}


def prepare_plan(tasks):
    if (type(tasks) is not list or len(tasks) != 12 or
            any(type(t) is not dict or type(t.get('id')) is not str or not 0 < len(t['id']) <= 128 for t in tasks) or
            len({t['id'] for t in tasks}) != len(tasks)):
        raise Refusal('FROZEN_SELECTION_INVENTORY')
    ranked = sorted(tasks, key=lambda t: fingerprint(SELECTION_SEED + t['id']))
    eligible, refusals = [], []
    # Explicit source-eligible study selection, before any engine or evaluator;
    # no backfill after a selected task/model call fails.
    for task in ranked:
        try:
            checked_source(task)
            eligible.append(task)
        except Refusal as exc:
            refusals.append({'taskID': task['id'], 'reason': str(exc)})
    selected = eligible[:MAX_TASKS]
    if len(selected) != MAX_TASKS:
        raise Refusal('FOUR_SOURCE_ELIGIBLE_CASES_REQUIRED')
    records = []
    for i, task in enumerate(selected):
        binding = caller_binding(task, FIELDS[i % len(FIELDS)])
        spec = specification(task, binding)
        records.append({'binding': binding, 'promptSHA256': spec['promptSHA256'],
                        'semanticSchemaSHA256': spec['semanticSchemaSHA256']})
    return {'version': 1, 'purpose': PURPOSE, 'conditions': list(CONDITIONS), 'selectionSeed': SELECTION_SEED,
            'selection': 'First four source-eligible tasks by SHA256(canonical seed+opaque ID); fields cycle subject/teacher/room independent of text; no post-call replacement',
            'records': records, 'preselectionSourceRefusals': refusals, 'maximumCallsPerModel': MAX_CALLS_PER_MODEL,
            'maximumTotalCalls': 8, 'originalInkOwnershipProof': 'ABSENT', 'roleAssignmentProof': 'ABSENT',
            'qualifiedGenAIModels': [], 'productionAdoption': False, 'wholeDocumentQuality': 'UNASSESSED',
            'rootExecutionGo': False}


def execute_plan(plan, tasks, condition, fresh_call):
    if condition not in CONDITIONS or plan != prepare_plan(tasks):
        raise Refusal('FROZEN_PLAN_SOURCE_OR_CONDITION_REWRITE')
    by_id = {t['id']: t for t in tasks}
    rows = []
    for i, record in enumerate(plan['records'], 1):
        binding = record['binding']
        task = by_id[binding['taskID']]
        spec = specification(task, binding)
        if spec['promptSHA256'] != record['promptSHA256'] or spec['semanticSchemaSHA256'] != record['semanticSchemaSHA256']:
            raise Refusal('EXACT_REQUEST_PIN_MISMATCH')
        rows.append(fresh_call({**spec, 'call': i, 'condition': condition}))
    return {'calls': len(rows), 'rows': rows, 'productionAdoption': False}
