"""New development controls on consumed fonts, never independent qualification."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import random
import string
from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--fonts', type=Path, required=True)
    p.add_argument('--model-manifest', type=Path, required=True)
    p.add_argument('--detector', type=Path, required=True)
    p.add_argument('--recognizer', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    out = a.output.resolve()
    out.mkdir(exist_ok=False)
    base = json.loads(a.model_manifest.read_text())
    model = dict(v1=a.model_manifest.parent / base['v1']['file'], v3=a.model_manifest.parent / base['v3']['file'],
                 dictionary=a.model_manifest.parent / base['dictionary']['file'],
                 japaneseDictionary=a.model_manifest.parent / base['hybridDictionary']['file'],
                 japaneseAlternative=a.model_manifest.parent / base['japaneseAlternative']['file'],
                 japaneseAlternativeDictionary=a.model_manifest.parent / base['japaneseAlternativeDictionary']['file'],
                 detector=a.detector, recognizer=a.recognizer)
    sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    asset = lambda p: dict(file=p.name, bytes=p.stat().st_size, sha256=sha(p))
    m = {}
    for key, source in model.items():
        target = out / (key + source.suffix)
        os.link(source, target)
        m[key] = asset(target)
    font_files = [a.fonts / name for name in ['ZenKurenaido-Regular.ttf', 'KaiseiDecol-Regular.ttf']]
    cmaps = [TTFont(path).getBestCmap() for path in font_files]
    proposed = ['渡辺', '渡邊', '渡邉', '高崎', '高﨑', '賈', '祁', '鄭', '劉', '髙']
    rare = [s for s in proposed if all(all(ord(c) in cmap for c in s) for cmap in cmaps)]
    assert rare
    rows, oracle = [], []
    for f, font in enumerate(font_files):
        face = ImageFont.truetype(str(font), 20)
        rng = random.Random(20261016209 + f)
        values = []
        for n in range(40):
            while True:
                code = ''.join(rng.choice(string.ascii_letters + string.digits + '_-') for _ in range(rng.randrange(2, 9)))
                if any(c.isdigit() for c in code) and any(c.isalpha() for c in code):
                    break
            values.append(('mixed', '教室', ('架空室' if n % 2 else '') + code))
        fixed = ['JO', 'OO', 'IO', 'SO', 'RIO', 'BIO', 'IlI', 'OlO', 'lO', 'OIl', 'LAB', 'SOLO', 'ROOM', 'OIL']
        for n in range(40):
            code = fixed[n] if n < len(fixed) else ''.join(rng.choice(string.ascii_letters) for _ in range(rng.randrange(2, 9)))
            values.append(('true-alphabetic', '教室', ('架空室' if n % 2 else '') + code))
        for n in range(10):
            values.append(('numeric-only', '教室', str(rng.randrange(100, 10000))))
            values.append(('math-symbol', '教室', f'R{rng.randrange(1, 9)}×{rng.randrange(1, 9)}'))
        for n in range(20):
            values.append(('rare-japanese', '担当', '仮想' + rare[n % len(rare)] + str(rng.randrange(1000, 10000))))
            values.append(('english-teacher', '担当', 'Dr' + ''.join(rng.choice(string.ascii_letters) for _ in range(6))))
            values.append(('english-subject', '科目', ''.join(rng.choice(string.ascii_letters) for _ in range(7))))
        for n, (group, label, value) in enumerate(values):
            text = label + '：' + value
            assert all(ord(c) in cmaps[f] for c in text)
            image = Image.new('RGB', (512, 48), 'white')
            pen = ImageDraw.Draw(image)
            assert pen.textlength(text, font=face) < 496
            pen.text((8, 8), text, font=face, fill='black', anchor='lt')
            id = f'font{f}-{n:03}'
            pixels = out / (id + '.bgra')
            pixels.write_bytes(image.convert('RGBA').tobytes('raw', 'BGRA'))
            rows.append(dict(id=id, width=512, height=48, pixels=asset(pixels)))
            oracle.append(dict(id=id, font=font.name, group=group, label=label, expected=value))
    m.update(rows=rows, scope='Consumed-font development controls; paired actual full detector/recognizer/refinement, no whole-document or fresh credit')
    for name, value in [('inputs.json', m), ('oracle.json', oracle)]:
        (out / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')
    (out / 'generation-freeze.json').write_text(json.dumps(dict(sourceSHA256=sha(Path(__file__)), fonts={p.name: asset(p) for p in font_files},
        seed=20261016209, modelCalls=0, controls=['true-alphabetic', 'numeric-only', 'math-symbol', 'rare-japanese', 'english-teacher', 'english-subject'],
        proposedRareNames=proposed, supportedRareNames=rare, rows=len(rows), scope=m['scope']), ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(dict(rows=len(rows), modelCalls=0, supportedRareNames=rare), ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
