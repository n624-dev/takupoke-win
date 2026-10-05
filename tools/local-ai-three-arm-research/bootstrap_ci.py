"""Manual-approved portable controller. Public assets first, no model import here."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import urllib.request
from guard import resources
from protocol import digest

ROOT = Path(__file__).resolve().parent

def read(path):
    return json.loads(Path(path).read_text())

def verify_source(root, packet_sha, recipe_sha, workflow):
    if len(packet_sha)!=64 or len(recipe_sha)!=64 or any(c not in '0123456789abcdef' for c in packet_sha+recipe_sha):
        raise RuntimeError('EXACT_MANUAL_APPROVAL_HASH_REQUIRED')
    if digest(root/'packet-freeze.json')!=packet_sha or digest(root/'recipe.json')!=recipe_sha:
        raise RuntimeError('MANUAL_APPROVAL_SOURCE_MISMATCH')
    freeze=read(root/'packet-freeze.json')
    if digest(workflow)!=freeze['workflowSHA256']:raise RuntimeError('WORKFLOW_SOURCE_MISMATCH')
    for pin in freeze['pins']:
        relative=Path(pin['path']);path=root/relative
        if relative.is_absolute() or '..' in relative.parts or path.is_symlink() or not path.is_file():
            raise RuntimeError('NONPORTABLE_PIN')
        if path.stat().st_size!=pin['bytes'] or digest(path)!=pin['sha256']:raise RuntimeError('PORTABLE_PIN_CHANGED')
    return freeze

def download(asset, target):
    # Public pinned provenance only, before any document payload is consumed.
    if not asset['url'].startswith('https://'):raise RuntimeError('NON_HTTPS_PUBLIC_ASSET')
    total=0;hashed=hashlib.sha256();started=time.monotonic()
    request=urllib.request.Request(asset['url'],headers={'User-Agent':'fictional-local-ai-research/1'})
    with urllib.request.urlopen(request,timeout=60) as response, target.open('xb') as stream:
        if not response.geturl().startswith('https://'):raise RuntimeError('NON_HTTPS_ASSET_REDIRECT')
        while True:
            if time.monotonic()-started>300:raise RuntimeError('PUBLIC_ASSET_DOWNLOAD_DEADLINE')
            block=response.read(1024*1024)
            if not block:break
            total+=len(block)
            if total>asset['bytes']:raise RuntimeError('ASSET_SIZE_OVERFLOW')
            hashed.update(block);stream.write(block)
    if total!=asset['bytes'] or hashed.hexdigest()!=asset['sha256']:raise RuntimeError('PUBLIC_ASSET_HASH_MISMATCH')

def owned_pid(pid,packet):
    try:return ('FICTIONAL_RESEARCH_SCRATCH='+str(packet.parent)).encode() in Path('/proc',str(pid),'environ').read_bytes().split(b'\0')
    except FileNotFoundError:return False

def terminate_owned(process, packet):
    # Runner normally handles its separate worker group; this is an outer guard.
    if process is not None and process.poll() is None:
        process.terminate()
        try:process.wait(timeout=12)
        except subprocess.TimeoutExpired:
            process.kill();process.wait(timeout=5)
    elif process is None and (packet/'controller-runner.json').exists():
        pid=read(packet/'controller-runner.json')['pid']
        if type(pid) is not int or pid<=1:raise RuntimeError('INVALID_OWNED_RUNNER_PID')
        if owned_pid(pid,packet):
            try:os.kill(pid,signal.SIGTERM)
            except ProcessLookupError:pass
            else:
                deadline=time.monotonic()+12
                while owned_pid(pid,packet) and time.monotonic()<deadline:time.sleep(.1)
                if owned_pid(pid,packet):
                    try:os.kill(pid,signal.SIGKILL)
                    except ProcessLookupError:pass
    group_file=packet/'worker-process-group.json'
    if group_file.exists():
        group=read(group_file)['group']
        if type(group) is not int or group<=1:raise RuntimeError('INVALID_OWNED_GROUP')
        # Stale numeric records must never target a reused, unrelated process.
        members=[]
        for entry in Path('/proc').iterdir():
            if not entry.name.isdecimal() or not owned_pid(int(entry.name),packet):continue
            try:
                stat=(entry/'stat').read_text().rsplit(')',1)[1].split()
                if int(stat[2])==group:members.append(int(entry.name))
            except FileNotFoundError:pass
        if members:
            try:os.killpg(group,signal.SIGKILL)
            except ProcessLookupError:pass
            deadline=time.monotonic()+5
            while any(owned_pid(pid,packet) for pid in members):
                if time.monotonic()>=deadline:raise RuntimeError('OWNED_CHILD_CLEANUP_INCOMPLETE')
                time.sleep(.05)

def execute(root, packet_sha, recipe_sha, workflow, scratch_parent):
    freeze=verify_source(root,packet_sha,recipe_sha,workflow)
    if sys.version_info[:3]!=(3,12,14):raise RuntimeError('EXACT_PYTHON_3_12_14_REQUIRED')
    if sys.platform!='linux' or os.uname().machine!='x86_64':raise RuntimeError('LINUX_X86_64_SECCOMP_PLATFORM_REQUIRED')
    recipe=read(root/'recipe.json');identity=read(root/'runtime-identity.json');legal=read(root/'legal.json')
    # Only resource/provenance source is read before exact assets are available.
    initial={**recipe,'workingAllowanceBytes':recipe['workingAllowanceBytes']+recipe['modelBytes']+identity['wheel']['bytes']+209715200}
    resources(scratch_parent,initial)
    scratch=Path(tempfile.mkdtemp(prefix='fictional-three-arm-',dir=scratch_parent));process=None;packet=scratch/'packet'
    # Explicit path retained for an independent workflow always-cleanup step.
    marker=Path(scratch_parent)/'fictional-three-arm-owned-path.txt'
    if marker.exists():
        shutil.rmtree(scratch);raise RuntimeError('EXCLUSIVE_SCRATCH_ALREADY_EXISTS')
    marker.write_text(str(scratch)+'\n')
    try:
        model=legal['model']
        model_path=scratch/'gemma-4-E2B-it.litertlm'
        download({'url':model['downloadURL'],'bytes':recipe['modelBytes'],'sha256':recipe['modelSHA256']},model_path)
        wheel=identity['wheel'];wheel_path=scratch/wheel['filename'];download(wheel,wheel_path)
        venv=scratch/'runtime'
        subprocess.run([sys.executable,'-m','venv',str(venv)],check=True,timeout=60)
        python=venv/'bin/python'
        subprocess.run([str(python),'-m','pip','--disable-pip-version-check','install','--no-index','--no-deps','--no-cache-dir',str(wheel_path)],check=True,timeout=120)
        package=venv/'lib/python3.12/site-packages'
        for item in identity['files']:
            path=package/item['name']
            if path.stat().st_size!=item['bytes'] or digest(path)!=item['sha256']:raise RuntimeError('INSTALLED_RUNTIME_IDENTITY_MISMATCH')
        # Input/crop payload is relocated only AFTER both pinned public assets.
        packet.mkdir()
        for pin in freeze['pins']:
            destination=packet/pin['path'];destination.parent.mkdir(parents=True,exist_ok=True)
            shutil.copyfile(root/pin['path'],destination)
        shutil.copyfile(root/'packet-freeze.json',packet/'packet-freeze.json')
        (packet/'model').mkdir();shutil.move(str(model_path),packet/recipe['modelPath'])
        wheel_path.unlink()
        (packet/'runtime-binding.json').write_text(json.dumps({'pythonVersion':'3.12.14','pythonExecutable':str(python),'packageRoot':str(package),'wheelSHA256':wheel['sha256'],'newPortableRuntimeFilesVerified':True})+'\n')
        (packet/'root-inference-approval.json').write_text(json.dumps({'action':'ONE_LOCAL_GEMMA4_THREE_ARM_DEVELOPMENT_COMPONENT_PROBE','sourceFreezeSHA256':packet_sha,'recipeSHA256':recipe_sha,'plannedMaximumCalls':36,'newRecognizerCalls':0,'productionAdoption':False,'fullDocumentAssessment':False})+'\n')
        # Asset hashing warms file cache: this measured gate is mandatory now.
        resources(packet,recipe)
        env={k:v for k,v in os.environ.items() if k not in ('GITHUB_TOKEN','GH_TOKEN','ACTIONS_RUNTIME_TOKEN','ACTIONS_ID_TOKEN_REQUEST_TOKEN')}
        env['FICTIONAL_RESEARCH_SCRATCH']=str(scratch)
        process=subprocess.Popen([str(python),str(packet/'run_once.py')],cwd=packet,env=env,stdin=subprocess.DEVNULL,close_fds=True)
        (packet/'controller-runner.json').write_text(json.dumps({'pid':process.pid})+'\n')
        try:code=process.wait(timeout=recipe['processDeadlineSeconds']+60)
        except subprocess.TimeoutExpired:
            terminate_owned(process,packet);raise RuntimeError('OUTER_CONTROLLER_DEADLINE')
        terminate_owned(process,packet)
        # Research logs only; no artifacts/cache or inference admission.
        for name in ('preflight-failure.json','execution-receipt.json','comparison-report.json'):
            path=packet/name
            if path.exists():print(name+'\n'+path.read_text(),flush=True)
        return code
    finally:
        try:terminate_owned(process,packet)
        finally:
            shutil.rmtree(scratch)
            marker.unlink(missing_ok=True)
            print(json.dumps({'ownedScratchRemoved':not scratch.exists(),'modelDistributed':False,'artifactUploads':0,'productionAdoption':False}),flush=True)

def cleanup_only(parent):
    marker=parent/'fictional-three-arm-owned-path.txt'
    if not marker.exists():return
    scratch=Path(marker.read_text().strip())
    if scratch.parent.resolve()!=parent.resolve() or not scratch.name.startswith('fictional-three-arm-') or scratch.is_symlink():
        raise RuntimeError('UNOWNED_CLEANUP_PATH_REFUSED')
    terminate_owned(None,scratch/'packet')
    if scratch.exists():shutil.rmtree(scratch)
    marker.unlink()

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--cleanup-only',action='store_true');parser.add_argument('--packet-sha');parser.add_argument('--recipe-sha')
    args=parser.parse_args();parent=Path(os.environ['RUNNER_TEMP'])
    if args.cleanup_only:cleanup_only(parent);return
    workflow=ROOT.parents[1]/'.github/workflows/local-ai-three-arm-research.yml'
    def terminate(signum,frame):raise InterruptedError('controller terminated; clean owned child/scratch')
    previous_term=signal.getsignal(signal.SIGTERM)
    signal.signal(signal.SIGTERM,terminate)
    try:code=execute(ROOT,args.packet_sha or '',args.recipe_sha or '',workflow,parent)
    except Exception as exc:
        print(json.dumps({'controllerDisposition':'OPERATIONAL_UNASSESSED','error':type(exc).__name__+':'+str(exc),
                          'modelAccuracy':'UNASSESSED','modelLoads':'UNKNOWN_UNLESS_EXECUTION_RECEIPT_PRESENT',
                          'wholeDocumentFormalSlots':{'expected':1270,'assessed':0,'UNASSESSED':1270},
                          'wholeDocumentFormalClocks':{'expected':70,'assessed':0,'UNASSESSED':70},'productionAdoption':False}),flush=True)
        raise
    finally:signal.signal(signal.SIGTERM,previous_term)
    raise SystemExit(code)

if __name__=='__main__':main()
