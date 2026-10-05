"""Model-free, exact regeneration of the previously consumed invented pixels."""
import argparse, importlib.util, json, zipfile, io, urllib.request
from pathlib import Path
from contracts import sha, verify_crop, verify_ids

ROOT = Path(__file__).resolve().parent

def fetch(url, target, maximum, expected_sha, expected_bytes=None):
    if target.exists():
        data = target.read_bytes()
    else:
        partial = target.with_name(target.name+'.part')
        owned = False
        try:
            with partial.open('xb') as dest:
                owned = True
                with urllib.request.urlopen(url, timeout=60) as response:
                    total = 0
                    while True:
                        chunk = response.read(65536)
                        if not chunk: break
                        total += len(chunk)
                        if total > maximum: raise ValueError('Asset download exceeds fixed bound')
                        dest.write(chunk)
            data = partial.read_bytes()
            if sha(data) != expected_sha or (expected_bytes is not None and len(data) != expected_bytes):
                raise ValueError('Official asset byte/hash mismatch')
            partial.rename(target)
        finally:
            if owned and partial.exists(): partial.unlink()
    if len(data) > maximum or sha(data) != expected_sha or (expected_bytes is not None and len(data) != expected_bytes):
        raise ValueError('Cached official asset byte/hash mismatch')
    return data

def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--owned', type=Path, required=True); args = parser.parse_args()
    owned = args.owned.resolve()
    if not owned.is_dir() or not (owned/'.owned-easyocr-row-probe').is_file():
        raise ValueError('Exact owned temporary directory marker required')
    font = json.loads((ROOT/'font-pin.json').read_text())
    font_path = owned/'font.ttc'
    fetch(font['url'], font_path, 21*1024*1024, font['sha256'], font['bytes'])
    artifact = json.loads((ROOT/'artifact-pin.json').read_text()); pin = artifact['files'][0]
    archive = fetch(artifact['url'], owned/'japanese_g2.zip', 17*1024*1024, artifact['zipSHA256'], artifact['releaseAssetBytes'])
    with zipfile.ZipFile(io.BytesIO(archive)) as z:
        entries = z.infolist()
        if len(entries) != 1 or entries[0].filename != 'japanese_g2.pth' or entries[0].file_size > 48*1024*1024:
            raise ValueError('Unexpected model inventory or expansion')
        weight = z.read(entries[0])
    if len(weight) != pin['bytes'] or sha(weight) != pin['sha256']:
        raise ValueError('Official weight byte/hash mismatch')
    model_dir = owned/'models'; model_dir.mkdir(exist_ok=True)
    with (model_dir/'japanese_g2.pth').open('xb') as f: f.write(weight)
    spec = importlib.util.spec_from_file_location('original_generator', ROOT/'fictional-source/generate.py')
    gen = importlib.util.module_from_spec(spec); spec.loader.exec_module(gen)
    source = json.loads((ROOT/'fictional-source/drawing-source.json').read_text())
    png, drawing = gen.render(source, font_path)
    del drawing, source
    recipe = json.loads((ROOT/'recipe.json').read_text())
    if sha(png) != recipe['inputPNG']['sha256'] or len(png) != recipe['inputPNG']['bytes']:
        raise ValueError('Original invented PNG changed')
    with (owned/'original.png').open('xb') as f: f.write(png)
    from PIL import Image
    image = Image.open(io.BytesIO(png)); image.load()
    rows = json.loads((ROOT/'rows-before-native.json').read_text())['rows']; verify_ids(rows)
    for row in rows: verify_crop(image, row)
    report = {'phase':'preparation', 'nativeCalls':0, 'PNGBytes':len(png), 'PNGSHA256':sha(png), 'rows':170,
              'allOriginal170RGBAndShapesVerified':True, 'fontSHA256':font['sha256'], 'modelSHA256':pin['sha256'],
              'noOracleToWorker':True, 'source':'previously consumed independent invented source, no original school PDF/image'}
    (owned/'preparation.json').write_text(json.dumps(report, indent=2)+'\n'); print(json.dumps(report), flush=True)

if __name__ == '__main__': main()
