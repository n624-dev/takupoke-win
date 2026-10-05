"""Model-free controls using only retained fictional inference sources."""
from copy import deepcopy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from contract import (Refusal, caller_binding, checked_source, decode, execute_plan,
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

    def result(self, state, ids):
        return decode(json.dumps({'state': state, 'ids': ids}), self.task, self.binding)

    def test_native_enum_is_complete_focal_inventory_for_every_requested_field(self):
        focal = [s['id'] for s in self.task['sources'] if s['owner'] == 'cell']
        context = {s['id'] for s in self.task['sources'] if s['owner'] == 'context'}
        for field in ('subject', 'teacher', 'room'):
            binding = caller_binding(self.task, field)
            native, info = adapt(schema(self.task, binding))
            self.assertEqual(native['properties']['ids']['items']['enum'], focal)
            self.assertFalse(context.intersection(focal))
            self.assertNotIn('uniqueItems', native['properties']['ids'])
            self.assertEqual(info['removedUniqueItemsCount'], 1)

    def test_wrong_role_id_and_swapped_roles_remain_unverified_proposals(self):
        # Use other acquisition chunks, without evaluating their actual roles.
        # The protocol cannot infer roles from line order or caller purpose.
        groups = self.task['acquisitionRows']
        for chunk in (groups[1]['id'], groups[2]['id']):
            ids = [s['id'] for s in self.task['sources'] if s['owner'] == 'cell' and s['chunkID'] == chunk]
            result = self.result('PRESENT', ids)
            self.assertEqual(result['ids'], ids)
            self.assertEqual(result['assignmentCertificate'], 'ABSENT')
            self.assertEqual(result['roleCorrectness'], 'UNASSESSED')
            self.assertIs(result['productionAdoption'], False)
            self.assertIn(ids[0], schema(self.task, self.binding)['properties']['ids']['items']['enum'])

    def test_value_rebuilt_from_original_array_order_not_response_or_metadata(self):
        ids = self.binding['selectableFocalIds'][:4]
        for s in self.task['sources']:
            s['sourceOrder'] = 999 - s['sourceOrder']
        for s in self.task['promptInput']['sources']:
            s['sourceOrder'] = 999 - s['sourceOrder']
        self.binding = caller_binding(self.task, 'subject')
        result = self.result('PRESENT', list(reversed(ids)))
        self.assertEqual(result['ids'], ids)
        self.assertEqual(result['value'], ''.join(s['text'] for s in self.task['sources'] if s['id'] in ids))

    def test_all_nonpresent_states_forbid_ids(self):
        for state in ('UNREADABLE', 'MISSING', 'AMBIGUOUS', 'UNKNOWN', 'REFUSED'):
            self.assertEqual(self.result(state, [])['value'], '')
            with self.assertRaisesRegex(Refusal, 'CONSISTENCY'):
                self.result(state, [self.binding['selectableFocalIds'][0]])
        with self.assertRaisesRegex(Refusal, 'CONSISTENCY'):
            self.result('PRESENT', [])

    def test_empty_claim_and_missing_sources_cannot_create_empty_proof(self):
        for ids in ([], [self.binding['selectableFocalIds'][0]]):
            with self.assertRaisesRegex(Refusal, 'EMPTY_NOT_SOURCE_PROVEN'):
                self.result('EMPTY', ids)
        empty = next(t for t in TASKS if t['id'] == 'tab477632a6790f57')
        with self.assertRaisesRegex(Refusal, 'NO_FOCAL_SOURCE_NOT_EMPTY_PROOF'):
            caller_binding(empty, 'subject')
        self.binding['emptyProof'] = {'empty': True}
        with self.assertRaisesRegex(Refusal, 'REWRITE'):
            self.result('EMPTY', [])

    def test_context_and_outside_ids_are_rejected(self):
        context = next(s['id'] for s in self.task['sources'] if s['owner'] == 'context')
        for sid in (context, 'invented-id'):
            with self.assertRaisesRegex(Refusal, 'FOREIGN_OR_CONTEXT_ID'):
                self.result('PRESENT', [sid])

    def test_duplicate_ids_properties_and_extra_values_rejected(self):
        sid = self.binding['selectableFocalIds'][0]
        with self.assertRaisesRegex(Refusal, 'DUPLICATE_ID'):
            self.result('PRESENT', [sid, sid])
        with self.assertRaisesRegex(Refusal, 'DUPLICATE_PROPERTY'):
            decode('{"state":"UNKNOWN","state":"PRESENT","ids":[]}', self.task, self.binding)
        with self.assertRaisesRegex(Refusal, 'RESPONSE_KEYS'):
            decode('{"state":"UNKNOWN","ids":[],"value":"guessed"}', self.task, self.binding)

    def test_wrong_role_filter_or_requested_field_rewrite_cannot_change_frozen_plan(self):
        self.binding['selectableFocalIds'] = self.binding['selectableFocalIds'][:4]
        with self.assertRaisesRegex(Refusal, 'REWRITE'):
            request(self.task, self.binding)
        plan = prepare_plan(TASKS)
        plan['records'][0]['binding']['requestedField'] = 'teacher'
        sent = []
        with self.assertRaisesRegex(Refusal, 'FROZEN_PLAN'):
            execute_plan(plan, TASKS, CONDITIONS[0], sent.append)
        self.assertEqual(sent, [])

    def test_geometry_and_owner_changes_refuse_before_dependent_work(self):
        focal = next(i for i, s in enumerate(self.task['sources']) if s['owner'] == 'cell')
        for key, value, reason in (('box', [0, 0, 1, 1], 'FOCAL_SOURCE_OUTSIDE'),
                                   ('owner', None, 'SOURCE_OWNER_UNKNOWN'),
                                   ('box', [float('nan'), 1, 1, 1], 'SOURCE_AGGREGATE_NUMBER')):
            task = deepcopy(self.task)
            task['sources'][focal][key] = value
            task['promptInput']['sources'][focal][key] = value
            with self.assertRaisesRegex(Refusal, reason):
                caller_binding(task, 'subject')

    def test_missing_label_or_provenance_cannot_be_guessed(self):
        for key in ('sourceLine', 'owner', 'chunkID', 'ctcStart'):
            task = deepcopy(self.task)
            del task['sources'][0][key]
            with self.assertRaises(Refusal):
                caller_binding(task, 'teacher')
        task = deepcopy(self.task)
        task['promptInput']['coordinateScope'] = 'verified physical ink boxes'
        with self.assertRaisesRegex(Refusal, 'PROVENANCE_REQUIRED'):
            caller_binding(task, 'teacher')
        task = deepcopy(self.task)
        task['sourceProof'] = 'BODY_ROLE_CERTIFIED'
        with self.assertRaisesRegex(Refusal, 'NO_UNVERIFIED_SOURCE_CERTIFICATE'):
            caller_binding(task, 'teacher')
        with self.assertRaisesRegex(Refusal, 'REQUESTED_FIELD_REQUIRED'):
            caller_binding(self.task, None)

    def test_chunk_omission_or_text_rewrite_refuses(self):
        task = deepcopy(self.task)
        task['acquisitionRows'][0]['rawText'] = 'fabricated'
        with self.assertRaisesRegex(Refusal, 'TEXT_REWRITE'):
            checked_source(task)
        task = deepcopy(self.task)
        task['originalChunkIDs'].pop()
        with self.assertRaisesRegex(Refusal, 'CHUNK_INVENTORY'):
            checked_source(task)

    def test_same_four_request_bytes_for_both_models_and_no_retry(self):
        plan = prepare_plan(TASKS)
        sent = []
        for condition in CONDITIONS:
            result = execute_plan(plan, TASKS, condition, lambda s: sent.append(s) or {'state': 'UNKNOWN'})
            self.assertEqual(result['calls'], 4)
        for a, b in zip(sent[:4], sent[4:]):
            self.assertEqual(a['prompt'], b['prompt'])
            self.assertEqual(a['schema'], b['schema'])
            self.assertIs(a['freshConversationRequired'], True)
        attempts = []
        def fail(s):
            attempts.append(s['call'])
            raise RuntimeError('actual adapter failure')
        with self.assertRaisesRegex(RuntimeError, 'adapter failure'):
            execute_plan(plan, TASKS, CONDITIONS[0], fail)
        self.assertEqual(attempts, [1])

    def test_selection_uses_no_gold_role_lookup_and_is_permutation_invariant(self):
        self.assertEqual(prepare_plan(TASKS), prepare_plan(list(reversed(TASKS))))
        for module in ('contract.py', 'source_contract.py', 'worker.py', 'prepare.py'):
            text = (ROOT / module).read_text()
            self.assertNotIn('oracle-evaluation-only.json', text)
            self.assertNotIn('score_outputs', text)


class GuardTests(unittest.TestCase):
    def test_exact_models_and_limits(self):
        base = json.loads((ROOT / 'recipe.json').read_text())
        config = json.loads((ROOT / 'comparison-config.json').read_text())
        for condition in CONDITIONS:
            recipe = select_recipe(base, condition, config)
            validate_recipe(recipe)
            for key, value in (('maximumCalls', 5), ('maximumConcurrentEngines', 2), ('modelDownloads', 1),
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
                for filename in ('inputs.json', 'caller-plan.json', 'prompt.txt', 'contract.py', 'source_contract.py', 'worker.py'):
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
