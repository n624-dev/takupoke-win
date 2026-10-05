"""Meaningful adapter boundaries without model import or invented oracle roles."""
import copy,json,unittest
from pathlib import Path
from request_contract import execute_plan,Refusal
from reference_contract import specification
from worker import core_plan
from score_outputs import assess
from native_grammar import adapt,compact_sha
from request_contract import decode
ROOT=Path(__file__).resolve().parent
class AdapterControls(unittest.TestCase):
    def setUp(self):
        self.tasks=json.loads((ROOT/'inputs.json').read_text())['tasks'];self.plan=json.loads((ROOT/'caller-plan.json').read_text());self.oracle=json.loads((ROOT/'oracle-evaluation-only.json').read_text())
    def test_native_adapter_uniformly_removes_only_unsupported_keyword(self):
        for task in self.tasks:
            for profile in ('REFERENCE_454','FOCAL_BODY_V1'):
                for stage in ('text','compare'):
                    try:spec=specification(task,profile,stage,{'state':'UNKNOWN','lines':[]} if stage=='compare' else None)
                    except Refusal:continue
                    before=copy.deepcopy(spec['schema']);native,meta=adapt(before)
                    self.assertEqual(before,spec['schema']);self.assertEqual(meta['removedUniqueItemsCount'],3 if profile=='FOCAL_BODY_V1' else 0)
                    self.assertNotIn('uniqueItems',json.dumps(native));self.assertEqual(meta['semanticSchemaCompactSHA256'],compact_sha(before))
                    for field in ('subject','teacher','room'):
                        original=before['properties']['lessons']['items']['properties'][field];expected={k:v for k,v in original.items() if k!='uniqueItems'}
                        self.assertEqual(native['properties']['lessons']['items']['properties'][field],expected)
                    self.assertEqual(native['properties']['state'],before['properties']['state'])
    def test_elided_native_uniqueness_is_still_semantically_required(self):
        task=next(t for t in self.tasks if t['id']==self.plan['records'][1]['taskID']);ids=[s['id'] for s in task['sources'] if s['owner']=='cell']
        raw=json.dumps({'state':'CANDIDATE','lessons':[{'subject':[ids[0],ids[0]],'teacher':ids[1:2],'room':ids[2:]}]})
        with self.assertRaises(Refusal):decode(raw,task,'text')
    def test_report_separates_returned_operational_and_absent(self):
        row={'profile':'FOCAL_BODY_V1','taskID':self.plan['records'][0]['taskID'],'stage':'text','disposition':'OPERATIONAL_UNASSESSED','error':'unsupported-native-grammar'}
        report=assess(self.tasks,self.plan,self.oracle,[row]);self.assertEqual(report['operationalReturnedCallsUNASSESSED'],1);self.assertEqual(report['missingEligibleCallsUNASSESSED'],7)
    def test_actual_specifications_fresh_calls_and_no_source_refusal_callback(self):
        calls=[]
        def fresh(spec):
            self.assertTrue(spec['freshConversationRequired']);self.assertFalse(spec['image']);calls.append(spec)
            return {'profile':spec['profile'],'taskID':spec['taskID'],'stage':spec['stage']}
        result=execute_plan(core_plan(self.plan),self.tasks,fresh)
        self.assertEqual(result['calls'],8);self.assertEqual(len(calls),8)
        self.assertEqual({s['taskID'] for s in calls},{r['taskID'] for r in self.plan['records'] if 'intent' in r})
        for spec in calls:
            if spec['profile']=='FOCAL_BODY_V1':
                self.assertEqual(set(spec['schema']['properties']),{'state','lessons','comparison'} if spec['stage']=='compare' else {'state','lessons'})
                if spec['stage']=='compare':self.assertNotIn('NOT_APPLICABLE',spec['schema']['properties']['comparison']['enum'])
    def test_operational_failure_stops_without_next_callback(self):
        calls=[]
        def fail(spec):calls.append(spec);raise RuntimeError('native-operation-failed')
        with self.assertRaises(RuntimeError):execute_plan(core_plan(self.plan),self.tasks,fail)
        self.assertEqual(len(calls),1)
    def test_scorer_ignores_gold_role_mode_parallel_count(self):
        mutated=copy.deepcopy(self.oracle)
        for t in mutated['tasks']:
            t['expectedOwnedRoleIDs']={'poison':'wrong'};t['expectedParallelCount']=999;t['kind']='POISON';t['mode']='POISON'
        self.assertEqual(assess(self.tasks,self.plan,self.oracle,[]),assess(self.tasks,self.plan,mutated,[]))
    def test_duplicate_completion_no_selection_and_missing_not_refusal_credit(self):
        row={'profile':'FOCAL_BODY_V1','taskID':self.plan['records'][0]['taskID'],'stage':'text','raw':'{"state":"UNKNOWN","lessons":[]}'}
        report=assess(self.tasks,self.plan,self.oracle,[row,row]);match=[r for r in report['rows'] if r['profile']==row['profile'] and r['taskID']==row['taskID'] and r['stage']=='text'][0]
        self.assertEqual(match['state'],'UNASSESSED');self.assertEqual(match['operationalError'],'DUPLICATE_COMPLETION_NO_SELECTION');self.assertIsNone(match['tupleBindingExact'])
if __name__=='__main__':unittest.main()
