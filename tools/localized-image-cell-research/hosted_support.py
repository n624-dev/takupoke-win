"""Retained audited hosted download/cleanup helpers; no native imports."""
import hashlib,json,os,shutil,signal,subprocess,sys,time,urllib.request
from pathlib import Path
from protocol import digest

def read(path):
    return json.loads(Path(path).read_text())


def first_attempt():
    if os.environ.get('GITHUB_RUN_ATTEMPT')!='1':raise RuntimeError('RESEARCH_RERUN_NOT_AUTHORIZED')


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
    for name in ('preflight-failure.json','execution-receipt.json','protocol-report.json',
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
