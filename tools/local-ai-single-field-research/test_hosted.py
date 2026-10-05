"""Hosted controller negative/order controls. No HTTP, native import or Engine."""
from contextlib import redirect_stdout
from copy import deepcopy
import hashlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import bootstrap_ci as hosted
from comparison import select_recipe, CONDITIONS

ROOT = Path(__file__).resolve().parent


class HostedTests(unittest.TestCase):
    def setUp(self):
        self.base = json.loads((ROOT / 'recipe.json').read_text())
        self.config = json.loads((ROOT / 'comparison-config.json').read_text())
        self.recipes = [select_recipe(self.base, condition, self.config) for condition in CONDITIONS]

    def test_wrong_qwen_recipe_fails_before_any_asset_fetch(self):
        with patch.object(hosted, 'verify_source', return_value={}), patch.object(hosted, 'download') as network:
            with self.assertRaisesRegex(RuntimeError, 'QWEN_RECIPE_APPROVAL'):
                hosted.approved_recipes(ROOT, 'a'*64, 'b'*64, '0'*64, hosted.WORKFLOW)
            network.assert_not_called()

    def test_push_and_rerun_cannot_authorize_hosted_inference(self):
        env = {'GITHUB_ACTIONS': 'true', 'GITHUB_EVENT_NAME': 'push', 'GITHUB_RUN_ATTEMPT': '1',
               'GITHUB_REPOSITORY': hosted.REPOSITORY, 'GITHUB_REF': hosted.BRANCH}
        with patch.dict(os.environ, env, clear=True), patch.object(hosted, 'download') as network:
            with self.assertRaisesRegex(RuntimeError, 'DISPATCH_CONTEXT'):
                hosted.dispatch_authority('a'*64, 'b'*64, 'c'*64)
            network.assert_not_called()
        env['GITHUB_EVENT_NAME'] = 'workflow_dispatch'
        env['GITHUB_RUN_ATTEMPT'] = '2'
        with patch.dict(os.environ, env, clear=True):
            with self.assertRaisesRegex(RuntimeError, 'RERUN_NOT_AUTHORIZED'):
                hosted.dispatch_authority('a'*64, 'b'*64, 'c'*64)

    def test_exact_dispatch_hashes_and_head_bound(self):
        hashes = ('a'*64, 'b'*64, 'c'*64)
        expected = dict(zip(('approved_packet_sha256', 'approved_recipe_sha256', 'approved_qwen_recipe_sha256'), hashes))
        env = {'GITHUB_ACTIONS': 'true', 'GITHUB_EVENT_NAME': 'workflow_dispatch', 'GITHUB_RUN_ATTEMPT': '1',
               'GITHUB_REPOSITORY': hosted.REPOSITORY, 'GITHUB_REF': hosted.BRANCH,
               'GITHUB_SHA': 'd'*40, 'GITHUB_EVENT_PATH': 'not-read-from-disk'}
        event = {'ref': 'research/single-field-focal-20261005', 'inputs': expected}
        with patch.dict(os.environ, env, clear=True), patch.object(hosted, 'read', return_value=event), \
                patch.object(hosted.subprocess, 'check_output', return_value='d'*40+'\n'):
            authority = hosted.dispatch_authority(*hashes)
            self.assertEqual(authority['headSHA'], 'd'*40)
        event['inputs'] = {**expected, 'approved_qwen_recipe_sha256': 'e'*64}
        with patch.dict(os.environ, env, clear=True), patch.object(hosted, 'read', return_value=event):
            with self.assertRaisesRegex(RuntimeError, 'DISPATCH_INPUTS'):
                hosted.dispatch_authority(*hashes)

    def exercise_execution(self, fail_first=False):
        events = []
        with tempfile.TemporaryDirectory() as name:
            parent = Path(name)
            def fetch(root, scratch, recipes):
                events.append('verified-public-assets')
                return {}, {}
            def stage(root, scratch, freeze, recipe, packet_sha, binding, models, authority):
                condition = recipe['comparisonCondition']
                events.append('payload:'+condition)
                packet = scratch / condition
                packet.mkdir()
                (packet / 'recipe.json').write_text(json.dumps(recipe))
                return packet
            def run(packet, recipe, binding, scratch):
                condition = recipe['comparisonCondition']
                events.append('start:'+condition)
                if fail_first:
                    raise RuntimeError('first-model operational failure')
                events.append('cleaned:'+condition)
                return {'completedCalls': 4}
            with patch.object(hosted, 'approved_recipes', return_value=({}, self.recipes)), \
                    patch.object(hosted, 'resources'), patch.object(hosted, 'fetch_runtime', side_effect=fetch), \
                    patch.object(hosted, 'stage_condition', side_effect=stage), \
                    patch.object(hosted, 'condition_run', side_effect=run), \
                    patch.object(hosted.sys, 'version_info', (3, 12, 14)), redirect_stdout(io.StringIO()):
                if fail_first:
                    with self.assertRaisesRegex(RuntimeError, 'first-model'):
                        hosted.execute(ROOT, 'a'*64, 'b'*64, 'c'*64, hosted.WORKFLOW, parent, {})
                else:
                    hosted.execute(ROOT, 'a'*64, 'b'*64, 'c'*64, hosted.WORKFLOW, parent, {})
            self.assertFalse((parent / hosted.MARKER).exists())
            self.assertEqual(list(parent.iterdir()), [])
        return events

    def test_all_public_assets_before_payload_and_models_sequential(self):
        self.assertEqual(self.exercise_execution(), ['verified-public-assets', 'payload:gemma-single-field',
                         'start:gemma-single-field', 'cleaned:gemma-single-field', 'payload:qwen-single-field',
                         'start:qwen-single-field', 'cleaned:qwen-single-field'])

    def test_first_operational_failure_prevents_second_stage_and_engine(self):
        self.assertEqual(self.exercise_execution(True), ['verified-public-assets', 'payload:gemma-single-field',
                         'start:gemma-single-field'])

    def test_unknown_scratch_or_incomplete_cleanup_never_deleted(self):
        with tempfile.TemporaryDirectory() as name:
            parent = Path(name)
            scratch = parent / (hosted.PREFIX+'owned')
            packet = scratch / CONDITIONS[0]
            packet.mkdir(parents=True)
            (packet / 'recipe.json').write_text(json.dumps(self.recipes[0]))
            (parent / hosted.MARKER).write_text(str(scratch)+'\n')
            with patch.object(hosted, 'terminate_owned', return_value={'complete': False}), \
                    patch.object(hosted, 'print_research_logs', return_value={'complete': True}), redirect_stdout(io.StringIO()):
                with self.assertRaisesRegex(RuntimeError, 'INCOMPLETE_CLEANUP'):
                    hosted.cleanup_only(parent)
            self.assertTrue(scratch.exists())
            self.assertTrue((parent / hosted.MARKER).exists())

    def test_resource_refusal_precedes_fetch_and_scratch_creation(self):
        with tempfile.TemporaryDirectory() as name:
            parent = Path(name)
            with patch.object(hosted, 'approved_recipes', return_value=({}, self.recipes)), \
                    patch.object(hosted.sys, 'version_info', (3, 12, 14)), \
                    patch.object(hosted, 'resources', side_effect=RuntimeError('resource refusal')), \
                    patch.object(hosted, 'fetch_runtime') as fetch:
                with self.assertRaisesRegex(RuntimeError, 'resource refusal'):
                    hosted.execute(ROOT, 'a'*64, 'b'*64, 'c'*64, hosted.WORKFLOW, parent, {})
                fetch.assert_not_called()
            self.assertEqual(list(parent.iterdir()), [])

    def test_registered_push_is_validation_only_and_no_matrix_artifacts_cache(self):
        text = hosted.WORKFLOW.read_text()
        self.assertIn("if: github.event_name == 'push'", text)
        self.assertIn('--validate-only', text)
        self.assertIn("if: github.event_name == 'workflow_dispatch'", text)
        for forbidden in ('matrix:', 'upload-artifact', 'actions/cache', 'persist-credentials: true'):
            self.assertNotIn(forbidden, text)
        self.assertIn("python-version: '3.12.14'", text)
        self.assertIn('runs-on: ubuntu-24.04', text)


if __name__ == '__main__':
    unittest.main()
