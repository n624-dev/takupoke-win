"""Guarded text-only single-field worker; never reads an evaluator or oracle."""
import json
from pathlib import Path
import sys
import time
from contract import execute_plan, decode
from guard import resources, verify_packet, bind_runtime
from native_grammar import adapt
from protocol import SYSTEM_MESSAGE, context_capacity, response_capacity_failure

ROOT = Path(__file__).resolve().parent


def main():
    recipe = json.loads((ROOT / 'recipe.json').read_text())
    identity = verify_packet(ROOT, recipe, pin_role='worker')
    if len(sys.argv) != 2 or sys.argv[1] != identity['sourceFreezeSHA256'] or not (ROOT / 'probe-started.json').is_file():
        raise RuntimeError('GUARDED_RUNNER_REQUIRED')
    recipe = bind_runtime(ROOT, recipe)
    resources(ROOT, recipe)
    tasks = json.loads((ROOT / 'inputs.json').read_text())['tasks']
    plan = json.loads((ROOT / 'caller-plan.json').read_text())

    def emit(event, **fields):
        with (ROOT / 'worker-events.jsonl').open('a', encoding='utf-8') as stream:
            stream.write(json.dumps({'event': event, 'monotonic': time.monotonic(), **fields}, ensure_ascii=False) + '\n')

    # Validate/reconstruct the complete plan before native imports or loading.
    execute_plan(plan, tasks, recipe['comparisonCondition'], lambda spec: None)
    cache = ROOT / 'owned-runtime-cache'
    cache.mkdir(exist_ok=True)
    emit('loadStarted')
    from network_guard import install, parent_death_kill
    parent_death_kill(json.loads((ROOT / 'probe-started.json').read_text())['runnerPID'])
    emit('networkGuardReady', guard=install())
    from litert_lm import (Backend, ConstrainedDecodingConfig, Engine, LiteRtLmConstraintProviderType,
                           ResponseFormat, SamplerConfig, ThinkingConfig)
    engine = Engine(recipe['modelPath'], backend=Backend.CPU(thread_count=2), max_num_tokens=8192,
                    max_num_images=0, cache_dir=str(cache))
    emit('loadComplete')

    def call(spec):
        resources(ROOT, {**recipe, 'minimumAvailableMemoryBytes': 0, 'workingAllowanceBytes': 0})
        emit('callStarted', call=spec['call'], taskID=spec['taskID'], requestedField=spec['requestedField'])
        begin = time.monotonic()
        row = {k: spec[k] for k in ('call', 'condition', 'taskID', 'requestedField', 'promptSHA256')}
        row.update(raw=None, completeResponse=None, candidate=None, error=None)
        native_schema, transport = adapt(spec['schema'])
        row.update(transport)
        emit('requestSpecification', call=spec['call'], taskID=spec['taskID'], requestedField=spec['requestedField'],
             prompt=spec['prompt'], semanticSchema=spec['schema'], nativeSchema=native_schema, nativeTransport=transport,
             validatorCandidateMap=spec['candidateMap'], sourceSnapshotSHA256=spec['binding']['sourceSnapshotSHA256'])
        try:
            emit('tokenizeStarted', call=spec['call'])
            row['contextCapacity'] = context_capacity(engine.tokenize, spec['prompt'], native_schema, recipe)
            emit('tokenizeComplete', call=spec['call'], contextCapacity=row['contextCapacity'])
            if not row['contextCapacity']['passed']:
                raise RuntimeError('CONSERVATIVE_CONTEXT_CAPACITY_NO_SEND')
            emit('conversationCreateStarted', call=spec['call'])
            with engine.create_conversation(system_message=SYSTEM_MESSAGE,
                    thinking_config=ThinkingConfig(enable_thinking=False, thinking_token_budget=-1),
                    sampler_config=SamplerConfig(top_k=1, temperature=0, seed=17), max_output_tokens=256,
                    automatic_tool_calling=False, tools=[], constrained_decoding_config=ConstrainedDecodingConfig(
                        enable=True, provider=LiteRtLmConstraintProviderType.LL_GUIDANCE)) as conv:
                emit('conversationReady', call=spec['call'])
                emit('nativeSendStarted', call=spec['call'])
                response = conv.send_message(spec['prompt'], response_format=ResponseFormat.json(native_schema))
                emit('nativeSendComplete', call=spec['call'])
                row['completeResponse'] = response
                try:
                    from dataclasses import asdict
                    row['benchmarkInfo'] = asdict(conv.get_benchmark_info())
                except Exception as exc:
                    row['benchmarkInfoUnavailable'] = str(exc)
            row['raw'] = ''.join(c.get('text', '') for c in response.get('content', []))
            capacity = response_capacity_failure(response, row.get('benchmarkInfo', {}).get('last_decode_token_count'), 256)
            if capacity:
                raise RuntimeError('OPERATIONAL_RESPONSE_CAPACITY:' + capacity)
            try:
                row['candidate'] = decode(row['raw'], spec['task'], spec['binding'])
                row['disposition'] = row['candidate']['state']
            except (ValueError, TypeError, KeyError) as exc:
                row.update(disposition='SCHEMA_REJECTED', error=type(exc).__name__ + ':' + str(exc))
        except Exception as exc:
            row.update(disposition='OPERATIONAL_UNASSESSED', error=type(exc).__name__ + ':' + str(exc))
        row['seconds'] = time.monotonic() - begin
        with (ROOT / 'responses.jsonl').open('a', encoding='utf-8') as stream:
            stream.write(json.dumps(row, ensure_ascii=False) + '\n')
        emit('callComplete', call=spec['call'], taskID=spec['taskID'], disposition=row['disposition'])
        if row['disposition'] == 'OPERATIONAL_UNASSESSED':
            raise RuntimeError('STOP_AFTER_OPERATIONAL_FAILURE_NO_RETRY')
        return row

    try:
        result = execute_plan(plan, tasks, recipe['comparisonCondition'], call)
    finally:
        engine.close()
    emit('workerComplete', attemptedCalls=result['calls'], modelLoads=1, productionAdoption=False)


if __name__ == '__main__':
    main()
