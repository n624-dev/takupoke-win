"""Restore consumed fictional Windows pixels; no OCR or new accuracy credit."""
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


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    fonts = output / 'fonts'
    fonts.mkdir()
    models = output / 'unused-models'
    models.mkdir()
    protocol = json.loads((ROOT / 'tools/ocr-literal-retry-accuracy/protocol.json').read_text())
    for asset in protocol['fontPins']:
        path = fonts / asset['file']
        with urllib.request.urlopen(asset['url'], timeout=60) as response, path.open('xb') as target:
            shutil.copyfileobj(response, target)
        assert path.stat().st_size == asset['bytes'] and sha(path) == asset['sha256']
    study = output / 'study'
    subprocess.run([sys.executable, str(ROOT / 'tools/ocr-literal-retry-accuracy/prepare.py'),
                    '--fonts', str(fonts), '--output', str(study)], check=True)
    path = study / 'inputs.json'
    manifest = json.loads(path.read_text())
    manifest['documents'] = [d for d in manifest['documents'] if d['id'].endswith('-positive')]
    assert len(manifest['documents']) == 2
    path.write_text(json.dumps(manifest, indent=2) + '\n')
    subprocess.run([sys.executable, str(ROOT / 'tools/ocr-physical-framing/prepare_pdf.py'),
                    '--manifest', str(path)], check=True)
    project = ROOT / 'tools/ocr-physical-framing/windows-render/WindowsRender.csproj'
    subprocess.run([args.dotnet, 'build', str(project), '-c', 'Release', '--nologo'], check=True)
    dll = project.parent / 'bin/Release/net10.0-windows10.0.26100.0/win-x64/WindowsRender.dll'
    subprocess.run([args.dotnet, str(dll), str(study / 'pdf-inputs.json')], check=True)
    expected = json.loads((ROOT / 'research-results/ocr-literal-retry-first-20261016/study/native-inputs.json').read_text())
    actual = json.loads((study / 'native-inputs.json').read_text())
    lookup = {d['id']: d for d in expected['documents']}
    records = output / 'records'
    records.mkdir()
    verified = []
    for doc in actual['documents']:
        assert len(doc['pages']) == len(lookup[doc['id']]['pages']) == 5
        for page, old in zip(doc['pages'], lookup[doc['id']]['pages']):
            matches = (page['page'], page['width'], page['height'], page['pixels']) == (old['page'], old['width'], old['height'], old['pixels'])
            image = study / page['pixels']['file']
            assert image.stat().st_size == page['pixels']['bytes'] and sha(image) == page['pixels']['sha256']
            shutil.copyfile(image, records / image.name)
            verified.append({'id': doc['id'], 'page': page['page'], **page['pixels'],
                             'expectedSHA256': old['pixels']['sha256'], 'exact': matches})
    assert len(verified) == 10
    for name in ['native-inputs.json', 'native-render-receipt.json', 'generation-freeze.json']:
        shutil.copyfile(study / name, records / name)
    exact = all(p['exact'] for p in verified)
    receipt = dict(sourceCommit=os.environ.get('GITHUB_SHA'), referenceRun=38859980186,
                   all10OriginalNativePixelHashesExact=exact, modelCalls=0, independentAccuracyCredit=False,
                   pages=verified, scope='Consumed fictional originals only. Restore exact Windows pixels for development; full-page BGRA stays outside public source. No private documents or training.')
    (records / 'restoration-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps(dict(restoredPages=len(verified), modelCalls=0, exact=exact)), flush=True)
    if not exact:
        raise ValueError('Pixels differ from reference; retained outputs are not original-pixel restoration')
    project = ROOT / 'tools/ocr-literal-retry-study/CropDiagnostic.csproj'
    subprocess.run([args.dotnet, 'build', str(project), '-c', 'Release', '--nologo'], check=True)
    dll = project.parent / 'bin/diagnostic/net10.0/Takupoke.OcrComparison.dll'
    subprocess.run([args.dotnet, str(dll), '--bundle', str(output / 'bundle.json')], check=True)
    bundle = json.loads((output / 'bundle.json').read_text())
    assert bundle['Version'] == '20261016-v2' and len(bundle['Artifacts']) == 5
    model_root = study / 'models'
    model_root.mkdir()
    for item in bundle['Artifacts']:
        p = model_root / item['Path']
        with urllib.request.urlopen(item['Url'], timeout=60) as response, p.open('xb') as target:
            shutil.copyfileobj(response, target)
        assert p.stat().st_size == item['Size'] and sha(p) == item['Sha256']
    dictionary = study / 'hybrid-dictionary.json'
    shutil.copyfile(ROOT / 'src/Takupoke.Win/Assets/Recovery/ocr-characters-v6.json', dictionary)
    actual['hybridDictionary'] = dict(file=dictionary.name, bytes=dictionary.stat().st_size, sha256=sha(dictionary))
    (study / 'native-inputs.json').write_text(json.dumps(actual, indent=2) + '\n')
    subprocess.run([args.dotnet, str(dll), str(study / 'native-inputs.json'), str(records / 'crop-diagnostics.jsonl')], check=True)


if __name__ == '__main__':
    main()
