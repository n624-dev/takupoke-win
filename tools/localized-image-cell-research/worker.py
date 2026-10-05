"""Guarded actual image inference. OCR payload is opened only after blind freeze."""
import json
import os
from pathlib import Path
import sys
import time
from guard import resources,verify_packet,bind_runtime
from image_protocol import (BLIND_INSTRUCTION,BLIND_SCHEMA,decode_blind,validate_image,execute_image_first)
from native_grammar import adapt
from protocol import SYSTEM_MESSAGE,context_capacity,response_capacity_failure,digest
from contract import decode
ROOT=Path(__file__).resolve().parent


def main():
    recipe=json.loads((ROOT/'recipe.json').read_text())
    identity=verify_packet(ROOT,recipe,pin_role='worker')
    if len(sys.argv)!=2 or sys.argv[1]!=identity['sourceFreezeSHA256'] or not (ROOT/'probe-started.json').is_file():
        raise RuntimeError('GUARDED_RUNNER_REQUIRED')
    recipe=bind_runtime(ROOT,recipe);resources(ROOT,recipe)
    meta=json.loads((ROOT/'image-input.json').read_text());crop=validate_image(ROOT,meta,recipe)
    calls=0
    def emit(event,**fields):
        with (ROOT/'worker-events.jsonl').open('a',encoding='utf-8') as s:
            s.write(json.dumps({'event':event,'monotonic':time.monotonic(),**fields},ensure_ascii=False)+'\n');s.flush()
    cache=ROOT/'owned-runtime-cache';cache.mkdir(exist_ok=True)
    emit('loadStarted')
    from network_guard import install,parent_death_kill
    parent_death_kill(json.loads((ROOT/'probe-started.json').read_text())['runnerPID'])
    emit('networkGuardReady',guard=install())
    from litert_lm import (Backend,Capabilities,Content,Contents,ConstrainedDecodingConfig,Engine,
                          LiteRtLmConstraintProviderType,ResponseFormat,SamplerConfig,ThinkingConfig)
    with Capabilities(recipe['modelPath']) as cap:
        modalities=cap.input_modalities
        emit('modelCapabilities',text=modalities.text,vision=modalities.vision,maxVisionTokens=cap.max_vision_token_budget)
        if not modalities.vision or cap.max_vision_token_budget!=280:
            raise RuntimeError('IMAGE_UNSUPPORTED_OR_PINNED_VISION_BUDGET_MISMATCH_NO_FALLBACK')
    engine=Engine(recipe['modelPath'],backend=Backend.CPU(thread_count=2),vision_backend=Backend.CPU(thread_count=2),
                  max_num_tokens=8192,max_num_images=1,cache_dir=str(cache))
    emit('loadComplete')

    def call(stage,prompt,schema,spec=None,blind_frozen=None):
        nonlocal calls
        calls+=1
        if calls>2:raise RuntimeError('TWO_CALL_CAP')
        resources(ROOT,{**recipe,'minimumAvailableMemoryBytes':0,'workingAllowanceBytes':0})
        image=stage=='blind-image'
        begin=time.monotonic()
        emit('callStarted',call=calls,taskID=meta['taskID'],stage=stage)
        native_schema,transport=adapt(schema)
        row={'call':calls,'condition':recipe['comparisonCondition'],'taskID':meta['taskID'],'stage':stage,
             'promptSHA256':__import__('hashlib').sha256(prompt.encode()).hexdigest(),'cropSHA256':meta['cropSHA256'] if image else None,
             'blindFrozenSHA256':blind_frozen,'raw':None,'candidate':None,'error':None,'completeResponse':None,**transport}
        emit('requestSpecification',call=calls,stage=stage,prompt=prompt,semanticSchema=schema,nativeSchema=native_schema,
             imageCount=1 if image else 0,blindFrozenSHA256=blind_frozen)
        try:
            row['contextCapacity']=context_capacity(engine.tokenize,prompt,native_schema,recipe,image)
            if not row['contextCapacity']['passed']:raise RuntimeError('CONSERVATIVE_CONTEXT_CAPACITY_NO_SEND')
            with engine.create_conversation(system_message=SYSTEM_MESSAGE,
                thinking_config=ThinkingConfig(enable_thinking=False,thinking_token_budget=-1),
                sampler_config=SamplerConfig(top_k=1,temperature=0,seed=17),max_output_tokens=256,
                automatic_tool_calling=False,tools=[],constrained_decoding_config=ConstrainedDecodingConfig(
                    enable=True,provider=LiteRtLmConstraintProviderType.LL_GUIDANCE)) as conv:
                content=Contents.of(prompt,Content.ImageFile(str(crop))) if image else prompt
                response=conv.send_message(content,response_format=ResponseFormat.json(native_schema));row['completeResponse']=response
                try:
                    from dataclasses import asdict
                    row['benchmarkInfo']=asdict(conv.get_benchmark_info())
                except Exception as exc:row['benchmarkInfoUnavailable']=type(exc).__name__+':'+str(exc)
            row['raw']=''.join(c.get('text','') for c in response.get('content',[]))
            capacity=response_capacity_failure(response,row.get('benchmarkInfo',{}).get('last_decode_token_count'),256)
            if capacity:raise RuntimeError('OPERATIONAL_RESPONSE_CAPACITY:'+capacity)
            try:
                row['candidate']=decode_blind(row['raw']) if image else decode(row['raw'],spec['task'],spec['binding'])
                row['disposition']=row['candidate']['state']
            except (ValueError,TypeError,KeyError) as exc:
                row.update(disposition='SCHEMA_REJECTED',error=type(exc).__name__+':'+str(exc))
        except Exception as exc:
            row.update(disposition='OPERATIONAL_UNASSESSED',error=type(exc).__name__+':'+str(exc))
        row['seconds']=time.monotonic()-begin
        with (ROOT/'responses.jsonl').open('a',encoding='utf-8') as s:
            s.write(json.dumps(row,ensure_ascii=False)+'\n');s.flush();os.fsync(s.fileno())
        emit('callComplete',call=calls,taskID=meta['taskID'],stage=stage,disposition=row['disposition'])
        return row

    def freeze(row):
        # Complete native response + candidate and image/prompt identity, before OCR.
        with (ROOT/'blind-result-frozen.json').open('x',encoding='utf-8') as s:
            s.write(json.dumps(row,ensure_ascii=False)+'\n');s.flush();os.fsync(s.fileno())
        sha=digest(ROOT/'blind-result-frozen.json');emit('blindResultFrozen',sha256=sha);return sha

    def load_ocr():
        freeze=json.loads((ROOT/'packet-freeze.json').read_text())
        pin=next(p for p in freeze['pins'] if p['path']=='inputs.json')
        if digest(ROOT/'inputs.json')!=pin['sha256']:raise RuntimeError('OCR_PIN_CHANGED')
        payload=json.loads((ROOT/'inputs.json').read_text());tasks=payload['tasks']
        if len(tasks)!=1 or payload['nativeGate'] is not False or tasks[0]['id']!=meta['taskID']:raise RuntimeError('EXACT_ONE_OCR_CELL_REQUIRED')
        emit('ocrInputOpenedAfterBlindFreeze',inputSHA256=pin['sha256'])
        return tasks[0]
    try:
        result=execute_image_first(lambda:call('blind-image',BLIND_INSTRUCTION,BLIND_SCHEMA),freeze,load_ocr,
            lambda spec,sha:call('ocr-compare',spec['prompt'],spec['schema'],spec,sha))
    finally:engine.close()
    emit('workerComplete',attemptedCalls=calls,modelLoads=1,newTextBaselineCalls=0,productionAdoption=False,
         comparisonSkipped=result['comparison']=='UNASSESSED_NO_USABLE_IMAGE')
    rows=[json.loads(s) for s in (ROOT/'responses.jsonl').read_text().splitlines()]
    if any(r['disposition']=='OPERATIONAL_UNASSESSED' for r in rows):raise SystemExit(1)

if __name__=='__main__':main()
