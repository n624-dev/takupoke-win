"""Portable source-only controls. No package/model import, download, or inference."""
import copy
import hashlib
import json
import io
from types import SimpleNamespace
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import bootstrap_ci
import guard
import protocol

ROOT=Path(__file__).resolve().parent

class Portable(unittest.TestCase):
    def recipe(self):return {'diskReserveBytes':10,'workingAllowanceBytes':2,'minimumAvailableMemoryBytes':20}
    def stat(self):return {k:0 for k in ('file','shmem','inactive_file','file_dirty','file_writeback','file_mapped','unevictable')}
    def record(self):return {'path':'synthetic','max':None,'high':None,'current':0,'stat':self.stat()}

    def test_no_visible_cgroup_records_never_uses_host(self):
        with patch.object(guard,'full_vm_proof') as proof:
            with self.assertRaisesRegex(RuntimeError,'NOT_OBSERVED'):guard.resources(ROOT,self.recipe(),disk_free=99,cgroup_records=[])
        proof.assert_not_called()

    def test_finite_ancestor_never_uses_host_even_if_other_unbounded(self):
        record=self.record();record['max']=20
        with patch.object(guard,'full_vm_proof') as proof:
            result=guard.resources(ROOT,self.recipe(),disk_free=12,cgroup_records=[self.record(),record])
        proof.assert_not_called();self.assertFalse(result['hostMemAvailableUsed'])

    def test_zero_high_limit_is_real_finite_failure(self):
        record=self.record();record['high']=0
        with patch.object(guard,'full_vm_proof') as proof:
            with self.assertRaisesRegex(RuntimeError,'MEMORY'):guard.resources(ROOT,self.recipe(),disk_free=99,cgroup_records=[record])
        proof.assert_not_called()

    def test_unbounded_container_or_unknown_vm_fails_closed(self):
        with patch.object(guard,'full_vm_proof',return_value={'verified':False}):
            with self.assertRaisesRegex(RuntimeError,'FULL_VM'):guard.resources(ROOT,self.recipe(),disk_free=99,cgroup_records=[self.record()])

    def test_only_verified_unbounded_full_vm_can_use_memavailable(self):
        with patch.object(guard,'full_vm_proof',return_value={'verified':True}),patch.object(guard.Path,'read_text',return_value='MemAvailable: 42 kB\n'):
            result=guard.resources(ROOT,self.recipe(),disk_free=99,cgroup_records=[self.record()])
        self.assertTrue(result['hostMemAvailableUsed']);self.assertEqual(result['availableCgroupMemoryBudgetBytes'],42*1024)

    def test_missing_vm_tool_is_not_an_unbounded_exception(self):
        with patch.object(guard.Path,'read_text',return_value=''),patch.object(guard.subprocess,'run',side_effect=FileNotFoundError):
            with self.assertRaises(FileNotFoundError):guard.full_vm_proof()

    def test_full_vm_proof_requires_every_identity_flag(self):
        def text(path):
            return '1 0 0:1 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n' if str(path).endswith('mountinfo') else 'systemd\n'
        with patch.dict(guard.os.environ,{'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted'}),patch.object(guard.Path,'read_text',text),patch.object(guard.os,'readlink',return_value='pid:[same]'),patch.object(guard.subprocess,'run',return_value=SimpleNamespace(returncode=1,stdout='none\n')):
            self.assertTrue(guard.full_vm_proof()['verified'])
            with patch.dict(guard.os.environ,{'RUNNER_ENVIRONMENT':'self-hosted'}):self.assertFalse(guard.full_vm_proof()['verified'])

    def test_public_download_hash_or_size_failure_is_operational(self):
        class Response(io.BytesIO):
            def geturl(self):return 'https://example.invalid/public'
        asset={'url':'https://example.invalid/public','bytes':3,'sha256':hashlib.sha256(b'abc').hexdigest()}
        with tempfile.TemporaryDirectory() as d:
            for index,data in enumerate((b'abcd',b'xyz')):
                with patch.object(bootstrap_ci.urllib.request,'urlopen',return_value=Response(data)):
                    with self.assertRaisesRegex(RuntimeError,'ASSET_'):bootstrap_ci.download(asset,Path(d)/str(index))

    def test_runtime_and_model_identity_checked_without_import(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'model').mkdir();model=root/'model/m';model.write_bytes(b'fake-only')
            package=root/'package';package.mkdir();native=package/'fake';native.write_bytes(b'fake-native')
            identity={'wheel':{'sha256':'wheel'},'files':[{'name':'fake','bytes':11,'sha256':protocol.digest(native)}]}
            (root/'runtime-identity.json').write_text(json.dumps(identity))
            (root/'runtime-binding.json').write_text(json.dumps({'pythonVersion':'3.12.14','wheelSHA256':'wheel','packageRoot':str(package),'pythonExecutable':'fakepython'}))
            recipe={'modelPath':'model/m','modelBytes':9,'modelSHA256':protocol.digest(model)}
            self.assertEqual(guard.bind_runtime(root,recipe)['modelPath'],str(model))
            native.write_bytes(b'tampered')
            with self.assertRaisesRegex(RuntimeError,'FILE_HASH'):guard.bind_runtime(root,recipe)

    def source(self,root):
        (root/'recipe.json').write_text('{}');(root/'payload').write_text('fictional')
        workflow=root/'workflow';workflow.write_text('manual only')
        freeze={'workflowSHA256':protocol.digest(workflow),'pins':[{'path':'payload','bytes':9,'sha256':protocol.digest(root/'payload')}]}
        (root/'packet-freeze.json').write_text(json.dumps(freeze))
        return workflow,protocol.digest(root/'packet-freeze.json'),protocol.digest(root/'recipe.json')

    def test_exact_approval_and_relative_payload_tamper_refusal(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);workflow,packet,recipe=self.source(root)
            bootstrap_ci.verify_source(root,packet,recipe,workflow)
            with self.assertRaisesRegex(RuntimeError,'APPROVAL'):bootstrap_ci.verify_source(root,'0'*64,recipe,workflow)
            (root/'payload').write_text('tampered!')
            with self.assertRaisesRegex(RuntimeError,'PIN_CHANGED'):bootstrap_ci.verify_source(root,packet,recipe,workflow)

    def test_relocation_pin_escape_and_symlink_refused(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);workflow,_,recipe=self.source(root)
            freeze=json.loads((root/'packet-freeze.json').read_text())
            for bad in ('../payload','/payload'):
                freeze['pins'][0]['path']=bad;(root/'packet-freeze.json').write_text(json.dumps(freeze))
                with self.assertRaisesRegex(RuntimeError,'NONPORTABLE'):bootstrap_ci.verify_source(root,protocol.digest(root/'packet-freeze.json'),recipe,workflow)

    def fake_bootstrap(self,root):
        recipe={**self.recipe(),'modelBytes':1,'modelSHA256':'fake','modelPath':'model/m','processDeadlineSeconds':1}
        (root/'recipe.json').write_text(json.dumps(recipe))
        (root/'runtime-identity.json').write_text(json.dumps({'wheel':{'filename':'fake.whl','bytes':1,'sha256':'fake','url':'https://example.invalid'},'files':[]}))
        (root/'legal.json').write_text(json.dumps({'model':{'downloadURL':'https://example.invalid'}}))
        (root/'packet-freeze.json').write_text('{}')
        return recipe

    def test_initial_gate_failure_never_downloads_or_creates_scratch(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.fake_bootstrap(root)
            with patch.object(bootstrap_ci,'verify_source',return_value={'pins':[]}),patch.object(bootstrap_ci.sys,'version_info',(3,12,14)),patch.object(bootstrap_ci,'resources',side_effect=RuntimeError('reserve')),patch.object(bootstrap_ci,'download') as download:
                with self.assertRaisesRegex(RuntimeError,'reserve'):bootstrap_ci.execute(root,'p','r',root/'workflow',root)
            download.assert_not_called();self.assertEqual(list(root.glob('fictional-three-arm-*')),[])

    def test_download_exception_unconditionally_removes_owned_scratch(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.fake_bootstrap(root)
            with patch.object(bootstrap_ci,'verify_source',return_value={'pins':[]}),patch.object(bootstrap_ci.sys,'version_info',(3,12,14)),patch.object(bootstrap_ci,'resources',return_value={}),patch.object(bootstrap_ci,'download',side_effect=RuntimeError('fake-download')),patch('builtins.print'):
                with self.assertRaisesRegex(RuntimeError,'fake-download'):bootstrap_ci.execute(root,'p','r',root/'workflow',root)
            self.assertEqual(list(root.glob('fictional-three-arm-*')),[])

    def test_post_asset_gate_failure_never_launches_then_cleans(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);self.fake_bootstrap(root)
            def fake_download(asset,path):path.write_bytes(b'x')
            with patch.object(bootstrap_ci,'verify_source',return_value={'pins':[]}),patch.object(bootstrap_ci.sys,'version_info',(3,12,14)),patch.object(bootstrap_ci,'resources',side_effect=[{},RuntimeError('posthash-reserve')]),patch.object(bootstrap_ci,'download',side_effect=fake_download),patch.object(bootstrap_ci.subprocess,'run'),patch.object(bootstrap_ci.subprocess,'Popen') as launch,patch('builtins.print'):
                with self.assertRaisesRegex(RuntimeError,'posthash-reserve'):bootstrap_ci.execute(root,'p','r',root/'workflow',root)
            launch.assert_not_called();self.assertEqual(list(root.glob('fictional-three-arm-*')),[])

    def test_cleanup_refuses_unowned_path(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'fictional-three-arm-owned-path.txt').write_text('/tmp/not-owner')
            with self.assertRaisesRegex(RuntimeError,'UNOWNED'):bootstrap_ci.cleanup_only(root)

    def test_controller_failure_reports_unassessed_not_accuracy_fail(self):
        with tempfile.TemporaryDirectory() as d,patch.dict(bootstrap_ci.os.environ,{'RUNNER_TEMP':d}),patch.object(bootstrap_ci.sys,'argv',['bootstrap_ci.py']),patch.object(bootstrap_ci,'execute',side_effect=RuntimeError('synthetic resource hold')),patch('builtins.print') as output:
            with self.assertRaisesRegex(RuntimeError,'resource hold'):bootstrap_ci.main()
        report=json.loads(output.call_args.args[0])
        self.assertEqual(report['controllerDisposition'],'OPERATIONAL_UNASSESSED')
        self.assertEqual(report['modelAccuracy'],'UNASSESSED');self.assertEqual(report['wholeDocumentFormalSlots']['UNASSESSED'],1270)
        self.assertEqual(report['wholeDocumentFormalClocks']['UNASSESSED'],70);self.assertFalse(report['productionAdoption'])

    def test_workflow_manual_only_no_upload_or_cache(self):
        workflow=(ROOT.parents[1]/'.github/workflows/local-ai-three-arm-research.yml').read_text()
        self.assertIn('workflow_dispatch:',workflow)
        for banned in ('upload-artifact','actions/cache','push:','pull_request:','schedule:'):
            self.assertNotIn(banned,workflow)
        self.assertIn('persist-credentials: false',workflow);self.assertIn("python-version: '3.12.14'",workflow)

    def test_published_payload_digest_and_no_host_paths(self):
        provenance=json.loads((ROOT/'local-verified-provenance.json').read_text())
        manifest=provenance['portablePayloadPins']
        encoded=json.dumps(manifest,sort_keys=True,separators=(',',':')).encode()
        self.assertEqual(hashlib.sha256(encoded).hexdigest(),provenance['portablePayloadSHA256'])
        for pin in manifest:
            self.assertEqual(protocol.digest(ROOT/pin['path']),pin['sha256'])
        inputs=json.loads((ROOT/'inputs.json').read_text())
        self.assertEqual(len(inputs['tasks']),12)
        self.assertNotIn('/workspace/',json.dumps(inputs));self.assertNotIn('originalBGRAPath',json.dumps(inputs))
        self.assertEqual(sum(len(t['sources']) for t in inputs['tasks']),156)

    def test_actual_portable_packet_and_worker_whitelist(self):
        freeze=json.loads((ROOT/'packet-freeze.json').read_text())
        bootstrap_ci.verify_source(ROOT,protocol.digest(ROOT/'packet-freeze.json'),protocol.digest(ROOT/'recipe.json'),ROOT.parents[1]/'.github/workflows/local-ai-three-arm-research.yml')
        self.assertEqual(len(freeze['workerPinPaths']),19)
        self.assertNotIn('oracle-evaluation-only.json',freeze['workerPinPaths'])
        self.assertNotIn('score_outputs.py',freeze['workerPinPaths'])
        self.assertFalse(any(Path(p['path']).suffix in ('.so','.dll','.whl','.bgra','.litertlm') for p in freeze['pins']))

if __name__=='__main__':unittest.main(verbosity=2)
