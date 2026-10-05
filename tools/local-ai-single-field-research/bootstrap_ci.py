"""Exact hosted dispatch controller. Public assets first; no native imports."""
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
from comparison import CONDITIONS, SCOPE_ACTION, select_recipe, derive_packet
from guard import resources
from hosted_support import (download, first_attempt, print_research_logs, proc_state,
                            read, terminate_owned, verify_source)
from protocol import digest

ROOT = Path(__file__).resolve().parent
REPOSITORY = 'n624-dev/takupoke-win'
BRANCH = 'refs/heads/research/single-field-focal-20261005'
WORKFLOW = ROOT.parents[1] / '.github/workflows/local-ai-single-field-research.yml'
MARKER = 'fictional-single-field-owned-path.txt'
PREFIX = 'fictional-single-field-'


def recipe_bytes(recipe):
    return (json.dumps(recipe, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def approved_recipes(root, packet_sha, gemma_sha, qwen_sha, workflow):
    freeze = verify_source(root, packet_sha, gemma_sha, workflow)
    base = read(root / 'recipe.json')
    config = read(root / 'comparison-config.json')
    recipes = [select_recipe(base, condition, config) for condition in CONDITIONS]
    if (type(qwen_sha) is not str or len(qwen_sha) != 64 or
            hashlib.sha256(recipe_bytes(recipes[1])).hexdigest() != qwen_sha):
        raise RuntimeError('EXACT_QWEN_RECIPE_APPROVAL_REQUIRED')
    return freeze, recipes


def dispatch_authority(packet_sha, gemma_sha, qwen_sha):
    first_attempt()
    if (os.environ.get('GITHUB_ACTIONS') != 'true' or os.environ.get('GITHUB_EVENT_NAME') != 'workflow_dispatch' or
            os.environ.get('GITHUB_REPOSITORY') != REPOSITORY or os.environ.get('GITHUB_REF') != BRANCH):
        raise RuntimeError('EXACT_RESEARCH_DISPATCH_CONTEXT_REQUIRED')
    event = read(os.environ['GITHUB_EVENT_PATH'])
    expected = {'approved_packet_sha256': packet_sha, 'approved_recipe_sha256': gemma_sha,
                'approved_qwen_recipe_sha256': qwen_sha}
    if event.get('ref') not in ('research/single-field-focal-20261005', BRANCH) or event.get('inputs') != expected:
        raise RuntimeError('EXACT_DISPATCH_INPUTS_REQUIRED')
    sha = os.environ.get('GITHUB_SHA', '')
    if len(sha) != 40 or any(c not in '0123456789abcdef' for c in sha):
        raise RuntimeError('DISPATCH_HEAD_SHA_INVALID')
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT.parents[1], text=True).strip()
    if head != sha:
        raise RuntimeError('DISPATCH_HEAD_NOT_CHECKED_OUT')
    return {'type': 'EXACT_ROOT_HASH_APPROVED_WORKFLOW_DISPATCH', 'repository': REPOSITORY,
            'ref': BRANCH, 'headSHA': head, 'runAttempt': 1, 'approvedInputs': expected}


def fetch_runtime(root, scratch, recipes):
    """Fetch exactly two retained model identities and one retained wheel."""
    identity = read(root / 'runtime-identity.json')
    legal = read(root / 'legal.json')
    model_paths = {}
    for recipe in recipes:
        model = legal['models'][recipe['modelKey']]
        if model['modelSHA256'] != recipe['modelSHA256'] or model['modelBytes'] != recipe['modelBytes']:
            raise RuntimeError('EXACT_LEGAL_MODEL_IDENTITY_REQUIRED')
        path = scratch / (recipe['modelKey'] + '.litertlm')
        download({'url': model['downloadURL'], 'bytes': recipe['modelBytes'], 'sha256': recipe['modelSHA256']}, path)
        model_paths[recipe['modelKey']] = path
        print(json.dumps({'publicAssetVerified': recipe['modelKey'], 'bytes': recipe['modelBytes'],
                          'sha256': recipe['modelSHA256'], 'newModelIdentity': False}), flush=True)
    wheel = identity['wheel']
    wheel_path = scratch / wheel['filename']
    download(wheel, wheel_path)
    venv = scratch / 'runtime'
    subprocess.run([sys.executable, '-m', 'venv', str(venv)], check=True, timeout=60)
    python = venv / 'bin/python'
    subprocess.run([str(python), '-m', 'pip', '--disable-pip-version-check', 'install', '--no-index',
                    '--no-deps', '--no-cache-dir', str(wheel_path)], check=True, timeout=120)
    package = venv / 'lib/python3.12/site-packages'
    for item in identity['files']:
        path = package / item['name']
        if path.stat().st_size != item['bytes'] or digest(path) != item['sha256']:
            raise RuntimeError('INSTALLED_RUNTIME_IDENTITY_MISMATCH')
    wheel_path.unlink()
    return model_paths, {'pythonVersion': '3.12.14', 'pythonExecutable': str(python),
                         'packageRoot': str(package), 'wheelSHA256': wheel['sha256'],
                         'newPortableRuntimeFilesVerified': True}


def stage_condition(root, scratch, freeze, recipe, packet_sha, binding, models, authority):
    # This is the first relocation of fictional inference payload, AFTER all
    # three exact public assets and runtime installation have been verified.
    packet = scratch / recipe['comparisonCondition']
    packet.mkdir()
    for pin in freeze['pins']:
        destination = packet / pin['path']
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / pin['path'], destination)
    selected_bytes = recipe_bytes(recipe)
    (packet / 'recipe.json').write_bytes(selected_bytes)
    configuration_sha = digest(root / 'comparison-config.json')
    derived = derive_packet(freeze, recipe, packet_sha, configuration_sha, selected_bytes)
    (packet / 'packet-freeze.json').write_text(json.dumps(derived, indent=2) + '\n')
    (packet / 'model').mkdir()
    shutil.move(str(models[recipe['modelKey']]), packet / recipe['modelPath'])
    (packet / 'runtime-binding.json').write_text(json.dumps(binding) + '\n')
    approval = {'action': SCOPE_ACTION, 'sourceFreezeSHA256': digest(packet / 'packet-freeze.json'),
                'recipeSHA256': digest(packet / 'recipe.json'), 'comparisonCondition': recipe['comparisonCondition'],
                'originalSourcePacketSHA256': packet_sha, 'comparisonConfigurationSHA256': configuration_sha,
                'plannedMaximumCalls': 4, 'newRecognizerCalls': 0, 'productionAdoption': False,
                'fullDocumentAssessment': False, 'authority': authority}
    (packet / 'root-inference-approval.json').write_text(json.dumps(approval) + '\n')
    print(json.dumps({'preparedCondition': recipe['comparisonCondition'], 'approval': approval,
                      'inputPayloadSHA256': digest(packet / 'inputs.json'),
                      'publicAssetsVerifiedBeforePayloadCopy': True, 'modelBytes': recipe['modelBytes'],
                      'modelSHA256': recipe['modelSHA256'], 'runtimeBinding': binding}), flush=True)
    return packet


def condition_run(packet, recipe, binding, scratch):
    """Wait, terminate/check owned processes, then diagnose before returning."""
    process = None
    primary_error = None
    result = None
    try:
        resources(packet, recipe)
        env = {k: v for k, v in os.environ.items() if k not in
               ('GITHUB_TOKEN', 'GH_TOKEN', 'ACTIONS_RUNTIME_TOKEN', 'ACTIONS_ID_TOKEN_REQUEST_TOKEN')}
        env['FICTIONAL_RESEARCH_SCRATCH'] = str(scratch)
        process = subprocess.Popen([binding['pythonExecutable'], str(packet / 'run_once.py')], cwd=packet,
                                   env=env, stdin=subprocess.DEVNULL, close_fds=True)
        state = proc_state(process.pid)
        (packet / 'controller-runner.json').write_text(json.dumps({'pid': process.pid,
                     'startTicks': state['startTicks'] if state else None}) + '\n')
        code = process.wait(timeout=recipe['processDeadlineSeconds'] + 60)
        receipt_path = packet / 'execution-receipt.json'
        receipt = read(receipt_path) if receipt_path.exists() else None
        if (code != 0 or receipt is None or receipt.get('failure') is not None or receipt.get('exitCode') != 0 or
                receipt.get('processGroupCleanup', {}).get('complete') is not True or
                receipt.get('reporter', {}).get('exitCode') != 0):
            raise RuntimeError('CONDITION_OPERATIONAL_FAILURE_STOP_NO_RETRY')
        report = read(packet / 'protocol-report.json')
        if report['completedCalls'] != 4 or report['missingCallsUNASSESSED'] != 0:
            raise RuntimeError('EXACT_FOUR_CALL_COMPLETION_REQUIRED')
        result = {'condition': recipe['comparisonCondition'], 'completedCalls': 4, 'executionReceipt': receipt,
                  'protocolReport': report}
    except BaseException as exc:
        primary_error = type(exc).__name__ + ':' + str(exc)
        raise
    finally:
        cleanup = terminate_owned(process, packet)
        diagnostics = print_research_logs(packet, recipe)
        (packet / 'hosted-diagnostics-printed.json').write_text(json.dumps({'diagnostics': diagnostics}) + '\n')
        print(json.dumps({'condition': recipe['comparisonCondition'], 'primaryControllerError': primary_error,
                          'outerProcessCleanup': cleanup, 'boundedDiagnostics': diagnostics,
                          'artifactUploads': 0, 'productionAdoption': False}), flush=True)
        if not cleanup['complete'] or not diagnostics['complete']:
            raise RuntimeError('CONDITION_CLEANUP_OR_DIAGNOSTICS_INCOMPLETE_STOP')
    return result


def cleanup_only(parent):
    marker = parent / MARKER
    if not marker.exists():
        return
    scratch = Path(marker.read_text().strip())
    if scratch.parent.resolve() != parent.resolve() or not scratch.name.startswith(PREFIX) or scratch.is_symlink():
        raise RuntimeError('UNOWNED_CLEANUP_PATH_REFUSED')
    complete = True
    for condition in CONDITIONS:
        packet = scratch / condition
        if packet.exists():
            if packet.is_symlink():
                raise RuntimeError('INDIRECT_CONDITION_CLEANUP_REFUSED')
            cleanup = terminate_owned(None, packet)
            printed = packet / 'hosted-diagnostics-printed.json'
            if printed.exists():
                diagnostics = read(printed)['diagnostics']
            else:
                recipe = read(packet / 'recipe.json') if (packet / 'recipe.json').exists() else {}
                diagnostics = print_research_logs(packet, recipe)
            complete = complete and cleanup['complete'] and diagnostics['complete']
            print(json.dumps({'alwaysCleanupCondition': condition, 'outerProcessCleanup': cleanup,
                              'boundedDiagnostics': diagnostics}), flush=True)
    if not complete:
        raise RuntimeError('INCOMPLETE_CLEANUP_RETAIN_OWNED_SCRATCH')
    shutil.rmtree(scratch)
    marker.unlink()
    print(json.dumps({'ownedScratchRemoved': not scratch.exists(), 'artifactUploads': 0,
                      'modelCacheUploads': 0, 'productionAdoption': False}), flush=True)


def execute(root, packet_sha, gemma_sha, qwen_sha, workflow, parent, authority):
    freeze, recipes = approved_recipes(root, packet_sha, gemma_sha, qwen_sha, workflow)
    if sys.version_info[:3] != (3, 12, 14) or sys.platform != 'linux' or os.uname().machine != 'x86_64':
        raise RuntimeError('EXACT_LINUX_X86_64_PYTHON_3_12_14_REQUIRED')
    identity = read(root / 'runtime-identity.json')
    initial = {**recipes[0], 'workingAllowanceBytes': recipes[0]['workingAllowanceBytes'] +
               sum(r['modelBytes'] for r in recipes) + identity['wheel']['bytes'] + 209715200}
    resources(parent, initial)
    marker = parent / MARKER
    if marker.exists():
        raise RuntimeError('EXCLUSIVE_SCRATCH_ALREADY_EXISTS')
    scratch = Path(tempfile.mkdtemp(prefix=PREFIX, dir=parent))
    try:
        with marker.open('x') as stream:
            stream.write(str(scratch) + '\n')
    except BaseException:
        shutil.rmtree(scratch)
        raise
    results = []
    try:
        models, binding = fetch_runtime(root, scratch, recipes)
        for recipe in recipes:
            packet = stage_condition(root, scratch, freeze, recipe, packet_sha, binding, models, authority)
            # No second condition staging/engine launch until first returns with
            # complete inner and outer process cleanup. A failure aborts loop.
            results.append(condition_run(packet, recipe, binding, scratch))
            shutil.rmtree(packet)
        print(json.dumps({'hostedComparisonComplete': True, 'completedConditions': len(results),
                          'actualCompletedCalls': sum(r['completedCalls'] for r in results),
                          'maximumConcurrentEngines': 1, 'existingModelFetches': 2, 'newModelIdentities': 0,
                          'publicAssetFetches': 3, 'qualifiedGenAIModels': [], 'productionAdoption': False,
                          'formalSlotsUNASSESSED': 1270, 'formalClocksUNASSESSED': 70}), flush=True)
    finally:
        cleanup_only(parent)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--validate-only', action='store_true')
    parser.add_argument('--cleanup-only', action='store_true')
    parser.add_argument('--packet-sha')
    parser.add_argument('--recipe-sha')
    parser.add_argument('--qwen-recipe-sha')
    args = parser.parse_args()
    if args.validate_only:
        recipe = read(ROOT / 'recipe.json')
        qwen = select_recipe(recipe, CONDITIONS[1], read(ROOT / 'comparison-config.json'))
        approved_recipes(ROOT, digest(ROOT / 'packet-freeze.json'), digest(ROOT / 'recipe.json'),
                         hashlib.sha256(recipe_bytes(qwen)).hexdigest(), WORKFLOW)
        print(json.dumps({'validated': True, 'modelDownloads': 0, 'engineCalls': 0, 'rootExecutionGo': False}))
        return
    parent = Path(os.environ['RUNNER_TEMP'])
    if args.cleanup_only:
        cleanup_only(parent)
        return
    authority = dispatch_authority(args.packet_sha or '', args.recipe_sha or '', args.qwen_recipe_sha or '')
    def terminate(signum, frame):
        raise InterruptedError('hosted controller terminated; clean owned scratch')
    old_term = signal.getsignal(signal.SIGTERM)
    signal.signal(signal.SIGTERM, terminate)
    try:
        execute(ROOT, args.packet_sha or '', args.recipe_sha or '', args.qwen_recipe_sha or '', WORKFLOW, parent, authority)
    except BaseException as exc:
        print(json.dumps({'controllerDisposition': 'OPERATIONAL_UNASSESSED', 'error': type(exc).__name__ + ':' + str(exc),
                          'qualifiedGenAIModels': [], 'productionAdoption': False}), flush=True)
        raise
    finally:
        signal.signal(signal.SIGTERM, old_term)


if __name__ == '__main__':
    main()
