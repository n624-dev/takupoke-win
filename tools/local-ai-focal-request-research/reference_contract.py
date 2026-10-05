"""Pinned pure 454 reference and new request specification; never loads SDK."""
import ast
import hashlib
import json
from pathlib import Path
import types
from request_contract import Refusal, caller_intent, prompt, schema, request

REFERENCE = Path(__file__).resolve().parent.parent / 'local-ai-three-arm-research'
PINS = {'protocol.py': 'e27ec1322faf2486e3d990ac088f01dfe84155f19285030702c062b36e21b408',
        'comparison.py': '4dde661659d0b719aeac69e02de0c0e10f6f7e34bf095b65f6e86aff06698d85'}


def pinned_reference():
    contents = {}
    for name, expected in PINS.items():
        data = (REFERENCE / name).read_bytes()
        if len(data) > 32768 or hashlib.sha256(data).hexdigest() != expected:
            raise Refusal('REFERENCE_SOURCE_PIN')
        contents[name] = data
    # This verified source imports only hashlib/json/math. Never import a
    # provider/runtime/native tokenizer. No sys.path or public module shadowing.
    protocol = types.ModuleType('_frozen_454_protocol')
    # Compile the same verified bytes, avoiding a hash-then-second-file-read gap.
    exec(compile(contents['protocol.py'], str(REFERENCE / 'protocol.py'), 'exec'), protocol.__dict__)
    node = ast.parse(contents['comparison.py'])
    assignments = [n for n in node.body if isinstance(n, ast.Assign)
                   and any(isinstance(t, ast.Name) and t.id == 'CLARIFIED_INSTRUCTION' for t in n.targets)]
    if len(assignments) != 1:
        raise Refusal('REFERENCE_INSTRUCTION')
    return protocol, ast.literal_eval(assignments[0].value)


def specification(task, profile, stage, diagnostic):
    # Uniform source gate/purpose for BOTH matched profiles, before generation.
    intent = caller_intent(task)
    request(task, intent)
    if profile == 'FOCAL_BODY_V1':
        text = prompt(task, intent, stage, diagnostic)
        response_schema = schema(task, stage)
    elif profile == 'REFERENCE_454':
        protocol, instruction = pinned_reference()
        text = instruction + '\n原文データ:\n' + json.dumps(task['promptInput'], ensure_ascii=False, separators=(',', ':'))
        if stage == 'compare':
            text += '\n別の盲検画像転記候補(原文IDの証拠ではない):\n' + json.dumps(diagnostic, ensure_ascii=False)
            text += '\n元の原文IDだけを選び、転記との比較をAGREE/DISAGREE/UNKNOWNで示してください。転記で原文を書き換えないでください。'
        elif stage != 'text' or diagnostic is not None:
            raise Refusal('REFERENCE_STAGE')
        response_schema = protocol.id_schema(task)
    else:
        raise Refusal('PROFILE')
    return {'profile': profile, 'stage': stage, 'taskID': task['id'],
            'prompt': text, 'promptSHA256': hashlib.sha256(text.encode('utf-8')).hexdigest(),
            'schema': response_schema,
            'nativeSchemaCompactSHA256': hashlib.sha256(json.dumps(response_schema, ensure_ascii=False, separators=(',', ':')).encode('utf-8')).hexdigest(),
            'freshConversationRequired': True, 'image': False,
            'diagnosticMeaning': 'PREVIOUS_CONSUMED_DIAGNOSTIC_ONLY' if stage == 'compare' else 'NONE',
            'productionMode': False, 'productionAdoption': False}
