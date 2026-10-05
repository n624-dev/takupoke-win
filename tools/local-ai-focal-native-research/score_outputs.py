"""Separate evaluator using only original complete tuples, never role/mode/count labels."""
import json
from pathlib import Path
from request_contract import PROFILES,STAGES,select_tasks,decode
from reference_contract import pinned_reference
from protocol import digest
ROOT=Path(__file__).resolve().parent
def assess(tasks,plan,oracle,records):
    selected=select_tasks(tasks);expected={(p,t['id'],s) for p in PROFILES for t in selected for s in STAGES}
    by={};invalid=[]
    for row in records:
        key=(row.get('profile'),row.get('taskID'),row.get('stage')) if type(row) is dict else None
        if key not in expected:invalid.append('UNKNOWN_COMPLETION_IDENTITY');continue
        if key in by:by[key]={'disposition':'OPERATIONAL_UNASSESSED','error':'DUPLICATE_COMPLETION_NO_SELECTION'};invalid.append('DUPLICATE_COMPLETION');continue
        by[key]=row
    truth={t['id']:t.get('expectedTuples') for t in oracle['tasks']}
    eligibility={r['taskID']:r for r in plan['records']};reference,_=pinned_reference();rows=[]
    for profile in PROFILES:
        for task in selected:
            for stage in STAGES:
                out={'profile':profile,'taskID':task['id'],'stage':stage,'state':'UNASSESSED','tupleBindingExact':None,'productionAdoption':False,'formalAccepted':False}
                row=by.get((profile,task['id'],stage));gate=eligibility[task['id']]
                if gate['eligibility']=='REFUSED_BEFORE_MODEL':
                    out.update(state='SOURCE_REFUSED_UNASSESSED',sourceRefusal=gate['refusal'])
                    if row is not None:invalid.append('COMPLETION_AFTER_SOURCE_REFUSAL')
                elif row is None:out['operationalError']='EXPECTED_COMPLETION_NOT_RETURNED'
                elif row.get('disposition')=='OPERATIONAL_UNASSESSED':out['operationalError']=row.get('error')
                else:
                    try:
                        candidate=decode(row.get('raw'),task,stage) if profile=='FOCAL_BODY_V1' else reference.decode_candidate(row.get('raw'),task,stage=='compare')
                        out.update(state=candidate['state'],candidate=candidate)
                        if candidate['state']=='CANDIDATE':
                            source={s['id']:s for s in task['sources']}
                            tuples=[{field:''.join(source[sid]['text'] for sid in lesson[field]) for field in ('subject','teacher','room')} for lesson in candidate['lessons']]
                            out['originalIDTuples']=tuples
                            if truth[task['id']] is not None:out['tupleBindingExact']=tuples==truth[task['id']]
                            out['sourceProofDisposition']='REFUSED_ABSENT_PRODUCTION_ASSIGNMENT_CERTIFICATE'
                        else:out['abstentionIsNotEmptyProof']=True
                    except (ValueError,TypeError,KeyError) as exc:out.update(state='SCHEMA_REJECTED',schemaError=str(exc))
                rows.append(out)
    states=sorted({r['state'] for r in rows})
    summary={profile:{stage:{'expected':4,'states':{state:sum(r['profile']==profile and r['stage']==stage and r['state']==state for r in rows) for state in states},'tupleBindingExact':sum(r['profile']==profile and r['stage']==stage and r['tupleBindingExact'] is True for r in rows),'tupleBindingWrong':sum(r['profile']==profile and r['stage']==stage and r['tupleBindingExact'] is False for r in rows)} for stage in STAGES} for profile in PROFILES}
    return {'scope':'CONSUMED_FICTIONAL_DEVELOPMENT_REQUEST_SCHEMA_BUNDLE_COMPARISON','rows':rows,'summary':summary,'returnedModelCalls':len(by),'eligibleExpectedModelCalls':plan['sourceEligibleMaximumCalls'],'missingEligibleCallsUNASSESSED':sum(r['state']=='UNASSESSED' for r in rows),'invalidCompletionRecords':invalid,'goldRoleModeParallelCountUsed':False,'qualifiedGenAIModels':[],'coreValidatorActuallyInvoked':False,'wholeDocumentFormalSlots':{'expected':1270,'assessed':0,'UNASSESSED':1270},'wholeDocumentFormalClocks':{'expected':70,'assessed':0,'UNASSESSED':70},'productionAdoption':False}
def main():
    records=[];path=ROOT/'responses.jsonl'
    if path.exists():
        for line in path.read_text().splitlines():
            try:records.append(json.loads(line))
            except json.JSONDecodeError:records.append(None)
    report=assess(json.loads((ROOT/'inputs.json').read_text())['tasks'],json.loads((ROOT/'caller-plan.json').read_text()),json.loads((ROOT/'oracle-evaluation-only.json').read_text()),records)
    report.update(inputsSHA256=digest(ROOT/'inputs.json'),oracleSHA256=digest(ROOT/'oracle-evaluation-only.json'),callerPlanSHA256=digest(ROOT/'caller-plan.json'))
    (ROOT/'comparison-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
if __name__=='__main__':main()
