#!/usr/bin/env python3
"""Freeze source-colour counterparts of the consumed complete row-proof corpus."""
import argparse
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('ordered_source_design', ROOT/'tools/ordered-row-e2e/generate.py')
ordered = importlib.util.module_from_spec(spec); spec.loader.exec_module(ordered)

# These source colours are fixed before native reading. No recognition, oracle
# or role chooses a colour, crop, position, label or class in the reader.
PLANS = [
    ('black', [(0, 0, 0)], 'positive', False, False),
    ('red', [(1, 0, 0)], 'positive', False, False),
    ('blue', [(0, 0, 1)], 'positive', False, False),
    ('gray', [(.55, .55, .55)], 'positive', False, False),
    ('mixed', [(.6, .1, .1), (.1, .2, .6), (.1, .55, .1)], 'positive', False, False),
    ('near-white', [(.96, .96, .96)], 'positive', False, False),
    ('white', [(1, 1, 1)], 'positive', False, False),
    ('transparent', [(0, 0, 1)], 'positive', True, False),
    ('covered', [(0, 0, 1)], 'positive', False, True),
    ('missing-class', [(0, 0, 1)], 'missing-class', False, False),
    ('missing-body', [(0, 0, 1)], 'missing-body', False, False),
]


def generate(output):
    output.mkdir(parents=True, exist_ok=False)
    (output/'.pdf-colour-owned').write_bytes(b'v1\n')
    ordered.public_font.install_font(output)
    original = ordered.canvas.Canvas; cases = []
    try:
        for name, colours, variant, transparent, cover in PLANS:
            class ColourCanvas(original):
                ordinal = 0

                def drawString(self, *args, **kwargs):
                    self.setFillColorRGB(*colours[self.ordinal % len(colours)])
                    if transparent: self.setFillAlpha(.5)
                    self.ordinal += 1
                    return super().drawString(*args, **kwargs)

                def line(self, *args, **kwargs):
                    self.setStrokeColorRGB(*colours[0] if name not in {'near-white', 'white'} else (0, 0, 0))
                    return super().line(*args, **kwargs)

                def showPage(self):
                    if cover:
                        self.setFillColorRGB(1, 1, 1)
                        self.rect(0, 0, self._pagesize[0], self._pagesize[1], fill=1, stroke=0)
                    return super().showPage()

            ordered.canvas.Canvas = ColourCanvas
            case = ordered.draw(output, name, 613957, variant)
            case['expect'] = 'exact' if name in {'black', 'red', 'blue', 'gray', 'mixed'} else 'refuse'
            case['colourProtocol'] = {'rgb': colours, 'transparent': transparent, 'covered': cover, 'sourceVariant': variant}
            cases.append(case)
    finally:
        ordered.canvas.Canvas = original
    manifest = {'recipe':'complete-source-colour-v1', 'generatorSHA':ordered.sha(Path(__file__)),
        'sourceGeneratorSHA':ordered.sha(ROOT/'tools/ordered-row-e2e/generate.py'),
        'fontSHA':ordered.public_font.FONT_SHA, 'cases':cases,
        'scope':'Consumed content, unchanged geometry; colour compatibility, not new recognition/model qualification',
        'obligationsPerPositive':{'pages':5, 'slots':680, 'bodyValues':2040}}
    (output/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return manifest


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output',required=True,type=Path);args=p.parse_args()
    m=generate(args.output)
    print(json.dumps({'recipe':m['recipe'],'cases':[{k:c[k] for k in ('case','sha256','expect')} for c in m['cases']]}))
