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
from comparison import CONDITIONS, SCOPE_ACTION, select_recipe, derive_packet

ROOT = Path(__file__).resolve().parent
REPOSITORY='n624-dev/takupoke-win'
BRANCH='refs/heads/research/windows-local-ai-three-arm-portable-20261005'

def read(path):
    return json.loads(Path(path).read_text())

def first_attempt():
    if os.environ.get('GITHUB_RUN_ATTEMPT')!='1':raise RuntimeError('RESEARCH_RERUN_NOT_AUTHORIZED')

def verify_push_authority(root):
    first_attempt()
    if os.environ.get('GITHUB_ACTIONS')!='true' or os.environ.get('GITHUB_EVENT_NAME')!='push' or os.environ.get('GITHUB_REPOSITORY')!=REPOSITORY or os.environ.get('GITHUB_REF')!=BRANCH:
        raise RuntimeError('EXACT_RESEARCH_PUSH_CONTEXT_REQUIRED')
    event=read(os.environ['GITHUB_EVENT_PATH']);sha=os.environ['GITHUB_SHA']
    if len(sha)!=40 or any(c not in '0123456789abcdef' for c in sha):raise RuntimeError('PUSH_SHA_INVALID')
    if event.get('deleted') is not False or event.get('forced') is not False or event.get('ref')!=BRANCH or event.get('repository',{}).get('full_name')!=REPOSITORY or event.get('after')!=sha:
        raise RuntimeError('PUSH_EVENT_AUTHORITY_MISMATCH')
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=root.parents[1],text=True).strip()
    if head!=sha:raise RuntimeError('PUSH_HEAD_NOT_CHECKED_OUT')
    return {'type':'EXACT_REVIEWED_RESEARCH_BRANCH_PUSH','repository':REPOSITORY,'ref':BRANCH,'eventAfterSHA':sha,'localGitHEAD':head,'runAttempt':1,'manualApproval':False}

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

def proc_state(pid):
    try:
        fields=Path('/proc',str(pid),'stat').read_text().rsplit(')',1)[1].split()
        return {'state':fields[0],'group':int(fields[2]),'session':int(fields[3]),'startTicks':int(fields[19])}
    except (FileNotFoundError,ProcessLookupError):return None

def owned_pid(pid,packet):
    # Only inspect an already selected potential owned PID. Never print environ.
    try:
        path=Path('/proc',str(pid),'environ')
        with path.open('rb') as stream:
            value=stream.read(2097153)
        if len(value)>2097152:raise RuntimeError('OWNERSHIP_ENVIRONMENT_BOUND')
        return ('FICTIONAL_RESEARCH_SCRATCH='+str(packet.parent)).encode() in value.split(b'\0')
    except (FileNotFoundError,ProcessLookupError):return False

def owned_group_members(group,packet):
    members=[];errors=[];zombies=[]
    for entry in Path('/proc').iterdir():
        if not entry.name.isdecimal():continue
        pid=int(entry.name)
        try:state=proc_state(pid)
        except Exception as exc:
            errors.append('uninspectablePotentialGroup:'+str(pid)+':'+type(exc).__name__);continue
        # Group/session filtering MUST precede protected environ inspection.
        if state is None or state['group']!=group or state['session']!=group:continue
        if state['state']=='Z':zombies.append(pid);continue
        try:
            if not owned_pid(pid,packet):
                errors.append('groupOwnershipNotProven:'+str(pid));continue
        except Exception as exc:
            errors.append('groupOwnershipUnavailable:'+str(pid)+':'+type(exc).__name__);continue
        members.append(pid)
    return {'members':members,'errors':errors,'zombiePids':zombies}

def terminate_owned(process, packet):
    # Outer fail-closed guard. Scratch may only be removed after complete cleanup.
    errors=[];remaining=[];zombies=[]
    try:
        if process is not None and process.poll() is None:
            process.terminate()
            try:process.wait(timeout=12)
            except subprocess.TimeoutExpired:
                process.kill();process.wait(timeout=5)
        elif process is None and (packet/'controller-runner.json').exists():
            pid=read(packet/'controller-runner.json')['pid']
            if type(pid) is not int or pid<=1:raise RuntimeError('INVALID_OWNED_RUNNER_PID')
            state=proc_state(pid)
            if state is not None and state['state']!='Z':
                if not owned_pid(pid,packet):raise RuntimeError('RUNNER_OWNERSHIP_NOT_PROVEN')
                original_start=state['startTicks']
                recorded_start=read(packet/'controller-runner.json').get('startTicks')
                if recorded_start is not None and recorded_start!=original_start:raise RuntimeError('RUNNER_START_IDENTITY_CHANGED')
                def same_live_runner():
                    current=proc_state(pid)
                    if current is None or current['state']=='Z':return False
                    if current['startTicks']!=original_start or not owned_pid(pid,packet):
                        raise RuntimeError('RUNNER_OWNERSHIP_CHANGED; no signal')
                    return True
                if same_live_runner():os.kill(pid,signal.SIGTERM)
                deadline=time.monotonic()+12
                while same_live_runner() and time.monotonic()<deadline:time.sleep(.1)
                if same_live_runner():os.kill(pid,signal.SIGKILL)
        group_file=packet/'worker-process-group.json'
        group=None
        if group_file.exists():group=read(group_file)['group']
        elif (packet/'preflight-failure.json').exists():
            failure=read(packet/'preflight-failure.json')
            if failure.get('dependentExecutionStarted') is True:
                group=failure.get('workerPID')
                if type(group) is not int or group<=1:raise RuntimeError('LAUNCHED_WORKER_GROUP_IDENTITY_MISSING')
            elif failure.get('dependentExecutionStarted') is not False:
                raise RuntimeError('PREFLIGHT_LAUNCH_STATE_UNKNOWN')
        elif (packet/'probe-started.json').exists():
            raise RuntimeError('GUARDED_LAUNCH_GROUP_UNKNOWN; retain scratch')
        if group is not None:
            if type(group) is not int or group<=1:raise RuntimeError('INVALID_OWNED_GROUP')
            selected=owned_group_members(group,packet)
            errors.extend(selected['errors']);zombies=selected['zombiePids']
            # Never signal a reused group with ambiguous or foreign ownership.
            if selected['members'] and not selected['errors']:
                try:os.killpg(group,signal.SIGKILL)
                except ProcessLookupError:pass
            deadline=time.monotonic()+5
            while True:
                selected=owned_group_members(group,packet)
                remaining=selected['members'];zombies=selected['zombiePids']
                errors.extend(selected['errors'])
                if errors or not remaining or time.monotonic()>=deadline:break
                time.sleep(.05)
            if remaining:errors.append('OWNED_CHILD_CLEANUP_INCOMPLETE')
        if process is not None and process.poll() is None:
            remaining.append(process.pid);errors.append('OWNED_RUNNER_STILL_LIVE')
        elif process is None and (packet/'controller-runner.json').exists():
            state=proc_state(pid)
            if state is not None and state['state']!='Z':
                remaining.append(pid);errors.append('OWNED_RUNNER_STILL_LIVE')
    except Exception as exc:errors.append(type(exc).__name__+':'+str(exc))
    return {'complete':not errors and not remaining,'remainingOwnedPids':sorted(set(remaining)),
            'zombiePids':zombies,'errors':sorted(set(errors))}

def print_research_logs(packet,recipe):
    # Per-file failure must never suppress the next diagnostic. No binary/env.
    errors=[];printed=[];remaining=recipe.get('maximumOutputBytes',16777216)
    tails=recipe.get('maximumPrintedWorkerLogBytes',1048576)
    for name in ('preflight-failure.json','execution-receipt.json','comparison-report.json',
                 'responses.jsonl','worker-events.jsonl','worker-stdout.log','worker-stderr.log'):
        path=packet/name
        try:
            if not path.exists():continue
            if path.is_symlink():raise RuntimeError('DIAGNOSTIC_SYMLINK_REFUSED')
            size=path.stat().st_size;is_tail=name.endswith('.log')
            limit=tails if is_tail else remaining
            with path.open('rb') as stream:
                if is_tail:stream.seek(max(0,size-limit))
                raw=stream.read(limit)
                value=raw.decode(errors='replace')
            count=len(raw)
            if not is_tail:remaining-=count
            truncated=size>limit
            if truncated and not is_tail:errors.append(name+':DIAGNOSTIC_BOUND_EXCEEDED')
            # Bounded stdout/stderr tails are intentionally incomplete.
            if is_tail:
                print(json.dumps({'log':name,'totalBytes':size,'printedTailBytesMaximum':limit,'tail':value,'truncated':truncated}),flush=True)
            else:
                print(name+'\n'+value,flush=True)
            printed.append({'name':name,'totalBytes':size,'printedBytes':count,'truncated':truncated})
        except Exception as exc:errors.append(name+':'+type(exc).__name__+':'+str(exc))
    return {'complete':not errors,'files':printed,'errors':errors}

def finish_owned_scratch(process,packet,scratch,marker,marker_owned,recipe,primary_error=None):
    try:cleanup=terminate_owned(process,packet)
    except BaseException as exc:
        cleanup={'complete':False,'remainingOwnedPids':None,'errors':[type(exc).__name__+':'+str(exc)]}
    # ALWAYS diagnose before deletion, even if termination raises or is incomplete.
    try:diagnostics=print_research_logs(packet,recipe)
    except BaseException as exc:
        diagnostics={'complete':False,'errors':[type(exc).__name__+':'+str(exc)]}
    removed=False
    if cleanup['complete']:
        try:
            shutil.rmtree(scratch)
            if marker_owned:marker.unlink(missing_ok=True)
            removed=not scratch.exists()
        except Exception as exc:cleanup['errors'].append('scratchRemoval:'+type(exc).__name__+':'+str(exc));cleanup['complete']=False
    print(json.dumps({'primaryControllerError':primary_error,'outerProcessCleanup':cleanup,'boundedDiagnostics':diagnostics,
                      'ownedScratchRemoved':removed,'scratchRetainedForIncompleteCleanup':not cleanup['complete'],
                      'modelDistributed':False,'artifactUploads':0,'productionAdoption':False}),flush=True)
    return cleanup['complete'] and diagnostics['complete'] and removed

def execute(root, packet_sha, recipe_sha, workflow, scratch_parent, authority=None, condition='gemma-original'):
    first_attempt()
    freeze=verify_source(root,packet_sha,recipe_sha,workflow)
    if sys.version_info[:3]!=(3,12,14):raise RuntimeError('EXACT_PYTHON_3_12_14_REQUIRED')
    if sys.platform!='linux' or os.uname().machine!='x86_64':raise RuntimeError('LINUX_X86_64_SECCOMP_PLATFORM_REQUIRED')
    recipe=read(root/'recipe.json');identity=read(root/'runtime-identity.json');legal=read(root/'legal.json')
    configuration_path=root/'comparison-config.json'
    if configuration_path.exists():recipe=select_recipe(recipe,condition,read(configuration_path))
    # Only resource/provenance source is read before exact assets are available.
    initial={**recipe,'workingAllowanceBytes':recipe['workingAllowanceBytes']+recipe['modelBytes']+identity['wheel']['bytes']+209715200}
    resources(scratch_parent,initial)
    scratch=Path(tempfile.mkdtemp(prefix='fictional-three-arm-',dir=scratch_parent));process=None;packet=scratch/'packet'
    # Explicit path retained for an independent workflow always-cleanup step.
    marker=Path(scratch_parent)/'fictional-three-arm-owned-path.txt'
    if marker.exists():
        shutil.rmtree(scratch);raise RuntimeError('EXCLUSIVE_SCRATCH_ALREADY_EXISTS')
    marker_owned=False;primary_error=None
    try:
        with marker.open('x') as stream:stream.write(str(scratch)+'\n')
        marker_owned=True
        model=legal['models'][recipe['modelKey']] if 'models' in legal else legal['model']
        if model['modelSHA256']!=recipe['modelSHA256'] or model['modelBytes']!=recipe['modelBytes']:raise RuntimeError('SELECTED_LEGAL_MODEL_IDENTITY_MISMATCH')
        model_path=scratch/'selected-model.litertlm'
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
        selected_recipe_bytes=(json.dumps(recipe,indent=2)+'\n').encode()
        (packet/'recipe.json').write_bytes(selected_recipe_bytes)
        derived_freeze=derive_packet(freeze,recipe,packet_sha,digest(configuration_path),selected_recipe_bytes)
        (packet/'packet-freeze.json').write_text(json.dumps(derived_freeze,indent=2)+'\n')
        selected_packet_sha=digest(packet/'packet-freeze.json');selected_recipe_sha=digest(packet/'recipe.json')
        (packet/'model').mkdir();shutil.move(str(model_path),packet/recipe['modelPath'])
        wheel_path.unlink()
        (packet/'runtime-binding.json').write_text(json.dumps({'pythonVersion':'3.12.14','pythonExecutable':str(python),'packageRoot':str(package),'wheelSHA256':wheel['sha256'],'newPortableRuntimeFilesVerified':True})+'\n')
        print(json.dumps({'sourcePortablePacketSHA256':packet_sha,'sourceRecipeSHA256':recipe_sha,'portablePacketSHA256':selected_packet_sha,'recipeSHA256':selected_recipe_sha,'comparisonCondition':condition,'modelSHA256Verified':recipe['modelSHA256'],'modelBytes':recipe['modelBytes'],'runtimeWheelSHA256Verified':wheel['sha256'],'runtimeFilesVerified':identity['files'],'pythonVersion':'3.12.14','inputPayloadSHA256':digest(packet/'inputs.json') if (packet/'inputs.json').exists() else None,'executionAuthority':authority or {'type':'MANUAL_WORKFLOW_DISPATCH_EXACT_HASH_ARGUMENTS'}}),flush=True)
        (packet/'root-inference-approval.json').write_text(json.dumps({'action':SCOPE_ACTION,'sourceFreezeSHA256':selected_packet_sha,'recipeSHA256':selected_recipe_sha,'comparisonCondition':condition,'originalSourcePacketSHA256':packet_sha,'comparisonConfigurationSHA256':digest(configuration_path),'plannedMaximumCalls':recipe['maximumCalls'],'newRecognizerCalls':0,'productionAdoption':False,'fullDocumentAssessment':False,'authority':authority or {'type':'MANUAL_WORKFLOW_DISPATCH_EXACT_HASH_ARGUMENTS'}})+'\n')
        # Asset hashing warms file cache: this measured gate is mandatory now.
        resources(packet,recipe)
        env={k:v for k,v in os.environ.items() if k not in ('GITHUB_TOKEN','GH_TOKEN','ACTIONS_RUNTIME_TOKEN','ACTIONS_ID_TOKEN_REQUEST_TOKEN')}
        env['FICTIONAL_RESEARCH_SCRATCH']=str(scratch)
        process=subprocess.Popen([str(python),str(packet/'run_once.py')],cwd=packet,env=env,stdin=subprocess.DEVNULL,close_fds=True)
        (packet/'controller-runner.json').write_text(json.dumps({'pid':process.pid,'startTicks':proc_state(process.pid)['startTicks']})+'\n')
        try:code=process.wait(timeout=recipe['processDeadlineSeconds']+60)
        except subprocess.TimeoutExpired:raise RuntimeError('OUTER_CONTROLLER_DEADLINE')
        return code
    except BaseException as exc:
        primary_error=type(exc).__name__+':'+str(exc)
        raise
    finally:
        complete=finish_owned_scratch(process,packet,scratch,marker,marker_owned,recipe,primary_error)
        if not complete and primary_error is None:raise RuntimeError('CONTROLLER_CLEANUP_OR_DIAGNOSTICS_INCOMPLETE')

def cleanup_only(parent):
    marker=parent/'fictional-three-arm-owned-path.txt'
    if not marker.exists():return
    scratch=Path(marker.read_text().strip())
    if scratch.parent.resolve()!=parent.resolve() or not scratch.name.startswith('fictional-three-arm-') or scratch.is_symlink():
        raise RuntimeError('UNOWNED_CLEANUP_PATH_REFUSED')
    packet=scratch/'packet'
    recipe=read(packet/'recipe.json') if (packet/'recipe.json').exists() else {}
    if not finish_owned_scratch(None,packet,scratch,marker,True,recipe):
        raise RuntimeError('ALWAYS_CLEANUP_INCOMPLETE; owned scratch retained')

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--cleanup-only',action='store_true');parser.add_argument('--approved-research-push',action='store_true');parser.add_argument('--packet-sha');parser.add_argument('--recipe-sha');parser.add_argument('--condition',choices=CONDITIONS)
    args=parser.parse_args();parent=Path(os.environ['RUNNER_TEMP'])
    if args.cleanup_only:cleanup_only(parent);return
    first_attempt()
    if args.condition is None or not (ROOT/'comparison-config.json').is_file():raise RuntimeError('EXACT_COMPARISON_CONDITION_REQUIRED')
    authority=None
    if args.approved_research_push:
        if args.packet_sha or args.recipe_sha:raise RuntimeError('MIXED_PUSH_MANUAL_AUTHORITY_REFUSED')
        authority=verify_push_authority(ROOT)
        args.packet_sha=digest(ROOT/'packet-freeze.json');args.recipe_sha=digest(ROOT/'recipe.json')
    workflow=ROOT.parents[1]/'.github/workflows/local-ai-three-arm-research.yml'
    def terminate(signum,frame):raise InterruptedError('controller terminated; clean owned child/scratch')
    previous_term=signal.getsignal(signal.SIGTERM)
    signal.signal(signal.SIGTERM,terminate)
    try:code=execute(ROOT,args.packet_sha or '',args.recipe_sha or '',workflow,parent,authority,args.condition)
    except Exception as exc:
        print(json.dumps({'controllerDisposition':'OPERATIONAL_UNASSESSED','error':type(exc).__name__+':'+str(exc),
                          'modelAccuracy':'UNASSESSED','modelLoads':'UNKNOWN_UNLESS_EXECUTION_RECEIPT_PRESENT',
                          'wholeDocumentFormalSlots':{'expected':1270,'assessed':0,'UNASSESSED':1270},
                          'wholeDocumentFormalClocks':{'expected':70,'assessed':0,'UNASSESSED':70},'productionAdoption':False}),flush=True)
        raise
    finally:signal.signal(signal.SIGTERM,previous_term)
    raise SystemExit(code)

if __name__=='__main__':main()
