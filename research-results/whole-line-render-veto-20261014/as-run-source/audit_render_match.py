#!/usr/bin/env python3
"""Known-font whole-line pixel veto on closed OCR outputs; never repair text."""
import argparse
import base64
import hashlib
import io
import json
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SIZES = list(range(8, 41))
CONFUSABLES = ('Il1', 'O0')


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def write(p, value):
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')


def above(r):
    return bool(r['text']) and r['minimumConfidence'] is not None and r['minimumConfidence'] >= .8 and not r['unsupportedPadding']


def worker(a):
    from PIL import Image, ImageChops, ImageDraw, ImageFont
    freeze = json.loads((a.output/'freeze.json').read_text())
    if sha(a.packet) != freeze['packetSha256'] or sha(a.raw) != freeze['readerRawSha256']:
        raise ValueError('Frozen pixel/reader input changed')
    for p in a.fonts:
        if sha(p) != freeze['fonts'][p.name]:
            raise ValueError('Frozen font changed')
    fonts = [(p.name, size, ImageFont.truetype(str(p), size)) for p in a.fonts for size in SIZES]
    readers = {}
    for line in a.raw.read_text().splitlines():
        r = json.loads(line)
        if r['model'] == 'v6-medium' and r['condition'] == 'bgr':
            if r['id'] in readers:
                raise ValueError('Duplicate reader ordinal')
            readers[r['id']] = r
    rows = json.loads(a.packet.read_text())['rows']
    if len(rows) != 192 or {r['id'] for r in rows} != set(readers):
        raise ValueError('Pixel/reader identity mismatch')
    started = time.monotonic()
    with (a.output/'unjudged.jsonl').open('x', encoding='utf-8', newline='\n') as stream:
        for row in rows:
            png = base64.b64decode(row['pngBase64'], validate=True)
            if hashlib.sha256(png).hexdigest() != row['pngSha256']:
                raise ValueError('Original PNG changed')
            with Image.open(io.BytesIO(png)) as source:
                image = source.convert('RGB')
            if image.size != (row['width'], row['height']):
                raise ValueError('Source dimensions changed')
            r = readers[row['id']]
            text = r['text']
            box = ImageChops.difference(image, Image.new('RGB', image.size, 'white')).getbbox()
            matches = []
            ambiguities = []
            rendered_count = 0
            # Empty/edge whitespace has no visible ownership proof. Never strip it.
            eligible = bool(box) and bool(text) and not text[0].isspace() and not text[-1].isspace() and len(text) <= 64 and not any(ord(c) < 32 for c in text)
            if eligible:
                colour = tuple(bounds[0] for bounds in image.getextrema())
                actual = image.crop(box)

                def matches_source(candidate, font):
                    nonlocal rendered_count
                    bounds = font.getbbox(candidate)
                    width, height = bounds[2]-bounds[0]+8, bounds[3]-bounds[1]+8
                    if width <= 0 or height <= 0 or width > 4096 or height > 256:
                        return False
                    canvas = Image.new('RGB', (width, height), 'white')
                    ImageDraw.Draw(canvas).text((4-bounds[0], 4-bounds[1]), candidate, font=font, fill=colour)
                    rendered_count += 1
                    ink = ImageChops.difference(canvas, Image.new('RGB', canvas.size, 'white')).getbbox()
                    if not ink:
                        return False
                    drawn = canvas.crop(ink)
                    if drawn.size != actual.size or drawn.tobytes() != actual.tobytes():
                        return False
                    whole = Image.new('RGB', image.size, 'white')
                    whole.paste(drawn, box[:2])
                    return whole.tobytes() == image.tobytes()

                for name, size, font in fonts:
                    if not matches_source(text, font):
                        continue
                    matches.append({'font': name, 'size': size})
                    # These are ambiguity witnesses only: never replacement outputs.
                    for index, char in enumerate(text):
                        alternatives = next((group for group in CONFUSABLES if char in group), '')
                        for alternate in alternatives:
                            if alternate == char:
                                continue
                            changed = text[:index]+alternate+text[index+1:]
                            if matches_source(changed, font):
                                ambiguities.append({'font': name, 'size': size, 'index': index, 'alternate': alternate})
            record = {'id': row['id'], 'rawText': text, 'nativeAboveFloor': above(r),
                      'sourceRgbSha256': hashlib.sha256(image.tobytes()).hexdigest(),
                      'renderMatches': matches, 'sameFontSizeConfusableWitnesses': ambiguities,
                      'renderOperations': rendered_count,
                      'retainedByDiagnosticVeto': above(r) and bool(matches) and not ambiguities}
            stream.write(json.dumps(record, ensure_ascii=False, separators=(',', ':'))+'\n')
            stream.flush()
    write(a.output/'worker-report.json', {'rows': len(rows), 'seconds': time.monotonic()-started,
          'additionalModelCalls': 0, 'qualification': False})


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--packet', type=Path, required=True)
    p.add_argument('--raw', type=Path, required=True)
    p.add_argument('--fonts', type=Path, nargs=2, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--worker', action='store_true')
    a = p.parse_args()
    if a.worker:
        worker(a)
        return
    from PIL import __version__ as pillow_version
    a.output.mkdir(parents=True, exist_ok=False)
    oracle = ROOT/'tools/vision-ocr-comparison/oracle.json'
    freeze = {'recipe': 'known-font-whole-line-exact-render-veto-consumed-v1',
              'sourceSha256': sha(Path(__file__)), 'packetSha256': sha(a.packet),
              'readerRawSha256': sha(a.raw), 'oracleSha256': sha(oracle),
              'fonts': {p.name: sha(p) for p in a.fonts}, 'sizes': SIZES,
              'pillowVersion': pillow_version, 'reader': 'v6-medium/bgr',
              'rule': 'Render unchanged whole raw text using each fixed font/size and pixel-derived RGB minima. Exact full RGB image equality at observed ink origin; no scaling, per-glyph normalization, whitespace/case/Unicode repair or confidence boost. Veto only original minCTC>=.8 supported outputs. Empty/edge whitespace refused.',
              'ambiguityCheck': 'Single-character Il1/O0 replacements at each matching font/size only; ambiguity witnesses, never output correction. Other Unicode, invisible characters, ligatures, scripts and other fonts are NOT proven unique.',
              'scope': 'Already consumed clean lossless Noto Sans/Serif source fonts. Known-font diagnostic only; not scanned/unseen-font/document/Windows qualification or independent precision evidence.',
              'additionalModelCalls': 0, 'qualification': False, 'modelActivation': False}
    write(a.output/'freeze.json', freeze)
    snapshot = a.output/'as-run-source'
    snapshot.mkdir()
    (snapshot/Path(__file__).name).write_bytes(Path(__file__).read_bytes())
    args = [sys.executable, str(Path(__file__).resolve()), '--worker', '--packet', str(a.packet.resolve()),
            '--raw', str(a.raw.resolve()), '--fonts', *(str(p.resolve()) for p in a.fonts), '--output', str(a.output.resolve())]
    with (a.output/'worker-stdout.txt').open('w') as stdout, (a.output/'worker-stderr.txt').open('w') as stderr:
        child = subprocess.run(args, stdout=stdout, stderr=stderr, timeout=600)
    raw = a.output/'unjudged.jsonl'
    write(a.output/'raw-receipt.json', {'exitCode': child.returncode, 'sha256': sha(raw) if raw.exists() else None,
          'bytes': raw.stat().st_size if raw.exists() else None})
    if child.returncode:
        raise RuntimeError('Observer failed; preserve partial raw, no precision credit')
    if sha(oracle) != freeze['oracleSha256'] or sha(Path(__file__)) != freeze['sourceSha256']:
        raise ValueError('Source/oracle changed')
    # Literal labels are read only after the opaque observer has exited and raw is closed.
    gold = {r['id']: r for r in json.loads(oracle.read_text())}
    records = [json.loads(line) for line in raw.read_text().splitlines()]
    if len(records) != 192 or {r['id'] for r in records} != set(gold):
        raise ValueError('Incomplete observer')
    groups = {}
    for r in records:
        g = gold[r['id']]
        v = groups.setdefault(g['cohort']+'/'+g['kind'], dict(rows=0, nativeCorrectAbove=0, nativeWrongAbove=0, renderMatches=0, retainedCorrect=0, retainedWrong=0, ambiguous=0))
        exact = r['rawText'] == g['literal']
        v['rows'] += 1
        v['nativeCorrectAbove'] += r['nativeAboveFloor'] and exact
        v['nativeWrongAbove'] += r['nativeAboveFloor'] and not exact
        v['renderMatches'] += bool(r['renderMatches'])
        v['retainedCorrect'] += r['retainedByDiagnosticVeto'] and exact
        v['retainedWrong'] += r['retainedByDiagnosticVeto'] and not exact
        v['ambiguous'] += bool(r['sameFontSizeConfusableWitnesses'])
    report = {'groups': groups, 'rawSha256': sha(raw), 'worker': json.loads((a.output/'worker-report.json').read_text()),
              'qualification': False, 'modelActivation': False, 'limitations': freeze['scope']+' '+freeze['ambiguityCheck']}
    write(a.output/'report.json', report)
    print(json.dumps(report, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
