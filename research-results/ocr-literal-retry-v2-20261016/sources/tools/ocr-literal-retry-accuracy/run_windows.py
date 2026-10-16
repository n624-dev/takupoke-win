"""Fixed paired native Windows trial; separate10446/current binaries, no oracle in workers."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).parent
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
write = lambda p, v: p.write_text(json.dumps(v, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
asset = lambda p: dict(file=p.name, bytes=p.stat().st_size, sha256=sha(p))


def download(spec, path):
    with urllib.request.urlopen(spec.get('url', spec.get('Url')), timeout=60) as response, path.open('xb') as target:
        shutil.copyfileobj(response, target)
    assert path.stat().st_size == spec.get('bytes', spec.get('Size'))
    assert sha(path) == spec.get('sha256', spec.get('Sha256'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    protocol = json.loads((HERE / 'protocol.json').read_text())
    from prepare import excluded_literals
    assert len(excluded_literals()) == protocol['excludeLiteralCount'], 'Validate the full exclusion union before downloading/building'
    for relative, digest in protocol['candidateRuntimeSources'].items():
        assert sha(ROOT / relative) == digest, 'Candidate changed after prospective registration'
    fonts = output / 'fonts'
    fonts.mkdir()
    for spec in protocol['fontPins']:
        download(spec, fonts / spec['file'])
    archive = output / 'baseline-source.zip'
    subprocess.run(['git', 'archive', '--format=zip', '-o', str(archive), protocol['baselineCommit']], cwd=ROOT, check=True)
    historical = output / 'baseline-source'
    historical.mkdir()
    with zipfile.ZipFile(archive) as z:
        z.extractall(historical)
    old_tools = historical / 'tools/ocr-literal-retry-accuracy'
    old_tools.mkdir(exist_ok=False)
    for file in ['Read.cs', 'Baseline.csproj']:
        shutil.copyfile(HERE / file, old_tools / file)
    subprocess.run([args.dotnet, 'build', str(old_tools / 'Baseline.csproj'), '-c', 'Release', '--nologo'], check=True)
    subprocess.run([args.dotnet, 'build', str(HERE / 'Read.csproj'), '-c', 'Release', '--nologo'], check=True)
    old_reader = old_tools / 'bin/Release/net10.0'
    candidate_reader = HERE / 'bin/Release/net10.0'
    subprocess.run([args.dotnet, str(old_reader / 'Takupoke.OcrComparison.dll'), '--bundle', str(output / 'baseline-bundle.json')], check=True)
    subprocess.run([args.dotnet, str(candidate_reader / 'Takupoke.OcrComparison.dll'), '--bundle', str(output / 'candidate-bundle.json')], check=True)
    bundle = json.loads((output / 'baseline-bundle.json').read_text())
    assert bundle == json.loads((output / 'candidate-bundle.json').read_text())
    assert bundle['Version'] == protocol['baselineProfile'] and len(bundle['Artifacts']) == 5
    study = output / 'study'
    # Generation begins only after registration and source checks above.
    subprocess.run([sys.executable, str(HERE / 'prepare.py'), '--fonts', str(fonts), '--output', str(study)], check=True)
    models = study / 'models'
    models.mkdir()
    downloads = []
    for spec in bundle['Artifacts']:
        download(spec, models / spec['Path'])
        downloads.append(spec)
    alt = study / 'japanese-alternative.onnx'
    download(protocol['japaneseAlternative'], alt)
    downloads.append(protocol['japaneseAlternative'])
    spec = protocol['japaneseAlternativeDictionary']
    dictionary = ROOT / spec['file']
    assert dictionary.stat().st_size == spec['bytes'] and sha(dictionary) == spec['sha256']
    alternate_dictionary = study / 'japanese-alternative-dictionary.json'
    shutil.copyfile(dictionary, alternate_dictionary)
    hybrid = study / 'hybrid-dictionary.json'
    shutil.copyfile(ROOT / 'src/Takupoke.Win/Assets/Recovery/ocr-characters-v6.json', hybrid)
    write(output / 'download-receipt.json', downloads)
    m = json.loads((study / 'inputs.json').read_text())
    m.update(hybridDictionary=asset(hybrid), japaneseAlternative=asset(alt), japaneseAlternativeDictionary=asset(alternate_dictionary))
    write(study / 'inputs.json', m)
    subprocess.run([sys.executable, str(ROOT / 'tools/ocr-physical-framing/prepare_pdf.py'), '--manifest', str(study / 'inputs.json')], check=True)
    renderer_project = ROOT / 'tools/ocr-physical-framing/windows-render/WindowsRender.csproj'
    subprocess.run([args.dotnet, 'build', str(renderer_project), '-c', 'Release', '--nologo'], check=True)
    renderer = renderer_project.parent / 'bin/Release/net10.0-windows10.0.26100.0/win-x64/WindowsRender.dll'
    sources = [*(ROOT / 'src/Takupoke.Core/Recovery').glob('*.cs'), *(ROOT / 'src/Takupoke.Infrastructure/Recovery').glob('*.cs'),
               *HERE.glob('*.py'), *HERE.glob('*.cs'), *HERE.glob('*.json'), *HERE.glob('*.csproj'),
               ROOT / 'tools/ocr-acquisition-replay/InspectAppSchema.cs', ROOT / 'tools/ocr-physical-framing/prepare_pdf.py',
               ROOT / 'tools/ocr-physical-framing/windows-render/WindowsRender.cs', dictionary]
    write(study / 'freeze.json', dict(recipe=protocol['recipe'], candidateSourceCommit=os.environ.get('GITHUB_SHA'),
        baselineSourceCommit=protocol['baselineCommit'], baselineArchiveSHA256=sha(archive), protocolSHA256=sha(HERE / 'protocol.json'),
        inputSHA256=sha(study / 'pdf-inputs.json'), oracleSHA256=sha(study / 'oracle.json'), rendererSHA256=sha(renderer),
        sourceSHA256={str(p.relative_to(ROOT)): sha(p) for p in sources}, modelSelectionBeforeGeneration=True))
    subprocess.run([args.dotnet, str(renderer), str(study / 'pdf-inputs.json')], check=True)
    manifest = study / 'native-inputs.json'
    readers = {}
    for arm, folder in [('baseline', old_reader), ('candidate', candidate_reader)]:
        target = study / 'readers' / arm
        shutil.copytree(folder, target)
        readers[arm] = dict(inputSHA256=sha(manifest), files={str(p.relative_to(target)): dict(bytes=p.stat().st_size, sha256=sha(p)) for p in target.rglob('*') if p.is_file()},
                            goldReadByWorker=False, sourceCommit=protocol['baselineCommit'] if arm == 'baseline' else os.environ.get('GITHUB_SHA'))
    write(study / 'execution-freeze.json', dict(arms=readers, sameOriginalManifest=True, baselineSourceCommit=protocol['baselineCommit']))
    env = {k: v for k, v in os.environ.items() if not any(s in k.upper() for s in ['TOKEN', 'SECRET', 'PASSWORD', 'CREDENTIAL'])}
    env.update(ORT_TELEMETRY_DISABLED='1', DOTNET_CLI_TELEMETRY_OPTOUT='1')
    for arm in ['baseline', 'candidate']:
        reader = study / 'readers' / arm
        raw = study / (arm + '-observations.jsonl')
        assert not raw.exists() and sha(manifest) == readers[arm]['inputSHA256']
        with (study / (arm + '-stdout.txt')).open('x') as stdout, (study / (arm + '-stderr.txt')).open('x') as stderr:
            result = subprocess.run([args.dotnet, str(reader / 'Takupoke.OcrComparison.dll'), str(manifest), str(raw), arm],
                                    env=env, stdout=stdout, stderr=stderr, timeout=1500)
        write(study / (arm + '-receipt.json'), dict(exitCode=result.returncode, rawSHA256=sha(raw) if raw.exists() else None, bytes=raw.stat().st_size if raw.exists() else None))
        print(json.dumps(dict(arm=arm, closed=True, exitCode=result.returncode)), flush=True)
        if result.returncode:
            raise RuntimeError('Worker failed; preserve all raw observations, no qualification')
        assert all(sha(reader / name) == value['sha256'] for name, value in readers[arm]['files'].items())
    assert sha(study / 'oracle.json') == json.loads((study / 'freeze.json').read_text())['oracleSHA256']
    subprocess.run([sys.executable, str(HERE / 'score.py'), '--study', str(study), '--dotnet', args.dotnet], check=True)


if __name__ == '__main__':
    main()
