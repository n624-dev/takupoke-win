"""Sequential controller; second model waits for complete first-worker cleanup."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
from comparison import validate_recipe


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--gemma', type=Path, required=True)
    parser.add_argument('--qwen', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    roots = [args.gemma.resolve(), args.qwen.resolve()]
    if roots[0] == roots[1] or output.exists():
        raise RuntimeError('EXCLUSIVE_SEPARATE_CONDITIONS_REQUIRED')
    recipes = [json.loads((root / 'recipe.json').read_text()) for root in roots]
    for recipe, condition in zip(recipes, ('gemma-row-choice', 'qwen-row-choice')):
        validate_recipe(recipe)
        if recipe['comparisonCondition'] != condition:
            raise RuntimeError('FIXED_COMPARISON_MODEL_ORDER_REQUIRED')
    for filename in ('inputs.json', 'caller-plan.json', 'prompt.txt', 'contract.py', 'acquisition.py', 'source_contract.py', 'worker.py'):
        if (roots[0] / filename).read_bytes() != (roots[1] / filename).read_bytes():
            raise RuntimeError('SHARED_RESEARCH_SOURCE_BYTES_REQUIRED')
    output.mkdir(parents=True)
    with (output / 'comparison-started.json').open('x') as stream:
        json.dump({'conditions': [r['comparisonCondition'] for r in recipes], 'maximumCalls': 2,
                   'maximumConcurrentEngines': 1, 'retries': 0}, stream)
    receipts = []
    for root in roots:
        # run_once itself requires exact root GO and pins before native launch.
        process = subprocess.run([sys.executable, str(root / 'run_once.py')], cwd=root,
                                 stdin=subprocess.DEVNULL, timeout=650, check=False)
        path = root / 'execution-receipt.json'
        receipt = json.loads(path.read_text()) if path.exists() else None
        receipts.append(receipt)
        if (process.returncode != 0 or receipt is None or receipt.get('failure') is not None or
                receipt.get('exitCode') != 0 or receipt.get('processGroupCleanup', {}).get('complete') is not True or
                receipt.get('reporter', {}).get('exitCode') != 0):
            with (output / 'comparison-stopped.json').open('x') as stream:
                json.dump({'completedConditions': len(receipts) - 1, 'failedCondition': root.name,
                           'secondEngineStarted': len(receipts) == 2, 'receipts': receipts,
                           'productionAdoption': False}, stream)
            raise SystemExit(1)
    with (output / 'comparison-receipts.json').open('x') as stream:
        json.dump({'receipts': receipts, 'qualifiedGenAIModels': [], 'productionAdoption': False}, stream, indent=2)


if __name__ == '__main__':
    main()
