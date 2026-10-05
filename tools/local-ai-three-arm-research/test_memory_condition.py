"""Prospective resource-only controls, no host/model work or memory allocation."""
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import guard

ROOT=Path(__file__).resolve().parent

class MemoryCondition(unittest.TestCase):
    def record(self,budget):
        return {'path':'synthetic','max':budget,'high':None,'current':0,
                'stat':{k:0 for k in ('file','shmem','inactive_file','file_dirty','file_writeback','file_mapped','unevictable')}}

    def test_explicit_new_cap_and_headroom_quality_limits_unchanged(self):
        r=json.loads((ROOT/'recipe.json').read_text())
        self.assertEqual(r['maximumRSSBytes'],6*1024**3);self.assertEqual(r['minimumAvailableMemoryBytes'],7*1024**3)
        self.assertTrue(r['prospectiveResourceCondition']['resourceConditionChanged']);self.assertFalse(r['prospectiveResourceCondition']['qualityThresholdsChanged'])
        self.assertEqual(r['contextTokens'],8192);self.assertEqual(r['sampler']['maximumOutputTokens'],768)
        self.assertEqual((r['maximumTasks'],r['maximumCalls'],r['processDeadlineSeconds'],r['loadDeadlineSeconds'],r['callDeadlineSeconds']),(12,36,1200,180,60))
        self.assertEqual(r['nativeThreshold'],.8);self.assertFalse(r['nativeWholeDocumentGate']);self.assertFalse(r['productionAdoption'])

    def test_old_headroom_or_one_byte_below_new_stops_before_dependency(self):
        r=json.loads((ROOT/'recipe.json').read_text())
        for budget in (4831838208,7516192767):
            with patch.object(guard,'full_vm_proof') as proof:
                with self.assertRaisesRegex(RuntimeError,'RESOURCE_PRECONDITION_MEMORY'):
                    guard.resources(ROOT,r,disk_free=r['diskReserveBytes']+r['workingAllowanceBytes'],cgroup_records=[self.record(budget)])
            proof.assert_not_called()

    def test_exact_new_finite_headroom_passes_without_host_credit(self):
        r=json.loads((ROOT/'recipe.json').read_text())
        with patch.object(guard,'full_vm_proof') as proof:
            measured=guard.resources(ROOT,r,disk_free=r['diskReserveBytes']+r['workingAllowanceBytes'],cgroup_records=[self.record(7516192768)])
        proof.assert_not_called();self.assertFalse(measured['hostMemAvailableUsed']);self.assertEqual(measured['availableCgroupMemoryBudgetBytes'],7516192768)

    def test_disk_floor_not_reduced_by_memory_configuration(self):
        r=json.loads((ROOT/'recipe.json').read_text());self.assertEqual(r['diskReserveBytes']+r['workingAllowanceBytes'],3321888768)
        with self.assertRaisesRegex(RuntimeError,'RESOURCE_PRECONDITION_DISK'):
            guard.resources(ROOT,r,disk_free=3321888767,cgroup_records=[self.record(7516192768)])

    def test_zero_request_has_no_manufactured_memory_credit(self):
        r=json.loads((ROOT/'recipe.json').read_text());r['minimumAvailableMemoryBytes']=0
        with patch.object(guard,'read_cgroups') as cg,patch.object(guard,'full_vm_proof') as proof:
            measured=guard.resources(ROOT,r,disk_free=3321888768)
        cg.assert_not_called();proof.assert_not_called();self.assertIsNone(measured['availableCgroupMemoryBudgetBytes']);self.assertEqual(measured['memoryAssessment'],'NOT_REQUESTED')

if __name__=='__main__':unittest.main(verbosity=2)
