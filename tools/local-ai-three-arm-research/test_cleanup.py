"""Synthetic ownership/diagnostic controls; no model, privilege, or real signals."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace
import bootstrap_ci as b
import run_once

ROOT=Path(__file__).resolve().parent

class Cleanup(unittest.TestCase):
    def packet(self,d):
        p=Path(d)/'packet';p.mkdir();(p/'worker-process-group.json').write_text('{"group":1234}')
        return p

    def test_foreign_pid1_filtered_before_environ(self):
        with patch.object(b.Path,'iterdir',return_value=[Path('/proc/1'),Path('/proc/1234')]),patch.object(b,'proc_state',side_effect=[{'group':1,'session':1,'state':'S'},{'group':1234,'session':1234,'state':'S'}]),patch.object(b,'owned_pid',return_value=True) as owner:
            result=b.owned_group_members(1234,ROOT)
        owner.assert_called_once_with(1234,ROOT);self.assertEqual(result['members'],[1234]);self.assertEqual(result['errors'],[])

    def test_denied_same_group_is_not_clean_or_signalled(self):
        with tempfile.TemporaryDirectory() as d:
            p=self.packet(d)
            with patch.object(b.Path,'iterdir',return_value=[Path('/proc/1234')]),patch.object(b,'proc_state',return_value={'group':1234,'session':1234,'state':'S'}),patch.object(b,'owned_pid',side_effect=PermissionError('denied')),patch.object(b.os,'killpg') as kill:
                result=b.terminate_owned(None,p)
            self.assertFalse(result['complete']);kill.assert_not_called()

    def test_foreign_marker_same_group_never_signalled(self):
        with tempfile.TemporaryDirectory() as d:
            p=self.packet(d)
            with patch.object(b.Path,'iterdir',return_value=[Path('/proc/1234')]),patch.object(b,'proc_state',return_value={'group':1234,'session':1234,'state':'S'}),patch.object(b,'owned_pid',return_value=False),patch.object(b.os,'killpg') as kill:
                result=b.terminate_owned(None,p)
            self.assertFalse(result['complete']);kill.assert_not_called()

    def test_unknown_stat_is_fail_closed(self):
        with patch.object(b.Path,'iterdir',return_value=[Path('/proc/5')]),patch.object(b,'proc_state',side_effect=PermissionError()),patch.object(b,'owned_pid') as owner:
            result=b.owned_group_members(1234,ROOT)
        self.assertTrue(result['errors']);owner.assert_not_called()

    def test_zombie_cannot_hold_live_model_and_needs_no_environ(self):
        with patch.object(b.Path,'iterdir',return_value=[Path('/proc/1234')]),patch.object(b,'proc_state',return_value={'group':1234,'session':1234,'state':'Z'}),patch.object(b,'owned_pid') as owner:
            result=b.owned_group_members(1234,ROOT)
        self.assertEqual(result['zombiePids'],[1234]);self.assertEqual(result['members'],[]);owner.assert_not_called()

    def test_confirmed_group_signalled_and_disappearance_checked(self):
        with tempfile.TemporaryDirectory() as d:
            p=self.packet(d)
            with patch.object(b,'owned_group_members',side_effect=[{'members':[1234],'errors':[],'zombiePids':[]},{'members':[],'errors':[],'zombiePids':[]}]),patch.object(b.os,'killpg') as kill:
                result=b.terminate_owned(None,p)
            self.assertTrue(result['complete']);kill.assert_called_once_with(1234,b.signal.SIGKILL)

    def test_runner_reused_before_term_is_never_signalled(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);(p/'controller-runner.json').write_text('{"pid":1234,"startTicks":8}')
            with patch.object(b,'proc_state',side_effect=[{'state':'S','startTicks':8},{'state':'S','startTicks':9}]),patch.object(b,'owned_pid',return_value=True),patch.object(b.os,'kill') as kill:
                result=b.terminate_owned(None,p)
            self.assertFalse(result['complete']);kill.assert_not_called()

    def test_runner_reused_during_wait_never_receives_kill(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);(p/'controller-runner.json').write_text('{"pid":1234,"startTicks":8}')
            with patch.object(b,'proc_state',side_effect=[{'state':'S','startTicks':8},{'state':'S','startTicks':8},{'state':'S','startTicks':9}]),patch.object(b,'owned_pid',return_value=True),patch.object(b.os,'kill') as kill:
                result=b.terminate_owned(None,p)
            self.assertFalse(result['complete']);kill.assert_called_once_with(1234,b.signal.SIGTERM)

    def test_runner_marker_lost_during_wait_never_receives_kill(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);(p/'controller-runner.json').write_text('{"pid":1234,"startTicks":8}')
            with patch.object(b,'proc_state',return_value={'state':'S','startTicks':8}),patch.object(b,'owned_pid',side_effect=[True,True,False]),patch.object(b.os,'kill') as kill:
                result=b.terminate_owned(None,p)
            self.assertFalse(result['complete']);kill.assert_called_once_with(1234,b.signal.SIGTERM)

    def test_lingering_owned_group_is_incomplete(self):
        with tempfile.TemporaryDirectory() as d:
            p=self.packet(d)
            with patch.object(b,'owned_group_members',return_value={'members':[1234],'errors':[],'zombiePids':[]}),patch.object(b.time,'monotonic',side_effect=[0,6]),patch.object(b.os,'killpg'):
                result=b.terminate_owned(None,p)
            self.assertFalse(result['complete']);self.assertEqual(result['remainingOwnedPids'],[1234])

    def test_group_record_failure_known_launched_group_still_checked(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);(p/'preflight-failure.json').write_text('{"dependentExecutionStarted":true,"workerPID":1234,"launchCleanup":{"complete":false}}')
            with patch.object(b,'owned_group_members',return_value={'members':[1234],'errors':['owned uninspectable'],'zombiePids':[]}),patch.object(b.os,'killpg') as kill:
                result=b.terminate_owned(None,p)
            self.assertFalse(result['complete']);kill.assert_not_called()

    def test_failed_group_record_incomplete_launch_cleanup_never_deletes_live_scratch(self):
        with tempfile.TemporaryDirectory() as d:
            s=Path(d)/'fictional-three-arm-test';s.mkdir();p=s/'packet';p.mkdir();m=Path(d)/'marker';m.write_text(str(s))
            (p/'probe-started.json').write_text('{}');(p/'preflight-failure.json').write_text('{"dependentExecutionStarted":true,"workerPID":1234,"launchCleanup":{"complete":false}}');(p/'worker-stderr.log').write_text('launch record failure')
            with patch.object(b,'owned_group_members',return_value={'members':[1234],'errors':[],'zombiePids':[]}),patch.object(b.time,'monotonic',side_effect=[0,6]),patch.object(b.os,'killpg'),patch.object(b.shutil,'rmtree') as remove,patch('builtins.print') as output:
                self.assertFalse(b.finish_owned_scratch(None,p,s,m,True,{}))
            remove.assert_not_called();self.assertTrue(s.exists());self.assertTrue(m.exists());self.assertTrue(any('launch record failure' in x.args[0] for x in output.call_args_list))

    def test_valid_preflight_selected_group_still_requires_owned_marker_scan(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);(p/'preflight-failure.json').write_text('{"dependentExecutionStarted":true,"workerPID":1234}')
            with patch.object(b,'owned_group_members',side_effect=[{'members':[1234],'errors':[],'zombiePids':[]},{'members':[],'errors':[],'zombiePids':[]}]) as scan,patch.object(b.os,'killpg') as kill:
                result=b.terminate_owned(None,p)
            self.assertTrue(result['complete']);self.assertEqual(scan.call_args.args,(1234,p));kill.assert_called_once_with(1234,b.signal.SIGKILL)

    def test_missing_started_group_proof_finally_retains_marker_and_scratch(self):
        with tempfile.TemporaryDirectory() as d:
            s=Path(d)/'fictional-three-arm-test';s.mkdir();p=s/'packet';p.mkdir();m=Path(d)/'marker';m.write_text(str(s));(p/'probe-started.json').write_text('{}')
            with patch.object(b.shutil,'rmtree') as remove,patch('builtins.print'):
                self.assertFalse(b.finish_owned_scratch(None,p,s,m,True,{}))
            remove.assert_not_called();self.assertTrue(m.exists());self.assertTrue(s.exists())

    def test_unknown_or_malformed_started_group_holds_scratch(self):
        for document in ({'dependentExecutionStarted':True,'workerPID':None},{'dependentExecutionStarted':True,'workerPID':False},None):
            with tempfile.TemporaryDirectory() as d:
                p=Path(d);(p/'probe-started.json').write_text('{}')
                if document is not None:(p/'preflight-failure.json').write_text(json.dumps(document))
                result=b.terminate_owned(None,p);self.assertFalse(result['complete'])

    def test_incomplete_cleanup_prints_logs_and_retains_scratch(self):
        with tempfile.TemporaryDirectory() as d:
            s=Path(d)/'scratch';s.mkdir();p=s/'packet';p.mkdir();m=Path(d)/'marker';m.write_text(str(s));(p/'worker-stderr.log').write_text('fictional native error')
            with patch.object(b,'terminate_owned',return_value={'complete':False,'errors':['owned live']}),patch('builtins.print') as out,patch.object(b.shutil,'rmtree') as remove:
                complete=b.finish_owned_scratch(None,p,s,m,True,{},'PRIMARY_RESOURCE')
            self.assertFalse(complete);remove.assert_not_called();self.assertTrue(m.exists());self.assertTrue(any('fictional native error' in x.args[0] for x in out.call_args_list))

    def test_cleanup_exception_preserves_other_diagnostics(self):
        with tempfile.TemporaryDirectory() as d:
            s=Path(d);p=s/'packet';p.mkdir();(p/'worker-stderr.log').write_text('error retained')
            with patch.object(b,'terminate_owned',side_effect=PermissionError('owned denial')),patch('builtins.print') as out:
                self.assertFalse(b.finish_owned_scratch(None,p,s,s/'marker',False,{},'RSS'))
            self.assertTrue(any('error retained' in x.args[0] for x in out.call_args_list));self.assertTrue(s.exists())

    def test_oversized_first_response_still_emits_stderr(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);(p/'responses.jsonl').write_text('x'*100);(p/'worker-stderr.log').write_text('last diagnostic')
            with patch('builtins.print') as out:result=b.print_research_logs(p,{'maximumOutputBytes':8,'maximumPrintedWorkerLogBytes':20})
            self.assertFalse(result['complete']);self.assertTrue(any('last diagnostic' in x.args[0] for x in out.call_args_list));self.assertEqual(result['files'][0]['printedBytes'],8)

    def test_unreadable_first_file_still_emits_stderr(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);(p/'responses.jsonl').symlink_to(p/'worker-stderr.log');(p/'worker-stderr.log').write_text('retained')
            with patch('builtins.print') as out:result=b.print_research_logs(p,{})
            self.assertFalse(result['complete']);self.assertTrue(any('retained' in x.args[0] for x in out.call_args_list))

    def test_diagnostics_always_precede_scratch_deletion(self):
        with tempfile.TemporaryDirectory() as d:
            s=Path(d)/'scratch';s.mkdir();p=s/'packet';p.mkdir();m=Path(d)/'marker';m.write_text(str(s));order=[]
            with patch.object(b,'terminate_owned',return_value={'complete':True,'errors':[]}),patch.object(b,'print_research_logs',side_effect=lambda *a:order.append('logs') or {'complete':True}),patch.object(b.shutil,'rmtree',side_effect=lambda path:order.append('delete')),patch('builtins.print'):
                b.finish_owned_scratch(None,p,s,m,True,{})
            self.assertEqual(order,['logs','delete'])

    def test_started_worker_record_write_failure_has_unknown_model_counts(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);(p/'recipe.json').write_text((ROOT/'recipe.json').read_text());original=Path.write_text;fake=SimpleNamespace(pid=1234)
            def write(path,*args,**kwargs):
                if path.name=='worker-process-group.json':raise PermissionError('synthetic record failure')
                return original(path,*args,**kwargs)
            with patch.object(run_once,'ROOT',p),patch.object(run_once,'verify_packet',return_value={'sourceFreezeSHA256':'fake'}),patch.object(run_once,'bind_runtime',side_effect=lambda p,r:r),patch.object(run_once,'resources',return_value={}),patch.object(run_once,'subreaper'),patch.object(run_once.subprocess,'Popen',return_value=fake),patch.object(run_once,'cleanup_group',return_value={'complete':False,'errors':['live']}),patch.object(Path,'write_text',write):
                with self.assertRaises(SystemExit):run_once.main()
            receipt=json.loads((p/'preflight-failure.json').read_text());self.assertTrue(receipt['dependentExecutionStarted']);self.assertEqual(receipt['modelLoads'],'UNKNOWN');self.assertEqual(receipt['inferenceCalls'],'UNKNOWN')
            self.assertFalse(receipt['launchCleanup']['complete'])

if __name__=='__main__':unittest.main(verbosity=2)
