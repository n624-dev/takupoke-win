"""Model-free controls using retained fictional native rows, never role truth."""
from copy import deepcopy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from contract import (Refusal, caller_binding, checked_source, candidate_map, decode, execute_plan,
                      prepare_plan, request, schema, specification, CONDITIONS)
from comparison import validate_recipe, select_recipe, derive_packet
from native_grammar import adapt
from guard import verify_packet, resources
ROOT = Path(__file__).resolve().parent
TASKS = json.loads((ROOT / 'inputs.json').read_text())['tasks']
TASK = next(t for t in TASKS if t['id'] == 't000466d2ba598e3e')

class ContractTests(unittest.TestCase):
    def setUp(self):
        self.task = deepcopy(TASK)
        self.binding = caller_binding(self.task, 'subject')

    def result(self, state, candidate):
        return decode(json.dumps({'state': state, 'candidateID': candidate}), self.task, self.binding)

    def test_native_rows_complete_for_all_fields_without_gold_filter(self):
        maps = candidate_map(self.task)
        focal = [s['id'] for s in self.task['sources'] if s['owner'] == 'cell']
        self.assertEqual([i for c in maps for i in c['originalIDs']], focal)
        self.assertEqual([c['nativeChunkID'] for c in maps], self.task['originalChunkIDs'])
        for field in ('subject','teacher','room'):
            binding = caller_binding(self.task, field)
            native, info = adapt(schema(self.task,binding))
            self.assertEqual(native['properties']['candidateID']['enum'], ['r0','r1','r2','NONE'])
            self.assertEqual(info['removedUniqueItemsCount'],0)
            self.assertEqual(request(self.task,binding),request(self.task,self.binding))

    def test_wrong_role_rows_always_unverified_proposals(self):
        for candidate in ('r0','r1','r2'):
            result = self.result('PRESENT',candidate)
            self.assertEqual(result['ids'], next(c['originalIDs'] for c in candidate_map(self.task) if c['candidateID']==candidate))
            self.assertEqual(result['assignmentCertificate'],'ABSENT')
            self.assertEqual(result['roleCorrectness'],'UNASSESSED')
            self.assertFalse(result['productionAdoption'])

    def test_code_rebuilds_original_order_and_does_not_use_sourceorder_metadata(self):
        for s in self.task['sources']: s['sourceOrder'] = 999-s['sourceOrder']
        for s in self.task['promptInput']['sources']: s['sourceOrder'] = 999-s['sourceOrder']
        self.binding=caller_binding(self.task,'subject')
        result=self.result('PRESENT','r0')
        self.assertEqual(result['ids'],['p1s70','p1s71','p1s72','p1s73'])
        self.assertEqual(result['value'],'幻学03')

    def test_model_payload_excludes_characterids_floats_proofs_and_lineage(self):
        payload=request(self.task,self.binding)
        self.assertEqual(set(payload),{'candidates','relativeRows','readOnlyContext'})
        self.assertEqual(payload['candidates'],[{'candidateID':'r0','text':'幻学03'},{'candidateID':'r1','text':'詠Z2'},{'candidateID':'r2','text':'舎71'}])
        serialized=json.dumps(payload)
        for forbidden in ('p1s','chunkID','sourceLine','box','assignmentCertificate','confidence','sourceOrder'):
            self.assertNotIn(forbidden,serialized)
        def walk(x):
            self.assertIsNot(type(x),float)
            if isinstance(x,dict):
                for v in x.values(): walk(v)
            elif isinstance(x,list):
                for v in x: walk(v)
        walk(payload)

    def test_unknown_empty_lists_multicandidate_and_context_are_rejected(self):
        self.assertEqual(self.result('UNKNOWN','NONE')['ids'],[])
        self.assertEqual(self.result('UNKNOWN','NONE')['value'],'')
        for state,candidate in (('UNKNOWN','r0'),('PRESENT','NONE'),('PRESENT','p1s70'),('PRESENT','context0'),('EMPTY','NONE'),('PRESENT',['r0','r1'])):
            with self.assertRaises(Refusal): self.result(state,candidate)
        empty=next(t for t in TASKS if t['id']=='tab477632a6790f57')
        with self.assertRaisesRegex(Refusal,'NO_FOCAL_SOURCE_NOT_EMPTY_PROOF'): caller_binding(empty,'subject')

    def test_duplicates_extra_properties_and_guessed_text_rejected(self):
        with self.assertRaisesRegex(Refusal,'DUPLICATE_PROPERTY'):
            decode('{"state":"UNKNOWN","state":"PRESENT","candidateID":"NONE"}',self.task,self.binding)
        for raw in ('{"state":"PRESENT","candidateID":"r0","value":"guess"}',
                    '{"state":"PRESENT","candidateID":"r0","ids":["p1s70","p1s70"]}'):
            with self.assertRaisesRegex(Refusal,'RESPONSE_KEYS'): decode(raw,self.task,self.binding)
        task=deepcopy(self.task); task['sources'][1]['id']=task['sources'][0]['id'];task['promptInput']['sources'][1]['id']=task['sources'][0]['id']
        with self.assertRaises(Refusal): caller_binding(task,'subject')

    def test_plan_binding_field_or_candidate_inventory_tampering_refuses_before_call(self):
        self.binding['candidateInventorySHA256']='0'*64
        with self.assertRaisesRegex(Refusal,'REWRITE'): request(self.task,self.binding)
        plan=prepare_plan(TASKS);plan['records'][0]['binding']['requestedField']='teacher';sent=[]
        with self.assertRaisesRegex(Refusal,'FROZEN_PLAN'): execute_plan(plan,TASKS,CONDITIONS[0],sent.append)
        self.assertEqual(sent,[])

    def test_geometry_owner_and_missing_labels_refuse(self):
        focal=next(i for i,s in enumerate(self.task['sources']) if s['owner']=='cell')
        for key,value in (('box',[0,0,1,1]),('owner',None),('box',[float('nan'),1,1,1])):
            task=deepcopy(self.task);task['sources'][focal][key]=value;task['promptInput']['sources'][focal][key]=value
            with self.assertRaises(Refusal): caller_binding(task,'subject')
        for key in ('sourceLine','owner','chunkID','ctcStart'):
            task=deepcopy(self.task);del task['sources'][focal][key]
            with self.assertRaises(Refusal): caller_binding(task,'subject')
        for key,value in (('coordinateScope','verified physical ink boxes'),):
            task=deepcopy(self.task);task['promptInput'][key]=value
            with self.assertRaisesRegex(Refusal,'PROVENANCE_REQUIRED'): caller_binding(task,'subject')
        task=deepcopy(self.task);task['sourceProof']='BODY_ROLE_CERTIFIED'
        with self.assertRaisesRegex(Refusal,'NO_UNVERIFIED_SOURCE_CERTIFICATE'): caller_binding(task,'subject')
        with self.assertRaisesRegex(Refusal,'REQUESTED_FIELD_REQUIRED'): caller_binding(self.task,None)

    def test_native_text_inventory_and_source_line_cannot_be_rewritten(self):
        task=deepcopy(self.task);task['acquisitionRows'][0]['rawText']='fabricated'
        with self.assertRaisesRegex(Refusal,'TEXT_REWRITE'): candidate_map(task)
        task=deepcopy(self.task);task['originalChunkIDs'].pop()
        with self.assertRaisesRegex(Refusal,'CHUNK_INVENTORY'): candidate_map(task)
        task=deepcopy(self.task);i=next(i for i,s in enumerate(task['sources']) if s['id']=='p1s71')
        task['sources'][i]['sourceLine']=999;task['promptInput']['sources'][i]['sourceLine']=999
        with self.assertRaisesRegex(Refusal,'MULTIPLE_SOURCE_LINES'): candidate_map(task)

    def test_context_chunk_requires_one_supplied_owner_box(self):
        task=deepcopy(self.task);task['promptInput']['surroundingClosedBoxes'].append(deepcopy(task['promptInput']['surroundingClosedBoxes'][0]))
        with self.assertRaisesRegex(Refusal,'CONTEXT_OWNER_OVERLAP'): caller_binding(task,'subject')

    def test_same_single_request_for_both_models_and_no_retry(self):
        plan=prepare_plan(TASKS);sent=[]
        for condition in CONDITIONS:
            result=execute_plan(plan,TASKS,condition,lambda s: sent.append(s) or {'state':'UNKNOWN'})
            self.assertEqual(result['calls'],1)
        self.assertEqual(sent[0]['prompt'],sent[1]['prompt']);self.assertEqual(sent[0]['schema'],sent[1]['schema'])
        self.assertTrue(sent[0]['freshConversationRequired']);self.assertEqual(plan['maximumTotalCalls'],2)
        attempts=[]
        def fail(s):
            attempts.append(s['call']);raise RuntimeError('actual adapter failure')
        with self.assertRaisesRegex(RuntimeError,'adapter failure'): execute_plan(plan,TASKS,CONDITIONS[0],fail)
        self.assertEqual(attempts,[1])

    def test_explicit_consumed_target_permutation_invariant_and_no_oracle(self):
        self.assertEqual(prepare_plan(TASKS),prepare_plan(list(reversed(TASKS))))
        self.assertIn('consumed development',prepare_plan(TASKS)['selection'])
        for module in ('contract.py','acquisition.py','source_contract.py','worker.py','prepare.py'):
            text=(ROOT/module).read_text()
            self.assertNotIn('oracle-evaluation-only.json',text);self.assertNotIn('score_outputs',text)


class GuardTests(unittest.TestCase):
    def test_exact_models_and_limits(self):
        base = json.loads((ROOT / 'recipe.json').read_text())
        config = json.loads((ROOT / 'comparison-config.json').read_text())
        for condition in CONDITIONS:
            recipe = select_recipe(base, condition, config)
            validate_recipe(recipe)
            for key, value in (('maximumCalls', 5), ('maximumConcurrentEngines', 2), ('modelDownloads', 3),
                               ('productionAdoption', True), ('modelSHA256', '0'*64), ('maximumRSSBytes', 16*1024**3)):
                bad = deepcopy(recipe)
                bad[key] = value
                with self.assertRaises(RuntimeError):
                    validate_recipe(bad)

    def test_no_root_go_no_native_run(self):
        with self.assertRaises(FileNotFoundError):
            verify_packet(ROOT, json.loads((ROOT / 'recipe.json').read_text()))

    def test_disk_reserve_failure_is_before_launch(self):
        runner_path = ROOT / 'run_once.py'
        spec = importlib.util.spec_from_file_location('single_field_runner', runner_path)
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        launched = []
        with patch.object(runner, 'resources', side_effect=RuntimeError('RESOURCE_PRECONDITION_DISK')):
            with self.assertRaises(RuntimeError):
                runner.guarded_start({}, lambda: launched.append(True))
        self.assertEqual(launched, [])

    def test_second_condition_never_starts_after_incomplete_cleanup(self):
        spec = importlib.util.spec_from_file_location('single_field_compare', ROOT / 'compare_once.py')
        controller = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(controller)
        base = json.loads((ROOT / 'recipe.json').read_text())
        config = json.loads((ROOT / 'comparison-config.json').read_text())
        with tempfile.TemporaryDirectory() as name:
            temp = Path(name)
            roots = [temp / 'gemma', temp / 'qwen']
            for root, condition in zip(roots, CONDITIONS):
                root.mkdir()
                (root / 'recipe.json').write_text(json.dumps(select_recipe(base, condition, config)))
                for filename in ('inputs.json', 'caller-plan.json', 'prompt.txt', 'contract.py', 'acquisition.py', 'source_contract.py', 'worker.py'):
                    (root / filename).write_bytes((ROOT / filename).read_bytes())
            launches = []
            def finished(args, **kwargs):
                root = Path(kwargs['cwd'])
                launches.append(root.name)
                (root / 'execution-receipt.json').write_text(json.dumps({'failure': None, 'exitCode': 0,
                      'processGroupCleanup': {'complete': False}, 'reporter': {'exitCode': 0}}))
                return type('Completed', (), {'returncode': 0})()
            argv = ['compare_once.py', '--gemma', str(roots[0]), '--qwen', str(roots[1]), '--output', str(temp / 'out')]
            with patch('sys.argv', argv), patch.object(controller.subprocess, 'run', side_effect=finished):
                with self.assertRaises(SystemExit):
                    controller.main()
            self.assertEqual(launches, ['gemma'])


if __name__ == '__main__':
    unittest.main()
