"""Restore exact qualified fictional pixels and validate the deployed factory."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).parent
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
write = lambda p, v: p.write_text(json.dumps(v, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    reference = json.loads((HERE / 'factory-reference.json').read_text())
    assert sha(HERE / 'protocol.json') == reference['protocolSHA256']
    for relative, digest in reference['qualifiedRuntimeSources'].items():
        if relative != 'src/Takupoke.Infrastructure/Recovery/RecoveryOcrModels.cs':
            assert sha(ROOT / relative) == digest, 'Recognition/capture method changed after qualification'
    protocol = json.loads((HERE / 'protocol.json').read_text())
    fonts = output / 'fonts'
    fonts.mkdir()
    for spec in protocol['fontPins']:
        path = fonts / spec['file']
        with urllib.request.urlopen(spec['url'], timeout=60) as response, path.open('xb') as target:
            shutil.copyfileobj(response, target)
        assert path.stat().st_size == spec['bytes'] and sha(path) == spec['sha256']
    study = output / 'study'
    subprocess.run([sys.executable, str(HERE / 'prepare.py'), '--fonts', str(fonts), '--output', str(study)], check=True)
    manifest = json.loads((study / 'inputs.json').read_text())
    manifest['documents'] = [d for d in manifest['documents'] if d['id'].endswith('-positive')]
    assert len(manifest['documents']) == 2
    dictionary = study / 'hybrid-dictionary.json'
    shutil.copyfile(ROOT / 'src/Takupoke.Win/Assets/Recovery/ocr-characters-v6.json', dictionary)
    manifest['hybridDictionary'] = dict(file=dictionary.name, bytes=dictionary.stat().st_size, sha256=sha(dictionary))
    write(study / 'inputs.json', manifest)
    subprocess.run([sys.executable, str(ROOT / 'tools/ocr-physical-framing/prepare_pdf.py'), '--manifest', str(study / 'inputs.json')], check=True)
    renderer_project = ROOT / 'tools/ocr-physical-framing/windows-render/WindowsRender.csproj'
    subprocess.run([args.dotnet, 'build', str(renderer_project), '-c', 'Release', '--nologo'], check=True)
    renderer = renderer_project.parent / 'bin/Release/net10.0-windows10.0.26100.0/win-x64/WindowsRender.dll'
    subprocess.run([args.dotnet, str(renderer), str(study / 'pdf-inputs.json')], check=True)
    native = study / 'native-inputs.json'
    generated = json.loads(native.read_text())
    expected = {d['id']: d for d in reference['nativeDocuments']}
    verified = []
    for doc in generated['documents']:
        old = expected[doc['id']]
        assert doc['originalPDFSHA256'] == old['originalPDFSHA256']
        assert doc['pages'] == old['pages']
        for page in doc['pages']:
            path = study / page['pixels']['file']
            assert path.stat().st_size == page['pixels']['bytes'] and sha(path) == page['pixels']['sha256']
            verified.append(dict(id=doc['id'], page=page['page'], sha256=sha(path)))
    assert len(verified) == 10
    write(study / 'original-pixel-restoration.json', dict(referenceRun=reference['referenceRun'], all10Exact=True, pages=verified, independentAccuracyCredit=False))
    project = HERE / 'Factory.csproj'
    subprocess.run([args.dotnet, 'build', str(project), '-c', 'Release', '--nologo'], check=True)
    source_reader = HERE / 'bin/factory/net10.0'
    reader = study / 'reader'
    shutil.copytree(source_reader, reader)
    dll = reader / 'Takupoke.OcrComparison.dll'
    subprocess.run([args.dotnet, str(dll), '--bundle', str(study / 'factory-bundle.json')], check=True)
    bundle = json.loads((study / 'factory-bundle.json').read_text())
    assert bundle['Version'] == '20261016-v3' and len(bundle['Artifacts']) == 7
    assert sum(a['Size'] for a in bundle['Artifacts']) == 135868279
    models = study / 'models'
    models.mkdir()
    for spec in bundle['Artifacts']:
        path = models / spec['Path']
        with urllib.request.urlopen(spec['Url'], timeout=60) as response, path.open('xb') as target:
            shutil.copyfileobj(response, target)
        assert path.stat().st_size == spec['Size'] and sha(path) == spec['Sha256']
    sources = [*(ROOT / 'src/Takupoke.Core/Recovery').glob('*.cs'), *(ROOT / 'src/Takupoke.Infrastructure/Recovery').glob('*.cs'),
               *HERE.glob('*.py'), *HERE.glob('*.cs'), *HERE.glob('*.csproj'), *HERE.glob('*.json'),
               ROOT / 'tools/ocr-physical-framing/prepare_pdf.py', ROOT / 'tools/ocr-physical-framing/windows-render/WindowsRender.cs']
    write(study / 'freeze.json', dict(sourceCommit=os.environ.get('GITHUB_SHA'), referenceRun=reference['referenceRun'],
        inputSHA256=sha(native), sourceSHA256={str(p.relative_to(ROOT)): sha(p) for p in sources},
        reader={str(p.relative_to(reader)): sha(p) for p in reader.rglob('*') if p.is_file()}, workerReadsOracle=False))
    env = {k: v for k, v in os.environ.items() if not any(s in k.upper() for s in ['TOKEN', 'SECRET', 'PASSWORD', 'CREDENTIAL'])}
    env.update(ORT_TELEMETRY_DISABLED='1', DOTNET_CLI_TELEMETRY_OPTOUT='1')
    raw = study / 'factory-observations.jsonl'
    with (study / 'factory-stdout.txt').open('x') as stdout, (study / 'factory-stderr.txt').open('x') as stderr:
        result = subprocess.run([args.dotnet, str(dll), str(native), str(raw), 'candidate'], env=env, stdout=stdout, stderr=stderr, timeout=1200)
    write(study / 'factory-receipt.json', dict(exitCode=result.returncode, rawSHA256=sha(raw) if raw.exists() else None))
    assert result.returncode == 0 and sha(native) == json.loads((study / 'freeze.json').read_text())['inputSHA256']
    assert all(sha(reader / n) == v for n, v in json.loads((study / 'freeze.json').read_text())['reader'].items())
    subprocess.run([sys.executable, str(HERE / 'compare_factory.py'), '--actual', str(raw), '--reference', str(HERE / 'factory-reference.json'), '--output', str(study / 'factory-equivalence.json')], check=True)


if __name__ == '__main__':
    main()
