"""Code groups retained native rows; model selects one small candidate only."""
from copy import deepcopy
import hashlib
import json
from pathlib import Path
from acquisition import checked_source
from source_contract import Refusal, canonical, fingerprint, _box, _contains, _exact_keys, MAX_RESPONSE_BYTES

FIELDS = ('subject', 'teacher', 'room')
CONDITIONS = ('gemma-row-choice', 'qwen-row-choice')
PURPOSE = 'ROW_CANDIDATE_ID_PROPOSAL'
TARGET_TASK = 't000466d2ba598e3e'
TARGET_FIELD = 'subject'
MAX_CALLS_PER_MODEL = 1
INSTRUCTION = (Path(__file__).parent / 'prompt.txt').read_text(encoding='utf-8')


def _envelope(sources):
    boxes = [_box(s['box'], 'XYWH') for s in sources]
    return [min(b[0] for b in boxes), min(b[1] for b in boxes),
            max(b[2] for b in boxes), max(b[3] for b in boxes)]


def candidate_map(task):
    checked_source(task)
    chunks = {}
    for source in task['sources']:
        if source['owner'] == 'cell':
            chunks.setdefault(source['chunkID'], []).append(source)
    rows = {r['id']: r for r in task['acquisitionRows']}
    out = []
    for index, (chunk, sources) in enumerate(chunks.items()):
        if len({s['sourceLine'] for s in sources}) != 1:
            raise Refusal('NATIVE_CHUNK_MULTIPLE_SOURCE_LINES')
        text = ''.join(s['text'] for s in sources)
        if text != rows[chunk]['rawText']:
            raise Refusal('NATIVE_ROW_TEXT_REWRITE')
        out.append({'candidateID': 'r'+str(index), 'nativeChunkID': chunk, 'text': text,
                    'originalIDs': [s['id'] for s in sources], 'estimatedSourceEnvelopeLTRB': _envelope(sources)})
    if not 1 <= len(out) <= 8:
        raise Refusal('BOUNDED_NATIVE_ROWS_REQUIRED')
    return out


def _contexts(task):
    # Boundaries come only from supplied native chunkID and supplied closed
    # owner boxes. No text segmentation, new ownership or semantic labels.
    groups = {}
    for source in task['sources']:
        if source['owner'] != 'context':
            continue
        if type(source['chunkID']) is not int:
            raise Refusal('CONTEXT_NATIVE_CHUNK_ID_MISSING')
        groups.setdefault(source['chunkID'], []).append(source)
    focal = _box(task['promptInput']['cellBox'], 'LTRB')
    boxes = [_box(b, 'LTRB') for b in task['promptInput']['surroundingClosedBoxes']]
    out = []
    for members in groups.values():
        owners = [i for i, b in enumerate(boxes) if all(_contains(b, _box(s['box'], 'XYWH')) for s in members)]
        if len(owners) != 1:
            raise Refusal('CONTEXT_CHUNK_SUPPLIED_OWNER_NOT_UNIQUE')
        b = boxes[owners[0]]
        position = []
        if b[3] <= focal[1]: position.append('ABOVE_FOCAL')
        if b[1] >= focal[3]: position.append('BELOW_FOCAL')
        if b[2] <= focal[0]: position.append('LEFT_OF_FOCAL')
        if b[0] >= focal[2]: position.append('RIGHT_OF_FOCAL')
        out.append({'text': ''.join(s['text'] for s in members), 'relativePosition': position})
    return out


def caller_binding(task, field):
    if field not in FIELDS:
        raise Refusal('CALLER_REQUESTED_FIELD_REQUIRED')
    candidates = candidate_map(task)
    return {'taskID': task['id'], 'requestedField': field, 'sourceSnapshotSHA256': fingerprint(task),
            'candidateInventorySHA256': fingerprint(candidates), 'assignmentCertificate': 'ABSENT'}


def request(task, binding):
    _exact_keys(binding, ('taskID', 'requestedField', 'sourceSnapshotSHA256', 'candidateInventorySHA256',
                          'assignmentCertificate'), 'CALLER_BINDING_KEYS')
    if binding != caller_binding(task, binding['requestedField']):
        raise Refusal('CALLER_SOURCE_OR_CANDIDATE_REWRITE')
    candidates = candidate_map(task)
    relationships = []
    for a in candidates:
        for b in candidates:
            if a['candidateID'] != b['candidateID'] and a['estimatedSourceEnvelopeLTRB'][3] <= b['estimatedSourceEnvelopeLTRB'][1]:
                relationships.append({'above': a['candidateID'], 'below': b['candidateID']})
    # Keep characters, original IDs, floats, lineage and proof metadata solely
    # in the caller/Validator map. Model gets short native text rows and hints.
    return {'candidates': [{'candidateID': c['candidateID'], 'text': c['text']} for c in candidates],
            'relativeRows': relationships, 'readOnlyContext': _contexts(task)}


def schema(task, binding):
    request(task, binding)
    ids = [c['candidateID'] for c in candidate_map(task)]
    return {'type': 'object', 'additionalProperties': False, 'required': ['state', 'candidateID'],
            'properties': {'state': {'type': 'string', 'enum': ['PRESENT', 'UNKNOWN']},
                           'candidateID': {'type': 'string', 'enum': ids + ['NONE']}}}


def specification(task, binding):
    payload = request(task, binding)
    prompt = INSTRUCTION + '\nCALLER_REQUESTED_FIELD: ' + binding['requestedField'] + '\nSOURCE_ROWS:\n' + canonical(payload).decode('utf-8')
    semantic = schema(task, binding)
    return {'taskID': task['id'], 'requestedField': binding['requestedField'], 'prompt': prompt,
            'promptSHA256': hashlib.sha256(prompt.encode()).hexdigest(), 'schema': semantic,
            'semanticSchemaSHA256': fingerprint(semantic), 'task': deepcopy(task), 'binding': deepcopy(binding),
            'candidateMap': candidate_map(task), 'freshConversationRequired': True}


def decode(raw, task, binding):
    request(task, binding)
    if type(raw) is not str or len(raw.encode()) > MAX_RESPONSE_BYTES:
        raise Refusal('RESPONSE_LIMIT')
    def pairs(items):
        out = {}
        for key, value in items:
            if key in out: raise Refusal('DUPLICATE_PROPERTY')
            out[key] = value
        return out
    value = json.loads(raw, object_pairs_hook=pairs,
                       parse_constant=lambda _: (_ for _ in ()).throw(Refusal('NONFINITE_JSON')))
    _exact_keys(value, ('state', 'candidateID'), 'RESPONSE_KEYS')
    if value['state'] not in ('PRESENT', 'UNKNOWN') or type(value['candidateID']) is not str:
        raise Refusal('RESPONSE_SHAPE')
    candidates = {c['candidateID']: c for c in candidate_map(task)}
    if value['state'] == 'UNKNOWN':
        if value['candidateID'] != 'NONE': raise Refusal('ABSTENTION_WITH_CANDIDATE')
        selected = []
    else:
        if value['candidateID'] not in candidates: raise Refusal('FOREIGN_CONTEXT_OR_MISSING_CANDIDATE')
        members = set(candidates[value['candidateID']]['originalIDs'])
        selected = [s for s in task['sources'] if s['id'] in members]
    return {'state': value['state'], 'candidateID': value['candidateID'], 'ids': [s['id'] for s in selected],
            'value': ''.join(s['text'] for s in selected), 'assignmentCertificate': 'ABSENT',
            'roleCorrectness': 'UNASSESSED', 'productionAdoption': False}


def prepare_plan(tasks):
    if (type(tasks) is not list or len(tasks) != 12 or any(type(t) is not dict or type(t.get('id')) is not str for t in tasks) or
            len({t['id'] for t in tasks}) != len(tasks)):
        raise Refusal('FROZEN_SOURCE_INVENTORY')
    matches = [t for t in tasks if t['id'] == TARGET_TASK]
    if len(matches) != 1: raise Refusal('EXPLICIT_CALLER_TARGET_REQUIRED')
    task = matches[0]
    binding = caller_binding(task, TARGET_FIELD)
    spec = specification(task, binding)
    return {'version': 2, 'purpose': PURPOSE, 'conditions': list(CONDITIONS),
            'selection': 'Explicit root-chosen consumed development target after actual37383946385; no oracle role used in candidate construction',
            'records': [{'binding': binding, 'promptSHA256': spec['promptSHA256'], 'semanticSchemaSHA256': spec['semanticSchemaSHA256']}],
            'maximumCallsPerModel': 1, 'maximumTotalCalls': 2, 'originalInkOwnershipProof': 'ABSENT',
            'roleAssignmentProof': 'ABSENT', 'qualifiedGenAIModels': [], 'productionAdoption': False,
            'wholeDocumentQuality': 'UNASSESSED', 'rootExecutionGo': False}


def execute_plan(plan, tasks, condition, fresh_call):
    if condition not in CONDITIONS or plan != prepare_plan(tasks):
        raise Refusal('FROZEN_PLAN_SOURCE_OR_CONDITION_REWRITE')
    task = next(t for t in tasks if t['id'] == TARGET_TASK)
    binding = plan['records'][0]['binding']
    spec = specification(task, binding)
    row = fresh_call({**spec, 'call': 1, 'condition': condition})
    return {'calls': 1, 'rows': [row], 'productionAdoption': False}
