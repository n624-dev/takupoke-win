#!/usr/bin/env python3
"""Fixed blind 4/A controls on an unused public font; never an adoption route."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import random
import shutil
import subprocess
import urllib.request

import fitz
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas

FONT_ROOT = 'https://raw.githubusercontent.com/google/fonts/295d98a7a0c17c68f1341eaeea354e7960ea70d3/ofl/inconsolata/'
FONT_SHA = '23ded25b447074d00659392bf9b1123d89df55cb07b0ad9bfef3366d199b5fcb'
LICENSE_SHA = '29bd0cfd0fb2a45f9b057c834a057724bae1f63b525a8ac83d3e7525706d9f80'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def run(args):
    args.owned.mkdir(exist_ok=False)
    try:
        for name, size, expected in [('Inconsolata%5Bwdth%2Cwght%5D.ttf', 347180, FONT_SHA),
                                     ('OFL.txt', 4350, LICENSE_SHA)]:
            data = urllib.request.urlopen(FONT_ROOT + name, timeout=60).read()
            assert len(data) == size and sha(data) == expected, 'Pinned public font differs'
            (args.owned / ('font.ttf' if name.endswith('.ttf') else name)).write_bytes(data)
        pdfmetrics.registerFont(TTFont('BlindDigitLetter', str(args.owned / 'font.ttf')))
        designs = [(size, phase, color, glyph) for size in (9, 10)
                   for phase in (0, .375) for color in ((0, 0, 0), (.05, .1, .3)) for glyph in ('4', 'A')]
        random.Random(104729).shuffle(designs)
        inputs, inventory = [], []
        source_pairs = {}
        for index, (size, phase, color, glyph) in enumerate(designs):
            literal = glyph + '_Q7'
            pdf = args.owned / f'{index:02}.pdf'
            painter = canvas.Canvas(str(pdf), pagesize=(48, 26), invariant=1)
            painter.setFont('BlindDigitLetter', size)
            painter.setFillColorRGB(*color)
            painter.drawString(3 + phase, 11 + phase, literal)
            painter.save()
            with fitz.open(pdf) as source:
                raster = source[0].get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
                rgb, width, height = raster.samples, raster.width, raster.height
                source_pairs.setdefault((size, phase, color), {})[glyph] = sha(rgb)
                ink = [(i // 3 % width, i // 3 // width) for i in range(0, len(rgb), 3)
                       if min(rgb[i:i + 3]) < 255]
                assert ink
                x1, x2 = max(0, min(x for x, y in ink) - 4), min(width, max(x for x, y in ink) + 5)
                y1, y2 = max(0, min(y for x, y in ink) - 4), min(height, max(y for x, y in ink) + 5)
                cropped = b''.join(rgb[(y * width + x1) * 3:(y * width + x2) * 3] for y in range(y1, y2))
                bgra = bytearray((x2 - x1) * (y2 - y1) * 4)
                for channel in range(3):
                    bgra[channel::4] = cropped[2 - channel::3]
                bgra[3::4] = bytes([255]) * ((x2 - x1) * (y2 - y1))
                inputs.append(dict(Index=index, Width=x2 - x1, Height=y2 - y1,
                                   Bgra=base64.b64encode(bgra).decode()))
                inventory.append(dict(index=index, expected=literal, sourcePixels=size * 2, phase=phase,
                                      color=color, sourceRGBSHA256=sha(rgb), inputRGBSHA256=sha(cropped),
                                      cropBox=[x1, y1, x2 - x1, y2 - y1]))
        assert len(source_pairs) == 8 and all(pair['4'] != pair['A'] for pair in source_pairs.values())
        pixels = args.owned / 'ordinal-pixels.json'
        pixels.write_text(json.dumps(inputs))
        # The child receives pixels and ordinal IDs only. Gold is consulted only
        # after all16 fixed calls return. No known-class vocabulary is present.
        child = subprocess.run([args.dotnet, str(args.dll), str(args.model), str(pixels),
                                str(args.alphabet), 'blind-digit-letter-v1'], capture_output=True, text=True, timeout=120)
        assert child.returncode == 0, child.stderr[:2000]
        native = json.loads(child.stdout)
        assert native['calls'] == 16 and len(native['outputs']) == 16 and not native['qualified']
        rows = []
        for expected, actual in zip(inventory, native['outputs']):
            assert expected['index'] == actual['Index']
            rows.append(dict(**expected, **actual, exact=expected['expected'] == actual['text']))
        report = dict(recipe='blind-digit-letter-v1', publicFontSHA256=FONT_SHA, fontLicenseSHA256=LICENSE_SHA,
                      inputRegion='all original nonwhite pixels plus fixed4px margin', calls=16, obligations=16,
                      sourceDistinctPairs=8, exact=sum(r['exact'] for r in rows),
                      exactDigit=sum(r['exact'] for r in rows if r['expected'].startswith('4')),
                      exactLetter=sum(r['exact'] for r in rows if r['expected'].startswith('A')),
                      exactAboveNativeThreshold=sum(r['exact'] and r['minimumConfidence'] is not None and r['minimumConfidence'] >= .8 for r in rows),
                      incorrectAboveNativeThreshold=sum(not r['exact'] and r['minimumConfidence'] is not None and r['minimumConfidence'] >= .8 for r in rows),
                      missing=sum(not r['text'] for r in rows), paddingUnsupported=sum(not r['paddingSupported'] for r in rows),
                      runtime=native['runtime'], modelSHA256=sha(args.model.read_bytes()), qualified=False,
                      adoptionCalls=0, scope='new4/A error band only; previous I/l failures remain unchanged', outputs=rows)
        print('BLIND_DIGIT_LETTER ' + json.dumps(report, ensure_ascii=False))
    finally:
        shutil.rmtree(args.owned)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for name in ('owned', 'dll', 'model', 'alphabet'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    run(parser.parse_args())
