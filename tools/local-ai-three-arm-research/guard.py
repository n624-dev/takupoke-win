"""Pure fail-closed approval/resource boundary shared by runner and worker."""
import json
import os
import subprocess
from pathlib import Path
from protocol import digest

def cgroup_budget(records):
    if not records:
        raise RuntimeError('CGROUP_MEMORY_NOT_OBSERVED')
    assessed = []
    for record in records:
        limits = [v for v in (record['max'], record['high']) if type(v) is int and v >= 0]
        if not limits:
            continue
        stat = record['stat']
        required = ('file', 'shmem', 'inactive_file', 'file_dirty', 'file_writeback', 'file_mapped', 'unevictable')
        if any(type(stat.get(k)) is not int or stat[k] < 0 for k in required) or type(record['current']) is not int or record['current'] < 0:
            raise RuntimeError('CGROUP_MEMORY_STAT_INCOMPLETE')
        # Only clean, unmapped, inactive disk-backed file cache is allowed.
        # No tmpfs/shmem, swap, slab or active-file allowance; no global eviction.
        clean_backed = max(0, stat['file'] - stat['shmem'] - stat['file_dirty'] - stat['file_writeback'] - stat['file_mapped'] - stat['unevictable'])
        reclaimable = min(stat['inactive_file'], clean_backed)
        limit = min(limits)
        budget = max(0, limit - record['current'] + reclaimable)
        assessed.append({**record, 'effectiveLimitBytes': limit, 'conservativeReclaimableBytes': reclaimable, 'availableBudgetBytes': budget})
    if not assessed:
        raise RuntimeError('CGROUP_FINITE_MEMORY_LIMIT_NOT_OBSERVED')
    return min(r['availableBudgetBytes'] for r in assessed), assessed

def read_cgroups():
    mount = Path('/sys/fs/cgroup')
    lines = Path('/proc/self/cgroup').read_text().splitlines()
    paths = [line[3:] for line in lines if line.startswith('0::')]
    if len(paths) != 1 or '..' in Path(paths[0]).parts:
        raise RuntimeError('CGROUP_V2_PATH_NOT_VERIFIED')
    current_path = mount / paths[0].lstrip('/')
    records = []
    while True:
        core=[(current_path/name).exists() for name in ('memory.current','memory.max','memory.high')]
        if current_path==mount and not any(core):
            mount_roots=[line.split()[3] for line in Path('/proc/self/mountinfo').read_text().splitlines() if ' - cgroup2 ' in line and line.split()[4]=='/sys/fs/cgroup']
            controllers=(mount/'cgroup.controllers').read_text().split()
            if mount_roots!=['/'] or 'memory' not in controllers:raise RuntimeError('HIERARCHY_ROOT_ABSENCE_NOT_PROVEN')
            # A global kernel hierarchy root is non-limiting, not a fabricated
            # memory observation. memory.stat may legitimately exist there.
            records.append({'path':str(mount),'max':None,'high':None,'current':None,'stat':None,
                            'hierarchyRootNonLimiting':True,'completeAncestorWalk':True,
                            'rootAbsenceProof':{'fullMountRoot':True,'memoryControllerAvailable':True,'currentMaxHighAllAbsent':True}})
            break
        if not all(core):raise RuntimeError('CGROUP_MEMORY_FILES_PARTIALLY_MISSING')
        before = int((current_path / 'memory.current').read_text())
        stat = {k: int(v) for k, v in (line.split() for line in (current_path / 'memory.stat').read_text().splitlines())}
        after = int((current_path / 'memory.current').read_text())
        def limit(name):
            value = (current_path / name).read_text().strip()
            return None if value == 'max' else int(value)
        records.append({'path': str(current_path), 'max': limit('memory.max'), 'high': limit('memory.high'), 'current': max(before, after), 'stat': stat})
        if current_path == mount:
            records[-1]['completeAncestorWalk']=True
            break
        current_path = current_path.parent
    return records

def resources(root, recipe, disk_free=None, cgroup_records=None):
    if disk_free is None:
        stat = os.statvfs(root)
        disk_free = stat.f_bavail * stat.f_frsize
    records=read_cgroups() if cgroup_records is None else cgroup_records
    if not records:raise RuntimeError('CGROUP_MEMORY_NOT_OBSERVED')
    if any(k not in r or (r[k] is not None and (type(r[k]) is not int or r[k]<0)) for r in records for k in ('max','high')):
        raise RuntimeError('CGROUP_LIMIT_INVALID')
    finite=any(type(r.get(k)) is int and r[k]>=0 for r in records for k in ('max','high'))
    if finite:
        available_memory,cgroups=cgroup_budget(records)
        host_used=False
    else:
        # A VM exception is permitted ONLY after every visible ancestor is
        # unbounded and full host visibility is independently established.
        if not records[-1].get('completeAncestorWalk'):raise RuntimeError('UNBOUNDED_ANCESTOR_WALK_INCOMPLETE')
        proof=full_vm_proof()
        if not proof['verified']:raise RuntimeError('UNBOUNDED_CGROUP_WITHOUT_FULL_VM_PROOF')
        values=dict(line.split(':',1) for line in Path('/proc/meminfo').read_text().splitlines())
        available_memory=int(values['MemAvailable'].split()[0])*1024
        cgroups={'unboundedVisibleAncestors':records,'fullVMProof':proof}
        host_used=True
    if disk_free < recipe['diskReserveBytes'] + recipe['workingAllowanceBytes']:
        raise RuntimeError('RESOURCE_PRECONDITION_DISK; no dependent execution')
    if available_memory < recipe['minimumAvailableMemoryBytes']:
        raise RuntimeError('RESOURCE_PRECONDITION_MEMORY; no dependent execution')
    return {'availableDiskBytes': disk_free, 'availableCgroupMemoryBudgetBytes': available_memory, 'cgroups': cgroups,
            'hostMemAvailableUsed': host_used, 'reclaimPolicy': 'finitecgroups:clean-unmapped-inactive-disk-file-only; unboundedfullyverifiedVM:hostMemAvailable'}

def full_vm_proof():
    mount_roots=[line.split()[3] for line in Path('/proc/self/mountinfo').read_text().splitlines() if ' - cgroup2 ' in line and line.split()[4]=='/sys/fs/cgroup']
    check=subprocess.run(['systemd-detect-virt','--container'],capture_output=True,text=True,timeout=5)
    flags={'githubActions':os.environ.get('GITHUB_ACTIONS')=='true','githubHosted':os.environ.get('RUNNER_ENVIRONMENT')=='github-hosted',
           'cgroupMountFullRoot':mount_roots==['/'],'PID1Systemd':Path('/proc/1/comm').read_text().strip()=='systemd',
           'samePIDNamespaceAsInit':os.readlink('/proc/1/ns/pid')==os.readlink('/proc/self/ns/pid'),
           'noContainerDetected':check.returncode==1 and check.stdout.strip()=='none'}
    return {'verified':all(flags.values()),'flags':flags,'containerCheckExit':check.returncode}

def bind_runtime(root,recipe):
    root=Path(root);binding=json.loads((root/'runtime-binding.json').read_text())
    expected=json.loads((root/'runtime-identity.json').read_text())
    if binding['pythonVersion']!='3.12.14' or binding['wheelSHA256']!=expected['wheel']['sha256']:
        raise RuntimeError('NEW_RUNTIME_IDENTITY_MISMATCH')
    for item in expected['files']:
        path=Path(binding['packageRoot'])/item['name']
        if path.stat().st_size!=item['bytes'] or digest(path)!=item['sha256']:raise RuntimeError('NEW_RUNTIME_FILE_HASH_MISMATCH')
    model=root/recipe['modelPath']
    if model.stat().st_size!=recipe['modelBytes'] or digest(model)!=recipe['modelSHA256']:raise RuntimeError('NEW_MODEL_HASH_MISMATCH')
    return {**recipe,'modelPath':str(model),'pythonPath':binding['pythonExecutable']}

def verify_packet(root, recipe, verify_pins=True, pin_role='coordinator'):
    root = Path(root)
    freeze_path = root / 'packet-freeze.json'
    approval = json.loads((root / 'root-inference-approval.json').read_text())
    if approval.get('action') != 'ONE_LOCAL_GEMMA4_THREE_ARM_DEVELOPMENT_COMPONENT_PROBE' or approval.get('sourceFreezeSHA256') != digest(freeze_path) or approval.get('recipeSHA256') != digest(root / 'recipe.json'):
        raise RuntimeError('EXACT_ROOT_GO_MISSING_OR_MISMATCHED')
    for key, value in {'plannedMaximumCalls': 36, 'newRecognizerCalls': 0, 'productionAdoption': False, 'fullDocumentAssessment': False}.items():
        if type(approval.get(key)) is not type(value) or approval[key] != value:
            raise RuntimeError('ROOT_GO_SCOPE_MISMATCH')
    freeze = json.loads(freeze_path.read_text())
    if verify_pins:
        if pin_role not in ('coordinator', 'worker'):
            raise RuntimeError('UNKNOWN_PIN_ROLE')
        worker_paths = set(freeze['workerPinPaths'])
        for pin in freeze['pins']:
            if pin_role == 'worker' and pin['path'] not in worker_paths:
                continue
            path = root / pin['path']
            if Path(pin['path']).is_absolute() or '..' in Path(pin['path']).parts or path.is_symlink():
                raise RuntimeError('NONPORTABLE_OR_INDIRECT_PIN')
            if not path.is_file() or path.stat().st_size != pin['bytes'] or digest(path) != pin['sha256']:
                raise RuntimeError('FROZEN_PIN_CHANGED: ' + str(path))
    return {'approvalSHA256': digest(root / 'root-inference-approval.json'), 'sourceFreezeSHA256': digest(freeze_path), 'recipeSHA256': digest(root / 'recipe.json')}
