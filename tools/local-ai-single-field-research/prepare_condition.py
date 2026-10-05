"""Export an exclusive portable model condition; no runtime/model reads."""
import argparse
import json
from pathlib import Path
from comparison import derive_packet, select_recipe
from protocol import digest

ROOT = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--condition', choices=('gemma-single-field', 'qwen-single-field'), required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists() or ROOT in output.parents or output in ROOT.parents:
        raise RuntimeError('EXCLUSIVE_EXTERNAL_CONDITION_DIRECTORY_REQUIRED')
    freeze = json.loads((ROOT / 'packet-freeze.json').read_text())
    for pin in freeze['pins']:
        relative = Path(pin['path'])
        source = ROOT / relative
        if (relative.is_absolute() or '..' in relative.parts or source.is_symlink() or
                source.stat().st_size != pin['bytes'] or digest(source) != pin['sha256']):
            raise RuntimeError('FROZEN_SOURCE_PIN_CHANGED')
    recipe = select_recipe(json.loads((ROOT / 'recipe.json').read_text()), args.condition,
                           json.loads((ROOT / 'comparison-config.json').read_text()))
    recipe_bytes = (json.dumps(recipe, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
    derived = derive_packet(freeze, recipe, digest(ROOT / 'packet-freeze.json'),
                            digest(ROOT / 'comparison-config.json'), recipe_bytes)
    output.mkdir(parents=True)
    for pin in freeze['pins']:
        relative = Path(pin['path'])
        target = output / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('xb') as stream:
            stream.write(recipe_bytes if pin['path'] == 'recipe.json' else (ROOT / relative).read_bytes())
    with (output / 'packet-freeze.json').open('x') as stream:
        stream.write(json.dumps(derived, indent=2) + '\n')
    print(json.dumps({'condition': args.condition, 'sourceFreezeSHA256': digest(output / 'packet-freeze.json'),
                      'recipeSHA256': digest(output / 'recipe.json'), 'modelLoads': 0, 'engineCalls': 0,
                      'rootExecutionGo': False}))


if __name__ == '__main__':
    main()
