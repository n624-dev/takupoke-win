#!/usr/bin/env python3
"""Independent unlabelled natural-Japanese cohort; hard positives stay unchanged.

Only independently invented text and a pinned public font are used. This is a
new layout/content condition, not a replacement or relabelling of prior inputs.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import random

import fitz
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('public_font', ROOT / 'tools/independent-wide-timetable/generate.py')
public_font = importlib.util.module_from_spec(spec)
spec.loader.exec_module(public_font)
CLASSES = ['1_1', '1_2', '1_3'] + [f'{year}_{course}' for year in range(2, 6) for course in ('CN', 'ES', 'IT')] + ['AI_1', 'AI_2']
DAYS = ['月', '火', '水', '木', '金']


def sha(data):
    return hashlib.sha256(data).hexdigest()


def draw(out, name, seed, font_size, column_width, row_height):
    rng = random.Random(seed)
    classes = CLASSES[:]
    rng.shuffle(classes)
    days = list(range(1, 6))
    rng.shuffle(days)
    left, label_width = 24, 94
    width = left + label_width + 8 * column_width + 26
    height = 90 + 9 * row_height + 30
    path = out / (name + '.pdf')
    c = canvas.Canvas(str(path), pagesize=(width, height), invariant=1, pageCompression=1, initialFontName='IndependentJP')
    c.setTitle('完全独立架空日本語時間割 ' + name)
    c.setAuthor('Independent invented cohort; no school material')
    c.setCreator('readable-ordered-row-v1')
    slots = []
    for day in days:
        for group in (classes[:9], classes[9:]):
            bx, right, bottom = left + label_width, left + label_width + 8 * column_width, 90 + len(group) * row_height
            def line(x1, y1, x2, y2):
                c.line(x1, height-y1, x2, height-y2)
            def text(value, x, baseline, size=font_size):
                if pdfmetrics.stringWidth(value, 'IndependentJP', size) >= column_width-12:
                    raise ValueError('Invented text does not fit the independently drawn source cell')
                c.setFont('IndependentJP', size)
                c.drawString(x, height-baseline, value)
            c.setLineWidth(.6)
            # Separate year/term source atoms, without asking a recognizer to
            # synthesize a whitespace token. No old source/header is rewritten.
            text('2029年度', left, 24, 10)
            text('後期', left+100, 24, 10)
            line(bx, 36, right, 36);line(bx, 36, bx, 62);line(right, 36, right, 62)
            text(DAYS[day-1], bx+4*column_width-4, 53, 11)
            for y in [62, 90] + [90+n*row_height for n in range(1, len(group)+1)]:
                line(left, y, right, y)
            line(left, 62, left, bottom)
            for column in range(9):
                x = bx+column*column_width
                line(x, 62, x, bottom)
            for period in range(1, 9):
                text(str(period), bx+(period-1)*column_width+column_width/2, 80, 10)
            for row, cls in enumerate(group):
                top = 90+row*row_height
                text(cls, left+8, top+row_height/2+4, 10)
                for period in range(1, 9):
                    serial = (day-1)*136 + CLASSES.index(cls)*8 + period + (0 if seed == 982451 else 2000)
                    values = [f'架空科目{serial}', f'架空教員{serial}', f'架空室{serial}']
                    slots.append({'className': cls, 'weekday': day, 'period': period,
                                  'lessons': [dict(zip(('subject', 'teacher', 'room'), values))]})
                    for role, value in enumerate(values):
                        text(value, bx+(period-1)*column_width+6, top+15+role*(row_height-20)/3)
            c.showPage()
    c.save()
    slots.sort(key=lambda s: (s['className'], s['weekday'], s['period']))
    assert len(slots) == 680 and len({(s['className'], s['weekday'], s['period']) for s in slots}) == 680
    image_path = out / (name+'-image.pdf')
    page_reports = []
    with fitz.open(path) as source, fitz.open() as images:
        assert len(source) == 10
        for page in source:
            pixels = page.get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
            assert pixels.width <= 2048 and pixels.height <= 2048
            target = images.new_page(width=page.rect.width, height=page.rect.height)
            target.insert_image(target.rect, stream=pixels.tobytes('png'))
            page_reports.append({'width': pixels.width, 'height': pixels.height, 'rgbSha256': sha(pixels.samples)})
        images.save(image_path, garbage=4, deflate=True, no_new_id=True)
    with fitz.open(image_path) as check:
        assert len(check) == 10 and all(not page.get_text().strip() for page in check)
    return {'case': name+'-image', 'file': image_path.name, 'sha256': sha(image_path.read_bytes()),
            'bytes': image_path.stat().st_size, 'expect': 'exact', 'seed': seed,
            'sourcePdfSha256': sha(path.read_bytes()), 'embeddedPages': page_reports,
            'oracle': {'schoolYear': 2029, 'term': '後期', 'slots': slots},
            'layout': {'pageOrder': days, 'classOrder': classes, 'pages': 10,
                       'width': width, 'height': height, 'bodyFontPixels': font_size*2}}


def generate(output):
    output.mkdir(parents=True, exist_ok=False)
    (output/'.ordered-row-e2e-owned').write_text('v1\n')
    public_font.install_font(output)
    cases = [draw(output, 'readable-development', 982451, 9, 104, 62),
             draw(output, 'readable-held-out', 15485863, 10, 110, 66)]
    manifest = {'recipe': 'readable-ordered-row-v1',
                'provenance': 'Independent invented Japanese/body-number sources; prior lookalike positives unchanged',
                'generatorSha256': sha(Path(__file__).read_bytes()), 'fontSha256': public_font.FONT_SHA,
                'obligationsPerPositive': {'slots': 680, 'bodyValues': 2040}, 'cases': cases}
    (output/'raster-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = generate(args.output)
    print(json.dumps({'recipe': result['recipe'], 'cases': [{k: c[k] for k in ('case', 'sha256', 'bytes')} for c in result['cases']]}))
