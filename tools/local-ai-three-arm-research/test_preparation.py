"""Pure hostile/synthetic controls; never opens a model or launches the worker."""
import ast
import copy
import json
from pathlib import Path
import tempfile
import signal
import subprocess
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import guard
import protocol
import run_once
import score_outputs
import worker

ROOT = Path(__file__).resolve().parent

def task():
    sources = [{'id': 'z7', 'text': '仮', 'box': [1, 2, 3, 4], 'owner': 'cell'},
               {'id': 'a2', 'text': '名', 'box': [5, 2, 3, 4], 'owner': 'cell'},
               {'id': 'x9', 'text': '別', 'box': [20, 2, 3, 4], 'owner': 'context'}]
    return {'id': 'opaque', 'sources': sources, 'promptInput': {'sources': sources},
            'acquisitionRows': [], 'cropSHA256': 'opaque'}

def candidate(state='CANDIDATE'):
    d = {'state': state, 'headers': {k: [] for k in protocol.HEADERS}, 'lessons': [], 'comparison': 'NOT_APPLICABLE'}
    if state == 'CANDIDATE':
        d['headers']['class'] = ['z7', 'a2']
    return d

class Preparation(unittest.TestCase):
    def decode(self, value, t=None, compare=False):
        return protocol.decode_candidate(json.dumps(value), t or task(), compare)

    def test_original_order_not_lexical_id_order(self):
        self.assertEqual(self.decode(candidate())['headers']['class'], ['z7', 'a2'])
        d = candidate(); d['headers']['class'].reverse()
        with self.assertRaises(ValueError): self.decode(d)

    def test_all_roles_same_complete_id_enum(self):
        schema = protocol.id_schema(task())
        every = list(schema['properties']['headers']['properties'].values()) + list(schema['properties']['lessons']['items']['properties'].values())
        self.assertTrue(all(a['items']['enum'] == ['z7', 'a2', 'x9'] for a in every))

    def test_foreign_id_and_numeric_coercion_refused(self):
        for value in ['foreign', 7, True]:
            d = candidate(); d['headers']['class'] = [value]
            with self.assertRaises(ValueError): self.decode(d)

    def test_cross_role_duplicate_refused(self):
        d = candidate(); d['headers']['day'] = ['z7']
        with self.assertRaises(ValueError): self.decode(d)

    def test_foreign_cell_body_refused_even_valid_allid_enum(self):
        d = candidate(); d['headers']['class'] = []
        d['lessons'] = [{'subject': ['z7'], 'teacher': ['a2'], 'room': ['x9']}]
        with self.assertRaisesRegex(ValueError, 'cross-cell'): self.decode(d)

    def test_missing_teacher_not_empty_proof(self):
        d = candidate(); d['headers']['class'] = []; d['lessons'] = [{'subject': ['z7'], 'teacher': [], 'room': ['a2']}]
        with self.assertRaisesRegex(ValueError, 'EMPTY'): self.decode(d)

    def test_abstentions_carry_no_content_and_no_empty_lesson(self):
        for state in ('NONE', 'UNKNOWN'):
            self.assertEqual(self.decode(candidate(state))['state'], state)
            d = candidate(state); d['lessons'] = [{'subject': [], 'teacher': [], 'room': []}]
            with self.assertRaises(ValueError): self.decode(d)

    def test_empty_candidate_not_formal_empty(self):
        d = candidate(); d['headers']['class'] = []
        with self.assertRaises(ValueError): self.decode(d)

    def test_duplicate_json_property_unknown_field_and_value_generation(self):
        with self.assertRaises(ValueError): protocol.strict_json('{"a":1,"a":2}')
        for key in ('value', 'confidence', 'box', 'acceptance'):
            d = candidate(); d[key] = 'invented'
            with self.assertRaises(ValueError): self.decode(d)

    def test_nonfinite_geometry_refused(self):
        t = task(); t['sources'][0]['box'][0] = float('nan')
        with self.assertRaises(ValueError): protocol.validate_input(t)

    def test_blind_literal_unchanged_and_no_sourceid_field(self):
        raw = '{"state":"CANDIDATE","lines":["A·O0〜1"]}'
        self.assertEqual(protocol.decode_blind(raw)['lines'], ['A·O0〜1'])
        with self.assertRaises(ValueError): protocol.decode_blind('{"state":"CANDIDATE","lines":["a"],"sourceIDs":["z7"]}')

    def test_blind_unknown_not_empty(self):
        self.assertEqual(protocol.decode_blind('{"state":"UNKNOWN","lines":[]}')['state'], 'UNKNOWN')
        with self.assertRaises(ValueError): protocol.decode_blind('{"state":"NONE","lines":["a"]}')

    def test_stage_comparison_not_in_text_arm(self):
        d = candidate(); d['comparison'] = 'AGREE'
        with self.assertRaises(ValueError): self.decode(d)
        self.assertEqual(self.decode(d, compare=True)['comparison'], 'AGREE')

    def test_blind_prompt_contains_no_ocr_sources(self):
        self.assertNotIn(task()['sources'][0]['text'], protocol.BLIND_INSTRUCTION)
        tree = ast.parse((ROOT / 'worker.py').read_text())
        blind_calls = [n for n in ast.walk(tree) if isinstance(n, ast.Call) and isinstance(n.func, ast.Name) and n.func.id == 'call' and len(n.args) > 1 and isinstance(n.args[1], ast.Constant) and n.args[1].value == 'arm3_blind']
        self.assertEqual(len(blind_calls), 1)
        self.assertEqual(ast.unparse(blind_calls[0].args[2]), 'BLIND_INSTRUCTION')
        self.assertNotIn('oracle', [str(n.value) for n in ast.walk(tree) if isinstance(n, ast.Constant) and isinstance(n.value, str) and n.value.endswith('.json')])

    def test_worker_refuses_before_native_import(self):
        with patch.object(worker, 'verify_packet', side_effect=RuntimeError('NO_EXACT_GO')), patch.object(worker.Path, 'read_text', return_value='{}'):
            with self.assertRaisesRegex(RuntimeError, 'NO_EXACT_GO'): worker.main()
        tree = ast.parse((ROOT / 'worker.py').read_text())
        self.assertFalse(any(isinstance(n, ast.ImportFrom) and n.module == 'litert_lm' for n in tree.body))

    def test_worker_hash_gate_never_opens_evaluation_oracle(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'recipe.json').write_text('{}')
            (root / 'worker-input').write_text('native only')
            oracle = root / 'oracle-evaluation-only.json'
            oracle.write_text('expected answer')
            freeze = {'pins': [{'path': 'worker-input', 'bytes': 11, 'sha256': protocol.digest(root/'worker-input')},
                               {'path': 'oracle-evaluation-only.json', 'bytes': 15, 'sha256': protocol.digest(oracle)}], 'workerPinPaths': ['worker-input']}
            (root/'packet-freeze.json').write_text(json.dumps(freeze))
            (root/'root-inference-approval.json').write_text(json.dumps({'action': 'ONE_LOCAL_GEMMA4_THREE_ARM_DEVELOPMENT_COMPONENT_PROBE',
                'sourceFreezeSHA256': protocol.digest(root/'packet-freeze.json'), 'recipeSHA256': protocol.digest(root/'recipe.json'),
                'plannedMaximumCalls': 36, 'newRecognizerCalls': 0, 'productionAdoption': False, 'fullDocumentAssessment': False}))
            real = guard.digest
            opened = []
            def tracked(path):
                opened.append(str(path)); return real(path)
            with patch.object(guard, 'digest', tracked): guard.verify_packet(root, {}, pin_role='worker')
            self.assertNotIn(str(oracle), opened)

    def test_disk_and_memory_failure_no_launch_or_started_receipt(self):
        recipe = {'diskReserveBytes': 10, 'workingAllowanceBytes': 2, 'minimumAvailableMemoryBytes': 20}
        def cgroups(mem):
            return [{'path': 'synthetic', 'max': mem, 'high': None, 'current': 0, 'stat': {k: 0 for k in ('file','shmem','inactive_file','file_dirty','file_writeback','file_mapped','unevictable')}}]
        for disk, mem in ((11, 20), (12, 19)):
            with self.assertRaises(RuntimeError): guard.resources(ROOT, recipe, cgroup_records=cgroups(mem), disk_free=disk)
        self.assertEqual(guard.resources(ROOT, recipe, cgroup_records=cgroups(20), disk_free=12)['availableDiskBytes'], 12)
        called = []
        with tempfile.TemporaryDirectory() as directory, patch.object(run_once, 'ROOT', Path(directory)), patch.object(run_once, 'resources', side_effect=RuntimeError('reserve')):
            with self.assertRaises(RuntimeError): run_once.guarded_start(recipe, lambda: called.append(True))
            self.assertFalse((Path(directory) / 'probe-started.json').exists())
        self.assertEqual(called, [])

    def test_cgroup_shmem_active_slab_dirty_mapped_not_counted(self):
        stat = {'file': 900, 'shmem': 700, 'inactive_file': 300, 'active_file': 800, 'slab_reclaimable': 9000,
                'file_dirty': 30, 'file_writeback': 20, 'file_mapped': 40, 'unevictable': 10}
        budget, records = guard.cgroup_budget([{'path': 'x', 'max': 1000, 'high': None, 'current': 950, 'stat': stat}])
        self.assertEqual(budget, 150)
        self.assertEqual(records[0]['conservativeReclaimableBytes'], 100)

    def test_cgroup_ancestor_high_limit_and_missing_stat_fail_closed(self):
        stat = {k: 0 for k in ('file','shmem','inactive_file','file_dirty','file_writeback','file_mapped','unevictable')}
        child = {'path': 'child', 'max': 1000, 'high': None, 'current': 10, 'stat': stat}
        parent = {'path': 'parent', 'max': 2000, 'high': 500, 'current': 499, 'stat': stat}
        self.assertEqual(guard.cgroup_budget([child,parent])[0], 1)
        with self.assertRaises(RuntimeError): guard.cgroup_budget([{**child, 'stat': {}}])
        with self.assertRaises(RuntimeError): guard.cgroup_budget([{**child, 'max': None}])

    def fake_supervisor(self, kind):
        # Fake Popen object only: no process, native import, or model runs.
        recipe = json.loads((ROOT/'recipe.json').read_text())
        fake = SimpleNamespace(pid=987654, poll=lambda: None, wait=lambda **kw: -9)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root/'recipe.json').write_text(json.dumps(recipe))
            if kind == 'CALL_DEADLINE':
                (root/'worker-events.jsonl').write_text(json.dumps({'event':'callStarted','monotonic':930})+'\n')
                # Expected existing-event exclusivity belongs to real runner.
                # Emit it at fake launch, rather than before that boundary.
                event = (root/'worker-events.jsonl').read_text();(root/'worker-events.jsonl').unlink()
            else:
                event = None
            def fake_launch(*args, **kwargs):
                if event is not None:(root/'worker-events.jsonl').write_text(event)
                return fake
            count = 0
            def resource_gate(*args, **kwargs):
                nonlocal count
                count += 1
                if kind == 'DISK_RESERVE_DURING_EXECUTION' and count > 1:raise RuntimeError('disk')
                return {'availableDiskBytes': 9999999999, 'availableCgroupMemoryBudgetBytes': 9999999999}
            with patch.object(run_once,'ROOT',root), patch.object(run_once,'verify_packet',return_value={'sourceFreezeSHA256':'opaque'}), patch.object(run_once,'bind_runtime',side_effect=lambda root,r:r), \
                 patch.object(run_once,'subreaper'), \
                 patch.object(run_once,'resources',side_effect=resource_gate), patch.object(run_once.subprocess,'Popen',side_effect=fake_launch) as launch, \
                 patch.object(run_once.subprocess,'run',return_value=SimpleNamespace(returncode=0,stderr=b'')) as reporter, \
                 patch.object(run_once,'rss_group',return_value=recipe['maximumRSSBytes']+1 if kind=='PROCESS_RSS_LIMIT' else 1), \
                 patch.object(run_once.time,'monotonic',return_value=1000), patch.object(run_once.os,'killpg') as kill, patch('builtins.print'):
                with self.assertRaises(SystemExit) as stopped:run_once.main()
                self.assertEqual(stopped.exception.code,1)
            self.assertEqual(launch.call_count,1)
            kill.assert_called_once_with(fake.pid,signal.SIGKILL)
            receipt=json.loads((root/'execution-receipt.json').read_text())
            self.assertEqual(receipt['failure'],kind)
            self.assertEqual(receipt['retries'],0)
            self.assertFalse(receipt['productionAdoption'])
            if kind=='DISK_RESERVE_DURING_EXECUTION':
                self.assertEqual(reporter.call_count,0)
                self.assertEqual(receipt['reporter']['state'],'UNASSESSED_REPORTER_FAILED')
            else:self.assertEqual(reporter.call_count,1)

    def test_fake_worker_rss_cap_killed_once_no_retry(self):
        self.fake_supervisor('PROCESS_RSS_LIMIT')

    def test_fake_worker_call_deadline_killed_once_no_retry(self):
        self.fake_supervisor('CALL_DEADLINE')

    def test_fake_worker_disk_failure_kills_no_dependent_reporter(self):
        self.fake_supervisor('DISK_RESERVE_DURING_EXECUTION')

    def test_actual_sacrificial_socket_deny_no_model_or_network_packet(self):
        code='''import socket,os,errno,subprocess,sys,json
from network_guard import install
guard=install()
def denied(fn):
 try:fn()
 except OSError as e:
  assert e.errno==errno.EPERM;return True
 raise AssertionError('network/group-escape permitted')
checks=[denied(lambda:socket.socket(f,t)) for f,t in [(socket.AF_INET,socket.SOCK_STREAM),(socket.AF_INET,socket.SOCK_DGRAM),(socket.AF_INET6,socket.SOCK_STREAM),(socket.AF_UNIX,socket.SOCK_STREAM)]]
checks += [denied(socket.socketpair),denied(os.setsid),denied(lambda:os.setpgid(0,0))]
child_code=chr(10).join(['import socket,errno','try:',' socket.socket()','except OSError as e:',' assert e.errno==errno.EPERM','else:'," raise AssertionError('socket allowed')"])
child=subprocess.run([sys.executable,'-c',child_code],capture_output=True)
assert child.returncode==0,child.stderr
print(json.dumps({'installed':guard['installed'],'checks':len(checks),'inheritedChildDenied':True,'modelLoads':0,'inferenceCalls':0}))
'''
        result=subprocess.run([sys.executable,'-c',code],cwd=ROOT,capture_output=True,text=True,timeout=10)
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertEqual(json.loads(result.stdout),{'installed':True,'checks':7,'inheritedChildDenied':True,'modelLoads':0,'inferenceCalls':0})

    def test_fake_monitor_exception_always_cleans_before_reporter(self):
        recipe=json.loads((ROOT/'recipe.json').read_text())
        fake=SimpleNamespace(pid=987654,poll=lambda:None,wait=lambda **kw:-9)
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);(root/'recipe.json').write_text(json.dumps(recipe))
            with patch.object(run_once,'ROOT',root),patch.object(run_once,'verify_packet',return_value={'sourceFreezeSHA256':'opaque'}), patch.object(run_once,'bind_runtime',side_effect=lambda root,r:r), \
                 patch.object(run_once,'resources',return_value={}),patch.object(run_once,'subreaper'), \
                 patch.object(run_once.subprocess,'Popen',return_value=fake),patch.object(run_once,'rss_group',side_effect=OSError('injected monitor error')), \
                 patch.object(run_once.os,'killpg') as kill,patch.object(run_once,'group_pids',return_value=[]), \
                 patch.object(run_once.subprocess,'run',return_value=SimpleNamespace(returncode=0,stderr=b'')) as reporter,patch('builtins.print'):
                with self.assertRaises(SystemExit) as stopped:run_once.main()
                self.assertEqual(stopped.exception.code,1)
            kill.assert_called_once_with(fake.pid,signal.SIGKILL)
            self.assertEqual(reporter.call_count,1)
            receipt=json.loads((root/'execution-receipt.json').read_text())
            self.assertEqual(receipt['failure'],'SUPERVISOR_EXCEPTION')
            self.assertTrue(receipt['processGroupCleanup']['complete'])

    def test_normal_worker_exit_still_kills_and_reaps_remaining_group(self):
        fake=SimpleNamespace(pid=987654,wait=lambda **kw:0)
        with patch.object(run_once.os,'killpg') as kill,patch.object(run_once.os,'waitpid',side_effect=[(123,0),(0,0)]),patch.object(run_once,'group_pids',return_value=[]):
            result=run_once.cleanup_group(fake)
        kill.assert_called_once_with(fake.pid,signal.SIGKILL)
        self.assertTrue(result['complete'])

    def tuple_report(self, lessons, expected=None):
        ids=['s0','s1','t0','t1','r0','r1'];texts=['A','B','C','D','E','F']
        t=task();t['sources']=[{'id':i,'text':v,'box':[1,2,3,4],'owner':'cell'} for i,v in zip(ids,texts)]
        c=candidate();c['headers']={k:[] for k in protocol.HEADERS};c['lessons']=lessons
        original=[{'subject':'A','teacher':'C','room':'E'},{'subject':'B','teacher':'D','room':'F'}]
        oracle={'tasks':[{'id':'opaque','literalLines':[],'expectedOwnedRoleIDs':{**{k:[] for k in protocol.HEADERS},'subject':['s0','s1'],'teacher':['t0','t1'],'room':['r0','r1']},'expectedParallelCount':2,'expectedTuples':original if expected is None else expected}]}
        record={'taskID':'opaque','stage':'arm2_text','raw':json.dumps(c),'disposition':'CANDIDATE'}
        return score_outputs.assess({'tasks':[t]},oracle,[record])['tasks'][0]['arm2_text']

    def test_parallel_teacher_room_swap_union_count_still_exact_binding_wrong(self):
        base=[{'subject':['s0'],'teacher':['t0'],'room':['r0']},{'subject':['s1'],'teacher':['t1'],'room':['r1']}]
        self.assertTrue(self.tuple_report(base)['tupleBindingExact'])
        for role in ('teacher','room'):
            value=copy.deepcopy(base);value[0][role],value[1][role]=value[1][role],value[0][role]
            result=self.tuple_report(value)
            self.assertTrue(result['roleIdUnionExact']);self.assertTrue(result['parallelCountExact']);self.assertFalse(result['tupleBindingExact'])

    def test_tuple_order_merged_count_and_one_wrong_each_detected(self):
        base=[{'subject':['s0'],'teacher':['t0'],'room':['r0']},{'subject':['s1'],'teacher':['t1'],'room':['r1']}]
        self.assertFalse(self.tuple_report(base[::-1])['tupleBindingExact'])
        self.assertFalse(self.tuple_report(base,expected=[{'subject':'A','teacher':'C','room':'E'}])['tupleBindingExact'])
        self.assertFalse(self.tuple_report(base,expected=[{'subject':'A','teacher':'C','room':'E'},{'subject':'B','teacher':'WRONG','room':'F'}])['tupleBindingExact'])

    def test_no_original_bound_tuple_is_unassessed_not_guessed(self):
        inputs={'tasks':[task()]};oracle={'tasks':[{'id':'opaque','literalLines':[],'expectedOwnedRoleIDs':{},'expectedParallelCount':0,'expectedTuples':None}]}
        row={'taskID':'opaque','stage':'arm2_text','raw':json.dumps(candidate()),'disposition':'CANDIDATE'}
        result=score_outputs.assess(inputs,oracle,[row])['tasks'][0]['arm2_text']
        self.assertIsNone(result['tupleBindingExact']);self.assertEqual(result['tupleBindingAssessment'],'UNASSESSED_NO_BOUND_ORIGINAL_ORACLE_TUPLE')

    def test_missing_outputs_all_explicit_unassessed_and_false_native_gate(self):
        inputs = {'tasks': [task()]}; oracle = {'tasks': [{'id': 'opaque', 'literalLines': [], 'expectedOwnedRoleIDs': {}, 'expectedParallelCount': 0}]}
        result = score_outputs.assess(inputs, oracle, [])
        self.assertEqual(result['missingCallsUNASSESSED'], 3)
        self.assertEqual(result['wholeDocumentFormalSlots']['UNASSESSED'], 1270)
        self.assertEqual(result['wholeDocumentFormalClocks']['UNASSESSED'], 70)
        self.assertIs(result['nativeWholeDocumentProtectiveGate'], False)
        self.assertFalse(result['coreValidatorActuallyInvoked'])

    def test_duplicate_output_not_best_response_selected(self):
        inputs = {'tasks': [task()]}; oracle = {'tasks': [{'id': 'opaque', 'literalLines': [], 'expectedOwnedRoleIDs': {}, 'expectedParallelCount': 0}]}
        rows = [{'taskID': 'opaque', 'stage': 'arm2_text', 'raw': json.dumps(candidate('UNKNOWN')), 'disposition': 'UNKNOWN'}] * 2
        result = score_outputs.assess(inputs, oracle, rows)
        self.assertEqual(result['tasks'][0]['arm2_text']['state'], 'UNASSESSED')
        self.assertEqual(result['tasks'][0]['arm2_text']['operationalError'], 'DUPLICATE_COMPLETION_NO_SELECTION')

    def test_actual_frozen_input_no_labels_hidden_in_ids(self):
        data = json.loads((ROOT / 'inputs.json').read_text())
        self.assertEqual(len(data['tasks']), 12)
        for t in data['tasks']:
            protocol.validate_input(t)
            self.assertRegex(t['id'], '^t[0-9a-f]{16}$')
            self.assertEqual(protocol.digest(t['cropPath']), t['cropSHA256'])
            for source in t['sources']:
                self.assertRegex(source['id'], '^p[0-9]+s[0-9]+$')
                self.assertTrue(source['fromOcr'])
            self.assertNotIn('expected', json.dumps(t['promptInput']))
            self.assertNotIn('科目:', ''.join(s['text'] for s in t['sources']))

if __name__ == '__main__':
    unittest.main(verbosity=2)
