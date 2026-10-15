#!/usr/bin/env python3
"""Prospective fictional code images; no PDF, school data or training."""
import argparse
import collections
import hashlib
import json
import os
from pathlib import Path
import random
import shutil
import subprocess
import urllib.request

from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
FONT_REV = '295d98a7a0c17c68f1341eaeea354e7960ea70d3'
KNOWN = [('SawarabiGothic-Regular.ttf', '04f2f9111bb69cb8d3706c27b44879c2c0fac593a11edf5cfe8cec1efc92c789'),
         ('SawarabiMincho-Regular.ttf', '86373c619710d1528aac3a4d47ce96834101bb38b057235b39e47b61ba143c6d')]
NEW = [('kleeone', 'KleeOne-Regular.ttf'), ('kaiseitokumin', 'KaiseiTokumin-Regular.ttf')]


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def write(p, value):
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')


def asset(p, root):
    return dict(file=p.relative_to(root).as_posix(), bytes=p.stat().st_size, sha256=sha(p))


def distance(a, b):
    row = list(range(len(b) + 1))
    for i, c in enumerate(a, 1):
        nxt = [i]
        for j, d in enumerate(b, 1):
            nxt.append(min(nxt[-1] + 1, row[j] + 1, row[j-1] + (c != d)))
        row = nxt
    return row[-1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--known-fonts', type=Path, required=True)
    parser.add_argument('--official', type=Path, required=True)
    parser.add_argument('--bundle', type=Path, required=True)
    parser.add_argument('--dotnet', required=True)
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    fonts = []
    downloads = []
    for name, digest in KNOWN:
        source = args.known_fonts / name
        if sha(source) != digest:
            raise ValueError('Known font changed')
        target = out / name
        shutil.copyfile(source, target)
        fonts.append((target, 'development'))
    for folder, name in NEW:
        base = f'https://raw.githubusercontent.com/google/fonts/{FONT_REV}/ofl/{folder}/'
        for filename in (name, 'OFL.txt'):
            target = out / (name if filename == name else folder + '-OFL.txt')
            target.write_bytes(urllib.request.urlopen(base + filename, timeout=40).read())
            downloads.append(dict(url=base + filename, **asset(target, out)))
        fonts.append((out / name, 'new-font-evaluation'))
    write(out / 'font-downloads.json', downloads)
    models = []
    specs = [
        ('official-english-v5', args.official / 'inference.onnx', args.official / 'dictionary.json',
         'b5f833dfc5d0eb71da397b4efa06ebeee9b431b690a47d6af40d77d8eabc557f', 'ef84489f927fb92b64714c7f110404e5bfb83c4067bd609fe4c41d5f23e25659'),
        ('deployed-trained-english-v5', args.bundle / 'ascii-rec.onnx', args.bundle / 'ascii-dictionary.json',
         '9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703', 'fbf156ed886e4c46075f599651e60a0c38953dce89e10d999c765014477a7368'),
        ('japanese-v6', args.bundle / 'rec.onnx', ROOT / 'src/Takupoke.Win/Assets/Recovery/ocr-characters-v6.json',
         '9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba', 'a49d51120786924a642c9e8a14b2a2ab73c019699abf5b2b6970ec7355c0415f')]
    for name, model, dictionary, model_sha, dict_sha in specs:
        if sha(model) != model_sha or sha(dictionary) != dict_sha:
            raise ValueError('Pinned model/dictionary changed: ' + name)
        paths = []
        for source, suffix in [(model, '.onnx'), (dictionary, '.json')]:
            target = out / (name + suffix)
            shutil.copyfile(source, target)
            paths.append(asset(target, out))
        models.append(dict(id=name, model=paths[0], dictionary=paths[1]))
    write(out / 'protocol.json', dict(recipe='independent-code-images-v1', seed=202610150913,
        fonts=[dict(split=s, **asset(p, out)) for p, s in fonts], sizes=[18, 26],
        cleanCodesPerFontAndSize=24, confusableCodesPerFontAndSize=24, margins=[2, 6],
        metrics=['literal exact', 'CER', 'wrong above native .8', 'blank output', 'time', 'RSS'],
        scope='Isolated fictional code images. Paired margins are NOT independent samples. No PDF/detection/whole-document or production qualification.',
        noModelUpdate=True, noOracleRouting=True, noZeroErrorGate=True))
    rows, oracle, ambiguity = [], [], []
    rng = random.Random(202610150913)
    for font_path, split in fonts:
        for size in [18, 26]:
            font = ImageFont.truetype(str(font_path), size)
            glyphs = {}
            for char in 'Il1O0':
                image = Image.new('RGB', (size * 3, size * 3), 'white')
                ImageDraw.Draw(image).text((size, 0), char, font=font, fill='black')
                ink = ImageChops.invert(image).getbbox()
                glyphs[char] = dict(advance=font.getlength(char), inkBox=ink,
                    inkSHA256=hashlib.sha256(image.crop(ink).tobytes()).hexdigest())
            ambiguity.append(dict(font=font_path.name, size=size, glyphs=glyphs,
                scope='Same standalone ink is a counterexample; it does not prove whole strings or spacing are indistinguishable.'))
            values = []
            for n in range(24):
                values.append(('ordinary', rng.choice('ABCDEFGHJKLMNPQRSTUVWXYZ') + str(rng.randrange(200, 999)) + rng.choice(['', '-A', '-B'])))
                values.append(('confusable', rng.choice('Il1O0') + rng.choice('Il1O0') + rng.choice('Il1O0') + str(rng.randrange(20, 99))))
            for group, value in values:
                canvas = Image.new('RGB', (220, 80), 'white')
                ImageDraw.Draw(canvas).text((10, 5), value, font=font, fill='black')
                bounds = ImageChops.invert(canvas).getbbox()
                for margin in [2, 6]:
                    crop = canvas.crop((bounds[0]-margin, bounds[1]-margin, bounds[2]+margin, bounds[3]+margin))
                    ordinal = f'code-{len(rows):04d}'
                    path = out / (ordinal + '.bgra')
                    path.write_bytes(crop.convert('RGBA').tobytes('raw', 'BGRA'))
                    rows.append(dict(id=ordinal, width=crop.width, height=crop.height, pixels=asset(path, out)))
                    oracle.append(dict(id=ordinal, value=value, font=font_path.name, split=split,
                        size=size, group=group, margin=margin))
        ordinal = f'code-{len(rows):04d}'
        path = out / (ordinal + '.bgra')
        path.write_bytes(Image.new('RGBA', (80, 30), 'white').tobytes('raw', 'BGRA'))
        rows.append(dict(id=ordinal, width=80, height=30, pixels=asset(path, out)))
        oracle.append(dict(id=ordinal, value='', font=font_path.name, split=split, size=0, group='blank', margin=0))
    write(out / 'glyph-ambiguity.json', ambiguity)
    write(out / 'inputs.json', dict(recipe='independent-code-images-v1', models=models, rows=rows))
    write(out / 'oracle.json', oracle)
    env = {k: v for k, v in os.environ.items() if not any(s in k.upper() for s in ['TOKEN', 'SECRET', 'PASSWORD', 'CREDENTIAL'])}
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT='1', ORT_TELEMETRY_DISABLED='1')
    subprocess.run([args.dotnet, 'build', str(Path(__file__).parent / 'CodeProbe.csproj'), '-c', 'Release', '--verbosity', 'quiet'],
                   env=env, check=True, timeout=180)
    reader = out / 'reader'
    shutil.copytree(Path(__file__).parent / 'bin/Release/net10.0', reader)
    freeze = dict(inputs=asset(out/'inputs.json', out), oracle=asset(out/'oracle.json', out),
        protocol=asset(out/'protocol.json', out), reader=[asset(p, out) for p in sorted(reader.rglob('*')) if p.is_file()],
        sources={p.name: sha(p) for p in Path(__file__).parent.glob('*') if p.is_file()})
    write(out / 'freeze.json', freeze)
    with (out/'stdout.txt').open('w') as stdout, (out/'stderr.txt').open('w') as stderr:
        child = subprocess.run([args.dotnet, str(reader/'Takupoke.OcrComparison.dll'), str(out/'inputs.json'), str(out/'raw.jsonl')],
                               env=env, stdout=stdout, stderr=stderr, timeout=660)
    write(out/'worker-receipt.json', dict(exitCode=child.returncode, raw=asset(out/'raw.jsonl', out)))
    if child.returncode:
        raise RuntimeError('Worker failed; raw retained unscored')
    for record in [freeze['inputs'], freeze['oracle'], freeze['protocol'], *freeze['reader']]:
        if asset(out/record['file'], out) != record:
            raise ValueError('Frozen input/reader changed')
    for record in [r['pixels'] for r in rows] + [m[k] for m in models for k in ['model', 'dictionary']]:
        if asset(out/record['file'], out) != record:
            raise ValueError('Inference asset changed')
    gold = {v['id']: v for v in json.loads((out/'oracle.json').read_text())}
    observations = [json.loads(line) for line in (out/'raw.jsonl').read_text().splitlines()]
    if len(observations) != len(rows)*len(models) or len({(v['id'], v['model']) for v in observations}) != len(observations):
        raise ValueError('Missing/duplicate observations')
    groups = collections.defaultdict(list)
    for observation in observations:
        expected = gold[observation['id']]
        groups[observation['model'], expected['font'], expected['group'], expected['margin']].append((observation, expected))
    scores = []
    for (model, font, group, margin), items in sorted(groups.items()):
        edits = sum(distance(g['value'], v['text']) for v, g in items)
        chars = sum(len(g['value']) for _, g in items)
        native_pass = lambda v: bool(v['pieces']) and v['paddingSupported'] and (v['reject'] is None or v['reject'] <= .05) and min(p['Confidence'] for p in v['pieces']) >= .8
        scores.append(dict(model=model, font=font, group=group, margin=margin, count=len(items),
            exact=sum(v['text']==g['value'] for v,g in items), edits=edits, characters=chars,
            CER=edits/chars if chars else None, wrongAboveNativeThreshold=sum(native_pass(v) and v['text']!=g['value'] for v,g in items),
            milliseconds=sum(v['milliseconds'] for v,_ in items)))
    report = dict(rows=len(rows), distinctCodeRenderings=384, calls=len(observations), scores=scores,
        raw=asset(out/'raw.jsonl', out), peakWorkingSetBytes=max(v['peakWorkingSetBytes'] for v in observations),
        scope='New code images; independent font evaluation only for KleeOne and KaiseiTokumin. Isolated recognition, not whole PDF or human correction time.',
        productModelChanged=False)
    write(out/'report.json', report)
    print(json.dumps(report, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
