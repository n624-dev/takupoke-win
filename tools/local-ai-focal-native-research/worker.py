"""Guarded text-only focal proposal worker; never opens oracle/evaluator."""
import hashlib
import json
from pathlib import Path
import sys
import time
from protocol import SYSTEM_MESSAGE,context_capacity,response_capacity_failure,digest
from guard import resources,verify_packet,bind_runtime
from request_contract import execute_plan,decode
from reference_contract import pinned_reference
ROOT=Path(__file__).resolve().parent
PROVENANCE_KEYS=('retainedInputSHA256','retainedBlindResponsesSHA256','retainedBlindMeaning')
def core_plan(plan):return {k:v for k,v in plan.items() if k not in PROVENANCE_KEYS}
def main():
    recipe=json.loads((ROOT/'recipe.json').read_text());identity=verify_packet(ROOT,recipe,pin_role='worker')
    if len(sys.argv)!=2 or sys.argv[1]!=identity['sourceFreezeSHA256'] or not (ROOT/'probe-started.json').is_file():raise RuntimeError('GUARDED_RUNNER_REQUIRED')
    recipe=bind_runtime(ROOT,recipe);resources(ROOT,recipe)
    tasks=json.loads((ROOT/'inputs.json').read_text())['tasks'];plan=json.loads((ROOT/'caller-plan.json').read_text())
    if digest(ROOT/'inputs.json')!=plan['retainedInputSHA256'] or len(tasks)!=12 or plan['sourceEligibleMaximumCalls']!=8:raise RuntimeError('FROZEN_FOCAL_INPUT_SCOPE')
    def emit(event,**fields):
        with (ROOT/'worker-events.jsonl').open('a') as stream:stream.write(json.dumps({'event':event,'monotonic':time.monotonic(),**fields},ensure_ascii=False)+'\n')
    # Rebuild specifications before import without any evaluator dependency.
    reference,_=pinned_reference();cache=ROOT/'owned-runtime-cache';cache.mkdir(exist_ok=True)
    emit('loadStarted')
    from network_guard import install,parent_death_kill
    parent_death_kill(json.loads((ROOT/'probe-started.json').read_text())['runnerPID']);emit('networkGuardReady',guard=install())
    from litert_lm import Backend,ConstrainedDecodingConfig,Engine,LiteRtLmConstraintProviderType,ResponseFormat,SamplerConfig,ThinkingConfig
    engine=Engine(recipe['modelPath'],backend=Backend.CPU(thread_count=2),max_num_tokens=8192,max_num_images=0,cache_dir=str(cache));emit('loadComplete')
    operational=0
    def call(spec):
        nonlocal operational
        resources(ROOT,{**recipe,'minimumAvailableMemoryBytes':0,'workingAllowanceBytes':0})
        emit('callStarted',call=spec['call'],taskID=spec['taskID'],stage=spec['stage'],profile=spec['profile'])
        begin=time.monotonic();row={k:spec[k] for k in ('profile','stage','taskID','call','promptSHA256','nativeSchemaCompactSHA256')}
        row.update(raw=None,completeResponse=None,candidate=None,error=None)
        # Save exact sent request/schema independently; no inferred prompt reconstruction.
        with (ROOT/'worker-events.jsonl').open('a') as stream:stream.write(json.dumps({'event':'requestSpecification','monotonic':time.monotonic(),'call':spec['call'],'profile':spec['profile'],'taskID':spec['taskID'],'stage':spec['stage'],'prompt':spec['prompt'],'schema':spec['schema']},ensure_ascii=False)+'\n')
        try:
            row['contextCapacity']=context_capacity(engine.tokenize,spec['prompt'],spec['schema'],recipe)
            if not row['contextCapacity']['passed']:raise RuntimeError('CONSERVATIVE_CONTEXT_CAPACITY_NO_SEND')
            with engine.create_conversation(system_message=SYSTEM_MESSAGE,thinking_config=ThinkingConfig(enable_thinking=False,thinking_token_budget=-1),sampler_config=SamplerConfig(top_k=1,temperature=0,seed=17),max_output_tokens=768,automatic_tool_calling=False,tools=[],constrained_decoding_config=ConstrainedDecodingConfig(enable=True,provider=LiteRtLmConstraintProviderType.LL_GUIDANCE)) as conv:
                response=conv.send_message(spec['prompt'],response_format=ResponseFormat.json(spec['schema']));row['completeResponse']=response
                try:
                    from dataclasses import asdict
                    row['benchmarkInfo']=asdict(conv.get_benchmark_info())
                except Exception as exc:row['benchmarkInfoUnavailable']=str(exc)
            row['raw']=''.join(c.get('text','') for c in response.get('content',[]))
            capacity=response_capacity_failure(response,row.get('benchmarkInfo',{}).get('last_decode_token_count'),768)
            if capacity:raise RuntimeError('OPERATIONAL_RESPONSE_CAPACITY:'+capacity)
            try:
                row['candidate']=decode(row['raw'],spec['task'],spec['stage']) if spec['profile']=='FOCAL_BODY_V1' else reference.decode_candidate(row['raw'],spec['task'],spec['stage']=='compare')
                row['disposition']=row['candidate']['state']
            except (ValueError,TypeError,KeyError) as exc:row.update(disposition='SCHEMA_REJECTED',error=type(exc).__name__+':'+str(exc))
        except Exception as exc:row.update(disposition='OPERATIONAL_UNASSESSED',error=type(exc).__name__+':'+str(exc));operational+=1
        row['seconds']=time.monotonic()-begin
        with (ROOT/'responses.jsonl').open('a') as stream:stream.write(json.dumps(row,ensure_ascii=False)+'\n')
        emit('callComplete',call=spec['call'],taskID=spec['taskID'],stage=spec['stage'],profile=spec['profile'],disposition=row['disposition'])
        if row['disposition']=='OPERATIONAL_UNASSESSED':raise RuntimeError('STOP_AFTER_OPERATIONAL_FAILURE_NO_RETRY')
        return row
    try:result=execute_plan(core_plan(plan),tasks,call)
    finally:engine.close()
    emit('workerComplete',attemptedCalls=result['calls'],operationalErrors=operational,modelLoads=1,newImageCalls=0,newRecognizerCalls=0,productionAdoption=False)
if __name__=='__main__':main()
