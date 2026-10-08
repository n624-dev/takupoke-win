#!/usr/bin/env python3
"""Fixed paired BGR/grayscale recognition, independent invented pixels only.

Both complete reader arms finish before expected literals are evaluated. All
scores are retained in the numeric report; no production/adoption changes.
"""
import argparse
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import random
import subprocess
import tempfile
import urllib.request

import fitz
from fontTools.ttLib import TTFont as Font
from fontTools.varLib.instancer import instantiateVariableFont
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas

MODEL_URL = 'https://huggingface.co/PaddlePaddle/en_PP-OCRv5_mobile_rec_onnx/resolve/3fafbc3b5dcf93dd72add9f48368be8a3a2cd33b/inference.onnx'
MODEL_SHA = 'b5f833dfc5d0eb71da397b4efa06ebeee9b431b690a47d6af40d77d8eabc557f'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def run(dotnet, control_i1=False, model_file=None, dll_file=None):
    root = Path(__file__).resolve().parents[2]
    with tempfile.TemporaryDirectory(prefix='takupoke-il-gray-comparison-') as directory:
        owned = Path(directory)
        model = owned / 'english-v5.onnx'
        data = model_file.read_bytes() if model_file else urllib.request.urlopen(MODEL_URL, timeout=180).read(7848424)
        assert len(data) == 7848423 and sha(data) == MODEL_SHA
        model.write_bytes(data)
        spec = importlib.util.spec_from_file_location('gray_public_font', root / 'tools/independent-wide-timetable/generate.py')
        font = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(font)
        font_name, font_sha = 'IndependentJP', font.FONT_SHA
        if control_i1:
            # Freeze unused SourceSans3 font and license before reader calls.
            public_base = 'https://raw.githubusercontent.com/google/fonts/295d98a7a0c17c68f1341eaeea354e7960ea70d3/ofl/sourcesans3/'
            data = urllib.request.urlopen(public_base+'SourceSans3%5Bwght%5D.ttf', timeout=60).read(652633)
            font_sha = '8b95ef0061a8eb29ec83589c30e9c4cea279590782ac58963ab5edfca9a51493'
            assert len(data) == 652632 and sha(data) == font_sha
            license_bytes = urllib.request.urlopen(public_base+'OFL.txt', timeout=60).read(4580)
            assert len(license_bytes) == 4579 and sha(license_bytes) == '09746787287a289323b0ec3cff4d1a4a801331b82b7207c1e186f5d26619a392'
            original = owned / 'public-control-font.ttf'
            original.write_bytes(data)
            with Font(original, recalcTimestamp=False) as face:
                face = instantiateVariableFont(face, {'wght': 400}, inplace=True)
                face.recalcTimestamp = False
                fixed = owned / 'public-control-font-static.ttf'
                face.save(fixed, reorderTables=False)
            font_name = 'IndependentI1Control'
            pdfmetrics.registerFont(TTFont(font_name, str(fixed)))
        else:
            font.install_font(owned)
        if dll_file is None:
            build = subprocess.run([dotnet, 'build', str(root / 'tools/header-reader-probe/Probe.csproj'),
                '--configuration', 'Release', '--artifacts-path', str(owned / 'build'),
                '-p:WindowsAppSDKSelfContained=false', '-p:UseAppHost=false', '--verbosity', 'quiet'],
                capture_output=True, text=True, timeout=180)
            assert build.returncode == 0, build.stdout[-5000:] + build.stderr[-2000:]
        dll = dll_file or owned / 'build/bin/Probe/release/WindowsRasterNativeProbe.dll'
        assert dll.is_file()
        designs = [(size, phase, color, digit, letter) for size in (9, 10)
                   for phase in ((0, .25) if control_i1 else (0, .125, .25, .375)) for color in ((0, 0, 0), (.05, .1, .3))
                   for digit in (1, 2) for letter in (('I', '1') if control_i1 else ('I', 'l'))]
        random.Random(982451653 if control_i1 else 32452843).shuffle(designs)
        count = len(designs)
        inputs, inventory = [], []
        for index, (size, phase, color, digit, letter) in enumerate(designs):
            pdf = owned / f'{index}.pdf'
            c = canvas.Canvas(str(pdf), pagesize=(40, 26), invariant=1, initialFontName=font_name)
            c.setFont(font_name, size)
            c.setFillColorRGB(*color)
            c.drawString(3+phase, 11+phase, f'A{letter}_{digit}')
            c.save()
            with fitz.open(pdf) as document:
                image = document[0].get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
                original = image.samples
                points = [(i//3 % image.width, i//3//image.width) for i in range(0, len(original), 3)
                          if min(original[i:i+3]) < 255]
                x1, x2 = max(0, min(x for x, _ in points)-4), min(image.width, max(x for x, _ in points)+5)
                y1, y2 = max(0, min(y for _, y in points)-4), min(image.height, max(y for _, y in points)+5)
                w, h = x2-x1, y2-y1
                rgb = b''.join(original[(y*image.width+x1)*3:(y*image.width+x2)*3] for y in range(y1, y2))
                bgra = bytearray(w*h*4)
                for channel in range(3):
                    bgra[channel::4] = rgb[2-channel::3]
                bgra[3::4] = bytes([255])*(w*h)
                inputs.append(dict(Index=index, Width=w, Height=h, Bgra=base64.b64encode(bgra).decode()))
                inventory.append(dict(index=index, expected=f'A{letter}_{digit}', letter=letter, size=size,
                    phase=phase, color='black' if color == (0, 0, 0) else 'blue', sourceSHA256=sha(bgra)))
        pixel_file = owned / 'pixels-only.json'
        pixel_file.write_text(json.dumps(inputs))
        responses = {}
        modes = [('BGR', 'blind-i1-control-bgr-v1'), ('gray', 'blind-i1-control-grayscale-v1')] if control_i1 else [('BGR', 'blind-il-bgr-v1'), ('gray', 'blind-il-grayscale-v1')]
        for arm, mode in modes:
            child = subprocess.run([dotnet, str(dll), str(model), str(pixel_file),
                str(root / 'tools/header-reader-probe/english-v5-alphabet.json'), mode],
                capture_output=True, text=True, timeout=120)
            assert child.returncode == 0, child.stderr[:2000] + child.stderr[-1000:]
            # Checkpoint the unscored complete arm in owned scratch.
            (owned / f'{arm}-raw.json').write_text(child.stdout)
            responses[arm] = json.loads(child.stdout)
        # Gold evaluation starts here, after both complete planned arms.
        rows = []
        for arm, response in responses.items():
            assert response['calls'] == count and response['qualified'] is False and len(response['outputs']) == count
            for expected, actual in zip(inventory, response['outputs']):
                assert actual['Index'] == expected['index'] and actual['sourceBgraSHA256'] == expected['sourceSHA256']
                assert actual['neutralPixelsChanged'] == 0
                assert actual['minimumConfidence'] is None or 0 <= actual['minimumConfidence'] <= 1
                rows.append(dict(**expected, arm=arm, exact=actual['text'] == expected['expected'], output=actual))
        paired = list(zip(rows[:count], rows[count:]))
        for baseline, gray in paired:
            assert baseline['index'] == gray['index']
            a, b = baseline['output'], gray['output']
            assert (a['ValidWidth'], a['InputWidth'], a['timeCount']) == (b['ValidWidth'], b['InputWidth'], b['timeCount'])
            if baseline['color'] == 'black':
                assert a['tensorFloat32SHA256'] == b['tensorFloat32SHA256'] and a['text'] == b['text']
        summaries = []
        for arm in responses:
            for letter in (('I', '1') if control_i1 else ('I', 'l')):
                for color in ('black', 'blue'):
                    selected = [r for r in rows if r['arm'] == arm and r['letter'] == letter and r['color'] == color]
                    assert len(selected) == count//4
                    summaries.append(dict(arm=arm, letter=letter, color=color, obligations=count//4,
                        literalExact=sum(r['exact'] for r in selected),
                        incorrectAtOrAbove08=sum(not r['exact'] and r['output']['minimumConfidence'] is not None and r['output']['minimumConfidence'] >= .8 for r in selected),
                        correctBelow08=sum(r['exact'] and (r['output']['minimumConfidence'] is None or r['output']['minimumConfidence'] < .8) for r in selected)))
        return dict(recipe='fixed-2x-source-grayscale-I1-control-v1' if control_i1 else 'fixed-2x-source-grayscale-paired-v1', modelBytes=7848423, modelSHA256=MODEL_SHA,
            fontSHA256=font_sha, calls=count*2, newIndependentAccuracyItems=count if control_i1 else 0, qualified=False, adoptionCalls=0,
            grayscale='before-resize .114B+.587G+.299R; floor(value+.5) uint8; replicated three channels',
            summaries=summaries, gains=sum(not a['exact'] and b['exact'] for a, b in paired),
            regressions=sum(a['exact'] and not b['exact'] for a, b in paired),
            sameWrongLiteral=sum(not a['exact'] and not b['exact'] and a['output']['text'] == b['output']['text'] for a, b in paired),
            grayPrintedEllAsI=sum(r['arm'] == 'gray' and r['letter'] == 'l' and r['output']['text'] in ('AI_1', 'AI_2') for r in rows),
            grayPrintedOneAsI=sum(r['arm'] == 'gray' and r['letter'] == '1' and r['output']['text'] in ('AI_1', 'AI_2') for r in rows),
            grayNonwhitePixelsLost=sum(r['output']['nonwhitePixelsLost'] for r in rows if r['arm'] == 'gray'),
            unsupportedPadding=sum(not r['output']['paddingSupported'] for r in rows),
            outputsSHA256=sha(json.dumps(rows, sort_keys=True).encode()), outputs=rows)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--control-i1', action='store_true')
    parser.add_argument('--model-file', type=Path)
    parser.add_argument('--dll-file', type=Path)
    args = parser.parse_args()
    print('IL_GRAYSCALE_PAIRED ' + json.dumps(run(args.dotnet, args.control_i1, args.model_file, args.dll_file), sort_keys=True))
