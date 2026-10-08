#!/usr/bin/env python3
"""Audit I/l pixels in the actual app input tensor, without any OCR/model call.

The input transformer receives only ordinal BGRA pixels. Evaluation regions are
computed separately from the pinned public font's outlines. Natural glyph size,
advance, crop and resize geometry are preserved; unequal regions are reported.
"""
import argparse
import base64
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import random
import subprocess
import tempfile

import fitz
from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont
from reportlab.pdfgen import canvas


def sha(data):
    return hashlib.sha256(data).hexdigest()


def run(dotnet):
    root = Path(__file__).resolve().parents[2]
    with tempfile.TemporaryDirectory(prefix='takupoke-tensor-audit-') as directory:
        owned = Path(directory)
        spec = importlib.util.spec_from_file_location('audit_public_font',
            root / 'tools/independent-wide-timetable/generate.py')
        font = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(font)
        font.install_font(owned)
        with TTFont(owned / 'public-font-static.ttf') as face:
            glyphs, cmap = face.getGlyphSet(), face.getBestCmap()
            units = face['head'].unitsPerEm
            advance = face['hmtx'][cmap[ord('A')]][0]
            bounds = {}
            for letter in ('I', 'l'):
                pen = BoundsPen(glyphs)
                glyphs[cmap[ord(letter)]].draw(pen)
                bounds[letter] = pen.bounds
        union = (min(b[0] for b in bounds.values()), min(b[1] for b in bounds.values()),
                 max(b[2] for b in bounds.values()), max(b[3] for b in bounds.values()))
        build = subprocess.run([dotnet, 'build', str(root / 'tools/header-reader-probe/Probe.csproj'),
            '--configuration', 'Release', '--artifacts-path', str(owned / 'build'),
            '-p:WindowsAppSDKSelfContained=false', '-p:UseAppHost=false', '--verbosity', 'quiet'],
            capture_output=True, text=True, timeout=180)
        if build.returncode:
            raise RuntimeError(build.stdout[-5000:] + build.stderr[-2000:])
        candidates = list((owned / 'build/bin/Probe/release').glob('WindowsRasterNativeProbe.dll'))
        assert len(candidates) == 1
        designs = [(size, phase, color, digit, letter) for size in (9, 10)
                   for phase in (0, .125, .25, .375) for color in ((0, 0, 0), (.05, .1, .3))
                   for digit in (1, 2) for letter in ('I', 'l')]
        random.Random(32452843).shuffle(designs)
        summaries = []
        for scale in (2, 4):
            inputs, metadata = [], []
            for index, (size, phase, color, digit, letter) in enumerate(designs):
                pdf = owned / f'{scale}-{index}.pdf'
                c = canvas.Canvas(str(pdf), pagesize=(40, 26), invariant=1, initialFontName='IndependentJP')
                c.setFont('IndependentJP', size)
                c.setFillColorRGB(*color)
                c.drawString(3 + phase, 11 + phase, f'A{letter}_{digit}')
                c.save()
                with fitz.open(pdf) as document:
                    image = document[0].get_pixmap(matrix=fitz.Matrix(scale, scale), alpha=False)
                    original = image.samples
                    points = [(i // 3 % image.width, i // 3 // image.width)
                              for i in range(0, len(original), 3) if min(original[i:i+3]) < 255]
                    margin = 2 * scale
                    x1, x2 = max(0, min(x for x, _ in points) - margin), min(image.width, max(x for x, _ in points) + margin + 1)
                    y1, y2 = max(0, min(y for _, y in points) - margin), min(image.height, max(y for _, y in points) + margin + 1)
                    w, h = x2 - x1, y2 - y1
                    rgb = b''.join(original[(y*image.width+x1)*3:(y*image.width+x2)*3] for y in range(y1, y2))
                    bgra = bytearray(w*h*4)
                    for channel in range(3):
                        bgra[channel::4] = rgb[2-channel::3]
                    bgra[3::4] = bytes([255]) * (w*h)
                    inputs.append(dict(Index=index, Width=w, Height=h, Bgra=base64.b64encode(bgra).decode()))
                    factor = size / units
                    region = (math.floor((3+phase+(advance+union[0])*factor)*scale),
                              math.floor((26-(11+phase+union[3]*factor))*scale),
                              math.ceil((3+phase+(advance+union[2])*factor)*scale),
                              math.ceil((26-(11+phase+union[1]*factor))*scale))
                    assert 0 <= region[0] < region[2] <= image.width and 0 <= region[1] < region[3] <= image.height
                    original_region = b''.join(original[(y*image.width+region[0])*3:(y*image.width+region[2])*3]
                                               for y in range(region[1], region[3]))
                    metadata.append(dict(key=(size, phase, color, digit), letter=letter, region=region,
                        crop=(x1, y1, w, h), original_region=original_region, input_hash=sha(bgra)))
            pixel_file = owned / f'pixels-{scale}.json'
            pixel_file.write_text(json.dumps(inputs))
            child = subprocess.run([dotnet, str(candidates[0]), '--audit-inputs-v1', str(pixel_file)],
                capture_output=True, text=True, timeout=60)
            assert child.returncode == 0, child.stderr[:2000] + child.stderr[-1000:]
            actual = json.loads(child.stdout)
            assert actual['calls'] == 0 and actual['qualified'] is False and len(actual['outputs']) == 64
            pairs = {}
            for index, (row, meta) in enumerate(zip(actual['outputs'], metadata)):
                assert row['Index'] == index and row['sourceBgraSHA256'] == meta['input_hash']
                assert row['height'] == 48 and row['channelOrder'] == 'BGR' and row['paddingCompared'] is False
                pixels = base64.b64decode(row['validBgr'], validate=True)
                width = row['ValidWidth']
                assert len(pixels) == width*48*3 and row['InputWidth'] >= width
                x1, y1, w, h = meta['crop']
                left, top, right, bottom = meta['region']
                # The same half-pixel centre mapping used by the actual app.
                xs = [x for x in range(width) if left-.5 <= x1+min(w-1, max(0, (x+.5)*w/width-.5)) < right-.5]
                ys = [y for y in range(48) if top-.5 <= y1+min(h-1, max(0, (y+.5)*h/48-.5)) < bottom-.5]
                assert xs and ys and xs == list(range(xs[0], xs[-1]+1)) and ys == list(range(ys[0], ys[-1]+1))
                region_pixels = b''.join(pixels[(y*width+xs[0])*3:(y*width+xs[-1]+1)*3] for y in ys)
                pairs.setdefault(meta['key'], {})[meta['letter']] = dict(original=meta['original_region'],
                    normalized=region_pixels, shape=(len(xs), len(ys)), tensorSHA=row['tensorFloat32SHA256'])
            assert len(pairs) == 32 and all(set(p) == {'I', 'l'} for p in pairs.values())
            same_shape = [p for p in pairs.values() if p['I']['shape'] == p['l']['shape']]
            summaries.append(dict(scale=scale, sourcePairs=32,
                originalLetterRegionDistinct=sum(p['I']['original'] != p['l']['original'] for p in pairs.values()),
                normalizedSameShapePairs=len(same_shape), normalizedDifferentShapePairs=32-len(same_shape),
                normalizedSameShapeDistinct=sum(p['I']['normalized'] != p['l']['normalized'] for p in same_shape),
                normalizedSameShapeIdentical=sum(p['I']['normalized'] == p['l']['normalized'] for p in same_shape),
                fullTensorDistinct=sum(p['I']['tensorSHA'] != p['l']['tensorSHA'] for p in pairs.values())))
        return dict(recipe='actual-app-glyph-region-tensor-audit-v1', publicFontSHA256=font.FONT_SHA,
            evaluationRegion='union of pinned I/l glyph outline bounds after natural A advance; no suffix',
            resize='actual production OcrInputTransform.Recognition; natural crop and scale',
            OCRCalls=0, modelCalls=0, qualified=False, newIndependentAccuracyItems=0, outcomes=summaries)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', required=True)
    args = parser.parse_args()
    print('HEADER_TENSOR_AUDIT ' + json.dumps(run(args.dotnet), sort_keys=True))
