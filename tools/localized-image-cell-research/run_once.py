"""Exact root-approved one bounded local-only process. Never retries."""
import json
import os
from pathlib import Path
import signal
import subprocess
import time
import ctypes
from guard import resources, verify_packet, bind_runtime
from protocol import digest

ROOT = Path(__file__).resolve().parent

def rss_group(group):
    total = 0
    for path in Path('/proc').iterdir():
        if not path.name.isdecimal():
            continue
        try:
            stat = (path / 'stat').read_text().rsplit(')', 1)[1].split()
            if int(stat[2]) == group:
                total += int(stat[21]) * os.sysconf('SC_PAGE_SIZE')
        except (OSError, ValueError, IndexError):
            pass
    return total

def guarded_start(recipe, launch):
    # Testable ordering: neither launch nor dependent reporter runs on failure.
    boundary = resources(ROOT, recipe)
    with (ROOT / 'probe-started.json').open('x') as stream:
        json.dump({'boundary': boundary, 'noRetries': True, 'runnerPID': os.getpid()}, stream)
    return launch(), boundary

def subreaper():
    libc=ctypes.CDLL(None,use_errno=True)
    if libc.prctl(36,1,0,0,0)!=0:
        raise OSError(ctypes.get_errno(),'child subreaper unavailable; no worker launch')

def group_pids(group):
    found=[]
    for path in Path('/proc').iterdir():
        if not path.name.isdecimal():continue
        try:
            stat=(path/'stat').read_text().rsplit(')',1)[1].split()
            if int(stat[2])==group and int(stat[3])==group:found.append(int(path.name))
        except (FileNotFoundError,ProcessLookupError):pass
    return found

def cleanup_group(process):
    errors=[]
    # Unconditional termination also covers grandchildren surviving leader exit.
    try:os.killpg(process.pid,signal.SIGKILL)
    except ProcessLookupError:pass
    except Exception as exc:errors.append('killpg:'+str(exc))
    try:process.wait(timeout=5)
    except Exception as exc:errors.append('leaderwait:'+str(exc))
    deadline=time.monotonic()+5
    remaining=[]
    while True:
        try:
            while True:
                try:pid,_=os.waitpid(-process.pid,os.WNOHANG)
                except ChildProcessError:break
                if pid==0:break
            remaining=group_pids(process.pid)
        except Exception as exc:
            errors.append('groupread/reap:'+str(exc));break
        if not remaining or time.monotonic()>=deadline:break
        time.sleep(.05)
    return {'complete':not errors and not remaining,'remainingGroupPids':remaining,'errors':errors}

def main():
    recipe = json.loads((ROOT / 'recipe.json').read_text())
    launched=None;launch_cleanup=None;handles=[]
    try:
        identity = verify_packet(ROOT, recipe)
        recipe=bind_runtime(ROOT,recipe)
        if any((ROOT / n).exists() for n in ('probe-started.json', 'responses.jsonl', 'worker-events.jsonl', 'execution-receipt.json')):
            raise RuntimeError('EXCLUSIVE_RUN_ALREADY_STARTED_OR_OUTPUT_EXISTS')
        def launch():
            nonlocal launched,launch_cleanup
            # Owned TMPDIR exists before Python/LiteRT module imports.
            (ROOT / 'owned-runtime-cache').mkdir(exist_ok=True)
            stdout = (ROOT / 'worker-stdout.log').open('xb')
            stderr = (ROOT / 'worker-stderr.log').open('xb')
            handles.extend([stdout, stderr])
            subreaper()
            process = subprocess.Popen(
                [recipe['pythonPath'], str(ROOT / 'worker.py'), identity['sourceFreezeSHA256']],
                cwd=ROOT, env={k:v for k,v in {**os.environ, 'TMPDIR': str(ROOT / 'owned-runtime-cache'), 'HF_HUB_OFFLINE': '1', 'TRANSFORMERS_OFFLINE': '1'}.items() if k not in ('GITHUB_TOKEN','GH_TOKEN','ACTIONS_RUNTIME_TOKEN','ACTIONS_ID_TOKEN_REQUEST_TOKEN')},
                stdin=subprocess.DEVNULL, stdout=stdout, stderr=stderr, close_fds=True, start_new_session=True)
            launched=process
            try:
                (ROOT/'worker-process-group.json').write_text(json.dumps({'pid':process.pid,'group':process.pid})+'\n')
            except BaseException:
                launch_cleanup=cleanup_group(process)
                raise
            return process
        # There is no intervening dependent call between reserve and Popen.
        process, boundary = guarded_start(recipe, launch)
    except Exception as exc:
        close_errors=[]
        for stream in handles:
            try:stream.close()
            except Exception as close_error:close_errors.append(type(close_error).__name__+':'+str(close_error))
        (ROOT / 'preflight-failure.json').write_text(json.dumps({'error': str(exc), 'dependentExecutionStarted': launched is not None, 'workerPID': launched.pid if launched is not None else None, 'launchCleanup':launch_cleanup, 'logCloseErrors':close_errors, 'modelLoads': 'UNKNOWN' if launched is not None else 0, 'inferenceCalls': 'UNKNOWN' if launched is not None else 0}) + '\n')
        raise SystemExit(2)
    begin = time.monotonic()
    peak = 0
    failure = None
    supervisor_error=None
    cleanup=None
    old_term=signal.getsignal(signal.SIGTERM)
    def interrupted(signum,frame):raise InterruptedError('supervisor termination signal')
    signal.signal(signal.SIGTERM,interrupted)
    try:
      while process.poll() is None:
        elapsed = time.monotonic() - begin
        peak = max(peak, rss_group(process.pid))
        active = None
        events = ROOT / 'worker-events.jsonl'
        if events.exists():
            for line in events.read_text().splitlines():
                try:
                    e = json.loads(line)
                except json.JSONDecodeError:
                    continue
                if e['event'] in ('loadStarted', 'callStarted'):
                    active = e
                elif e['event'] in ('loadComplete', 'callComplete', 'workerComplete'):
                    active = None
        if elapsed > recipe['processDeadlineSeconds']:
            failure = 'PROCESS_DEADLINE'
        elif peak > recipe['maximumRSSBytes']:
            failure = 'PROCESS_RSS_LIMIT'
        elif active and time.monotonic() - active['monotonic'] > (recipe['loadDeadlineSeconds'] if active['event'] == 'loadStarted' else recipe['callDeadlineSeconds']):
            failure = 'LOAD_DEADLINE' if active['event'] == 'loadStarted' else 'CALL_DEADLINE'
        elif not events.exists() and elapsed > recipe['loadDeadlineSeconds']:
            failure = 'WORKER_PREFLIGHT_DEADLINE'
        try:
            resources(ROOT, {**recipe, 'minimumAvailableMemoryBytes': 0, 'workingAllowanceBytes': 0})
        except RuntimeError:
            failure = 'DISK_RESERVE_DURING_EXECUTION'
        output_bytes = sum(p.stat().st_size for p in ROOT.glob('*') if p.is_file() and p.name in ('responses.jsonl', 'worker-events.jsonl', 'worker-stdout.log', 'worker-stderr.log'))
        cache = ROOT / 'owned-runtime-cache'
        cache_bytes = sum(p.stat().st_size for p in cache.rglob('*') if p.is_file()) if cache.exists() else 0
        if output_bytes > recipe['maximumOutputBytes'] or cache_bytes > recipe['maximumCacheBytes']:
            failure = 'OWNED_DISK_WORK_LIMIT'
        if failure:break
        time.sleep(.1)
    except BaseException as exc:
        supervisor_error=type(exc).__name__+':'+str(exc)
        failure='SUPERVISOR_EXCEPTION'
    finally:
        cleanup=cleanup_group(process)
        signal.signal(signal.SIGTERM,old_term)
        for stream in handles:
            stream.close()
    code=process.returncode if hasattr(process,'returncode') else process.wait()
    receipt = {'comparisonCondition':recipe.get('comparisonCondition'),'declaredMaximumCalls':recipe['maximumCalls'],'identity': identity, 'preStartResources': boundary, 'exitCode': code, 'failure': failure,
               'milliseconds': round((time.monotonic()-begin)*1000), 'peakRSSBytes': peak,
               'retries': 0, 'newRecognizerCalls': 0, 'nativeRawChanged': False, 'productionAdoption': False,
               'completedRunIsNotQualification': True}
    receipt['supervisorError']=supervisor_error
    receipt['processGroupCleanup']=cleanup
    # Separate bounded reporter also runs after worker nonzero, with missing calls
    # explicitly UNASSESSED. Failed pre-start never reaches this branch.
    try:
        if not cleanup['complete']:raise RuntimeError('PROCESS_GROUP_CLEANUP_INCOMPLETE; no dependent reporter')
        resources(ROOT, {**recipe, 'minimumAvailableMemoryBytes': 0, 'workingAllowanceBytes': 0})
        result = subprocess.run([recipe['pythonPath'], str(ROOT / 'report_protocol.py')], cwd=ROOT, timeout=30, capture_output=True)
        receipt['reporter'] = {'exitCode': result.returncode, 'stderr': result.stderr.decode(errors='replace')[:4096]}
    except Exception as exc:
        receipt['reporter'] = {'exitCode': None, 'state': 'UNASSESSED_REPORTER_FAILED', 'error': str(exc)}
    (ROOT / 'execution-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps(receipt))
    if failure or code!=0 or not cleanup['complete'] or receipt['reporter'].get('exitCode')!=0:
        raise SystemExit(1)

if __name__ == '__main__':
    main()
