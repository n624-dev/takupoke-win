#!/usr/bin/env python3
"""One official local PARSeq-tiny recipe on independently invented I/l/1 crops.

No lexicon, case conversion, answer constraints, tuning, or adoption. The
sequence probabilities are not calibrated against existing CTC confidence.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import random
import sys
import tempfile
import urllib.request

import fitz
from fontTools.ttLib import TTFont as Font
from fontTools.varLib.instancer import instantiateVariableFont
from PIL import Image
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas

REVISION = '1902db043c029a7e03a3818c616c06600af574be'
WEIGHT_URL = 'https://github.com/baudm/parseq/releases/download/v1.0.0/parseq_tiny-e7a21b54.pt'
WEIGHT_SIZE = 24136675
WEIGHT_SHA = 'e7a21b543c98e67414a584c93b1dbb71c26e9463ae4aaa391d54e833660a4711'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def download(url, limit):
    with urllib.request.urlopen(url, timeout=90) as response:
        data = response.read(limit + 1)
    assert len(data) <= limit, 'Public dependency exceeds fixed size limit'
    return data


def run():
    root = Path(__file__).resolve().parents[2]
    with tempfile.TemporaryDirectory(prefix='takupoke-parseq-fixed-owned-') as temporary:
        owned = Path(temporary)
        # Download public code/weights/fonts only, before local image inference.
        code_hashes = {}
        files = ('LICENSE', 'strhub/models/parseq/model.py', 'strhub/models/parseq/modules.py',
                 'strhub/models/utils.py', 'strhub/data/utils.py', 'configs/charset/94_full.yaml')
        for name in files:
            data = download(f'https://raw.githubusercontent.com/baudm/parseq/{REVISION}/{name}', 65536)
            target = owned / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            code_hashes[name] = sha(data)
        for name in ('strhub', 'strhub/models', 'strhub/models/parseq', 'strhub/data'):
            (owned / name / '__init__.py').touch()
        weights = download(WEIGHT_URL, WEIGHT_SIZE)
        assert len(weights) == WEIGHT_SIZE and sha(weights) == WEIGHT_SHA, 'Official weight checksum differs'
        model_file = owned / 'parseq-tiny.pt'
        model_file.write_bytes(weights)
        import torch
        import yaml
        from torchvision import transforms as T
        sys.path.insert(0, str(owned))
        from strhub.data.utils import Tokenizer
        from strhub.models.parseq.model import PARSeq
        charset = yaml.safe_load((owned / 'configs/charset/94_full.yaml').read_text())['model']['charset_train']
        assert len(charset) == 94 and all(c in charset for c in 'AI_l1')
        tokenizer = Tokenizer(charset)
        torch.set_num_threads(2)
        torch.set_num_interop_threads(1)
        model = PARSeq(len(tokenizer), 25, (32, 128), (4, 8), 192, 3, 4, 12, 6, 4, 1, True, 1, .1)
        state = torch.load(model_file, map_location='cpu', weights_only=True)
        assert isinstance(state, dict) and state and all(isinstance(k, str) and isinstance(v, torch.Tensor) for k, v in state.items())
        # Official releases can contain the model's direct state, whereas the
        # Lightning system export uses a model. prefix. Both load strictly.
        if all(k.startswith('model.') for k in state):
            state = {k.removeprefix('model.'): v for k, v in state.items()}
        model.load_state_dict(state, strict=True)
        model.eval()
        transform = T.Compose([T.Resize((32, 128), T.InterpolationMode.BICUBIC), T.ToTensor(), T.Normalize(.5, .5)])
        font_spec = importlib.util.spec_from_file_location('parseq_public_font', root / 'tools/independent-wide-timetable/generate.py')
        font = importlib.util.module_from_spec(font_spec)
        font_spec.loader.exec_module(font)
        font.install_font(owned)
        public_base = 'https://raw.githubusercontent.com/google/fonts/295d98a7a0c17c68f1341eaeea354e7960ea70d3/ofl/sourcesans3/'
        source_sans = download(public_base+'SourceSans3%5Bwght%5D.ttf', 652632)
        assert len(source_sans) == 652632 and sha(source_sans) == '8b95ef0061a8eb29ec83589c30e9c4cea279590782ac58963ab5edfca9a51493'
        license_bytes = download(public_base+'OFL.txt', 4579)
        assert len(license_bytes) == 4579 and sha(license_bytes) == '09746787287a289323b0ec3cff4d1a4a801331b82b7207c1e186f5d26619a392'
        (owned/'control.ttf').write_bytes(source_sans)
        with Font(owned/'control.ttf', recalcTimestamp=False) as face:
            face = instantiateVariableFont(face, {'wght': 400}, inplace=True)
            face.recalcTimestamp = False
            face.save(owned/'control-static.ttf', reorderTables=False)
        pdfmetrics.registerFont(TTFont('IndependentI1Control', str(owned/'control-static.ttf')))
        batches, inventory = [], []
        for control in (False, True):
            designs = [(size, phase, color_name, color, digit, letter) for size in (9, 10)
                for phase in ((0, .25) if control else (0, .125, .25, .375))
                for color_name, color in (('black', (0, 0, 0)), ('navy', (.05, .1, .3)))
                for digit in (1, 2) for letter in (('I', '1') if control else ('I', 'l'))]
            random.Random(982451653 if control else 32452843).shuffle(designs)
            for ordinal, (size, phase, color_name, color, digit, letter) in enumerate(designs):
                path = owned / 'synthetic-source.pdf'
                font_name = 'IndependentI1Control' if control else 'IndependentJP'
                c = canvas.Canvas(str(path), pagesize=(40, 26), invariant=1, initialFontName=font_name)
                c.setFont(font_name, size); c.setFillColorRGB(*color)
                literal = f'A{letter}_{digit}'
                c.drawString(3+phase, 11+phase, literal); c.save()
                with fitz.open(path) as document:
                    raster = document[0].get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
                    rgb = raster.samples
                    pixels = [(i//3 % raster.width, i//3//raster.width) for i in range(0, len(rgb), 3) if min(rgb[i:i+3]) < 255]
                    x1, x2 = max(0, min(x for x, _ in pixels)-4), min(raster.width, max(x for x, _ in pixels)+5)
                    y1, y2 = max(0, min(y for _, y in pixels)-4), min(raster.height, max(y for _, y in pixels)+5)
                    crop = b''.join(rgb[(y*raster.width+x1)*3:(y*raster.width+x2)*3] for y in range(y1, y2))
                    batches.append(transform(Image.frombytes('RGB', (x2-x1, y2-y1), crop)).unsqueeze(0))
                    inventory.append(dict(controlI1=control, ordinal=ordinal, fontSHA=sha(source_sans) if control else font.FONT_SHA,
                        letter=letter, color=color_name, sourceSize=size, phase=phase, cropPixels=[x1, y1, x2-x1, y2-y1],
                        sourceRGBAHash=sha(Image.frombytes('RGB', (x2-x1, y2-y1), crop).convert('RGBA').tobytes()),
                        inputTensorHash=sha(batches[-1].numpy().tobytes()), expectedAfterRecognition=literal))
        assert len(batches) == len(inventory) == 96
        outputs = []
        # No network library is used below. Gold never enters the model or tokenizer.
        with torch.inference_mode():
            for image in batches:
                distributions = model(tokenizer, image).softmax(-1)
                labels, confidences = tokenizer.decode(distributions)
                raw_tokens, raw_scores = tokenizer.decode(distributions, raw=True)
                outputs.append(dict(observed=labels[0], ownSequenceProbabilities=confidences[0].tolist(),
                    rawTokenIds=distributions.argmax(-1)[0].tolist(), rawTokens=raw_tokens[0], rawProbabilities=raw_scores[0].tolist()))
        assert len(outputs) == 96
        checkpoint = json.dumps(outputs, sort_keys=True, ensure_ascii=False).encode()
        (owned/'unjudged-output.json').write_bytes(checkpoint)
        records = [dict(**source, **output, exact=source['expectedAfterRecognition'] == output['observed']) for source, output in zip(inventory, outputs)]
        summary = []
        for control in (False, True):
            for color in ('black', 'navy'):
                for letter in (('I', '1') if control else ('I', 'l')):
                    selected = [r for r in records if r['controlI1'] == control and r['color'] == color and r['letter'] == letter]
                    summary.append(dict(controlI1=control, color=color, letter=letter, items=len(selected), exact=sum(r['exact'] for r in selected),
                        falselyCanonicalAI=sum(letter != 'I' and r['observed'] == f"AI_{r['expectedAfterRecognition'][-1]}" for r in selected)))
        report = dict(model='official PARSeq-tiny', weightSize=WEIGHT_SIZE, weightSHA256=sha(weights), sourceRevision=REVISION,
            sourceHashes=code_hashes, ownOutputSHA256=sha(checkpoint), runtime=torch.__version__,
            inputRecipe='original RGB/fixed4px margin/bicubic32x128/normalize.5.5', threads=2, calls=96,
            charsetContainsMixedCaseAndUnderscore=True, caseConversion=False, answerConstraints=False,
            probabilitiesCalibratedAgainstCTC=False, qualified=False, adoptionCalls=0, newIndependentItems=0, summary=summary, records=records)
        if destination := os.environ.get('TAKUPOKE_PARSEQ_RESEARCH_REPORT'):
            with Path(destination).open('x') as file:
                json.dump(report, file, ensure_ascii=False, sort_keys=True)
        print(json.dumps({key: value for key, value in report.items() if key != 'records'}, ensure_ascii=False, sort_keys=True))
        return report


if __name__ == '__main__':
    argparse.ArgumentParser(description=__doc__).parse_args()
    run()
