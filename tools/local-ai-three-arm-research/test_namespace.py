"""Pure namespace permission counters; never runs sudo, SDK, model or inference."""
import errno
from pathlib import Path
import subprocess
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import guard

ROOT=Path(__file__).resolve().parent

class Namespace(unittest.TestCase):
    def controls(self,readlink,command=None,overrides=None,environment=None):
        data={'/proc/self/mountinfo':'1 0 0:1 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n','/proc/1/comm':'systemd\n'}
        data.update(overrides or {})
        def text(path):return data[str(path)]
        env={'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted'};env.update(environment or {})
        def default_command(argv,**kw):
            if argv[0]=='systemd-detect-virt':return SimpleNamespace(returncode=1,stdout='none\n')
            return SimpleNamespace(returncode=0,stdout='pid:[123]\n')
        stack=[patch.dict(guard.os.environ,env),patch.object(guard.Path,'read_text',text),patch.object(guard.os,'readlink',side_effect=readlink),patch.object(guard.subprocess,'run',side_effect=command or default_command)]
        entered=[p.start() for p in stack]
        for p in stack:self.addCleanup(p.stop)
        return entered[-1],entered[-2]

    def self_and_permission(self,number=errno.EACCES):
        def link(path):
            if path=='/proc/self/ns/pid':return 'pid:[123]'
            raise PermissionError(number,'synthetic PID1permission')
        return link

    def test_eacces_or_eperm_only_fixed_readonly_bounded_fallback(self):
        for number in (errno.EACCES,errno.EPERM):
            calls,links=self.controls(self.self_and_permission(number))
            proof=guard.full_vm_proof();self.assertTrue(proof['verified'])
            self.assertIn('FIXED_READONLY',proof['namespaceRead']['method'])
            sudo=calls.call_args_list[-1]
            self.assertEqual(sudo.args[0],['/usr/bin/sudo','-n','/usr/bin/readlink','/proc/1/ns/pid'])
            self.assertEqual(sudo.kwargs,{'stdin':subprocess.DEVNULL,'close_fds':True,'capture_output':True,'text':True,'timeout':5})
            self.assertEqual(links.call_args_list[0].args[0],'/proc/self/ns/pid')

    def test_unprivileged_matching_namespace_does_not_invoke_sudo(self):
        calls,_=self.controls(lambda path:'pid:[123]')
        self.assertTrue(guard.full_vm_proof()['verified']);self.assertEqual(calls.call_count,1)

    def test_other_vm_flag_failure_never_reads_namespace_or_invokes_sudo(self):
        variants=[({'/proc/1/comm':'bash\n'},{}),({'/proc/self/mountinfo':'1 0 0:1 /container /sys/fs/cgroup rw - cgroup2 cgroup rw\n'},{}),({}, {'RUNNER_ENVIRONMENT':'self-hosted'}),({}, {'GITHUB_ACTIONS':'false'})]
        for data,env in variants:
            calls,links=self.controls(self.self_and_permission(),overrides=data,environment=env)
            self.assertFalse(guard.full_vm_proof()['verified']);self.assertEqual(calls.call_count,1);links.assert_not_called()

    def test_detected_container_never_uses_permission_fallback(self):
        calls,links=self.controls(self.self_and_permission(),command=lambda argv,**kw:SimpleNamespace(returncode=0,stdout='docker\n'))
        self.assertFalse(guard.full_vm_proof()['verified']);self.assertEqual(calls.call_count,1);links.assert_not_called()

    def test_missing_self_or_nonpermission_pid1_failure_never_invokes_sudo(self):
        for self_value,init_error in ((None,None),('pid:[123]',FileNotFoundError('PID1absent')),('pid:[123]',PermissionError(errno.EIO,'notpermission'))):
            def link(path):
                if path=='/proc/self/ns/pid':
                    if self_value is None:raise FileNotFoundError('selfmissing')
                    return self_value
                raise init_error
            calls,_=self.controls(link)
            with self.assertRaises(OSError):guard.full_vm_proof()
            self.assertEqual(calls.call_count,1)

    def test_malformed_self_or_pid1_namespace_rejects(self):
        for self_value,init_value in (('pid:[same]','pid:[123]'),('pid:[123]','pid:[456]\nextra'),('pid:[0]','pid:[0]')):
            calls,_=self.controls(lambda path:self_value if path=='/proc/self/ns/pid' else init_value)
            with self.assertRaisesRegex(RuntimeError,'NAMESPACE_INVALID'):guard.full_vm_proof()
            self.assertEqual(calls.call_count,1)

    def test_sudo_missing_failure_timeout_and_wrong_namespace_never_pass(self):
        for result in (FileNotFoundError('nosudo'),subprocess.TimeoutExpired('fixedmetadata',5),SimpleNamespace(returncode=1,stdout=''),SimpleNamespace(returncode=0,stdout='pid:[456]\n'),SimpleNamespace(returncode=0,stdout='bad-output')):
            def command(argv,**kw):
                if argv[0]=='systemd-detect-virt':return SimpleNamespace(returncode=1,stdout='none\n')
                if isinstance(result,Exception):raise result
                return result
            self.controls(self.self_and_permission(),command)
            if isinstance(result,Exception):
                with self.assertRaises(type(result)):guard.full_vm_proof()
            elif result.returncode!=0 or result.stdout=='bad-output':
                with self.assertRaises(RuntimeError):guard.full_vm_proof()
            else:self.assertFalse(guard.full_vm_proof()['verified'])

    def test_exact_zero_is_disk_only_no_memory_credit_or_privileged_read(self):
        recipe={'minimumAvailableMemoryBytes':0,'diskReserveBytes':10,'workingAllowanceBytes':0}
        with patch.object(guard,'read_cgroups') as cgroup,patch.object(guard,'full_vm_proof') as vm,patch.object(guard.subprocess,'run') as commands:
            result=guard.resources(ROOT,recipe,disk_free=10)
            self.assertEqual(result['memoryAssessment'],'NOT_REQUESTED');self.assertIsNone(result['availableCgroupMemoryBudgetBytes']);self.assertFalse(result['hostMemAvailableUsed'])
            with self.assertRaisesRegex(RuntimeError,'DISK'):guard.resources(ROOT,recipe,disk_free=9)
        cgroup.assert_not_called();vm.assert_not_called();commands.assert_not_called()

    def test_bool_float_negative_missing_cannot_take_disk_only_branch(self):
        for value in (False,0.0,-1,None):
            recipe={'diskReserveBytes':10,'workingAllowanceBytes':0}
            if value is not None:recipe['minimumAvailableMemoryBytes']=value
            with patch.object(guard,'read_cgroups') as cgroup:
                with self.assertRaisesRegex(RuntimeError,'MEMORY_REQUIREMENT_INVALID'):guard.resources(ROOT,recipe,disk_free=99)
                cgroup.assert_not_called()

    def test_positive_preload_requirement_still_demands_fresh_proof(self):
        recipe={'minimumAvailableMemoryBytes':4831838208,'diskReserveBytes':10,'workingAllowanceBytes':0}
        for _ in range(2):
            with patch.object(guard,'read_cgroups',side_effect=PermissionError('proofunavailable')) as observe:
                with self.assertRaises(PermissionError):guard.resources(ROOT,recipe,disk_free=9999999999)
                observe.assert_called_once()

    def test_finite_cgroup_never_uses_namespace_fallback(self):
        stat={k:0 for k in ('file','shmem','inactive_file','file_dirty','file_writeback','file_mapped','unevictable')}
        records=[{'max':4831838208,'high':None,'current':0,'stat':stat,'path':'finite'}]
        with patch.object(guard,'full_vm_proof') as vm:
            result=guard.resources(ROOT,{'minimumAvailableMemoryBytes':4831838208,'diskReserveBytes':10,'workingAllowanceBytes':0},disk_free=10,cgroup_records=records)
        vm.assert_not_called();self.assertFalse(result['hostMemAvailableUsed'])

if __name__=='__main__':unittest.main(verbosity=2)
