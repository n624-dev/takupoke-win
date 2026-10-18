#!/usr/bin/env python3
"""New invented full 17-class development drawings; separated model input/oracle."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import shutil

from PIL import Image, ImageDraw, ImageFont
from fontTools.ttLib import TTFont

from run import asset, sha, write

FONTS = [
    ('shipporimincho', 'ShipporiMincho-Regular.ttf', 8677284,
     '769b5269f0f9bc6534b352c0e6bd856a566e03ff788f107191c2d835863570b2'),
    ('mplusrounded1c', 'MPLUSRounded1c-Regular.ttf', 3389792,
     'b75708b53e45b06d17d470aeeca5b766e3d1b3999f03f13ec4eb863ca846c14c')]
CLASSES = ['1_1', '1_2', '1_3'] + [f'{y}_{c}' for y in range(2, 6) for c in ('CN', 'ES', 'IT')] + ['AI_1', 'AI_2']
DAYS = ['月', '火', '水', '木', '金']


def draw(out, font_path, ordinal, variant):
    rng = random.Random(910701 + ordinal)
    classes = CLASSES[:]; rng.shuffle(classes)
    days = list(range(1, 6)); rng.shuffle(days)
    label, column, top, row = 113, 240, 98, 78
    width, height = label + 8 * column + 1, top + 17 * row + 1
    font = ImageFont.truetype(str(font_path), 16)
    header = ImageFont.truetype(str(font_path), 20)
    pages = []; slots = []; lessons = []
    for page, day in enumerate(days, 1):
        image = Image.new('RGB', (width, height), 'white'); pen = ImageDraw.Draw(image)
        def text(value, x, y, face=font):
            if pen.textbbox((x, y), value, font=face)[2] > width: raise ValueError('Drawing exceeds source')
            pen.text((x, y), value, font=face, fill='black')
        text('2029年度 前期', 6, 1, header)
        pen.rectangle((label, 32, width-1, 62), outline='black', width=1)
        if not (variant == 'missing-day' and page == 1):
            text(DAYS[day-1], label + 4*column, 33, header)
        for y in [62, top] + [top + n*row for n in range(1, 18)]:
            pen.line((0, y, width-1, y), fill='black', width=1)
        pen.line((0, 62, 0, height-1), fill='black', width=1)
        for n in range(9): pen.line((label+n*column, 62, label+n*column, height-1), fill='black', width=1)
        for period in range(1, 9): text(str(period), label+(period-1)*column+column//2, 65, header)
        for index, cls in enumerate(classes):
            printed = 'Al_1' if variant == 'noncanonical-class' and cls == 'AI_1' else cls
            text(printed, 8, top+index*row+28, header)
            for period in range(1, 9):
                serial = rng.randrange(100, 1000)
                values = [f'架空化学丁{serial}', f'仮想担当丁{serial}', f'架空室{rng.choice(["O0", "0O", "I1l", "B2"])}{serial}']
                position = (cls, day, period)
                slots.append(dict(zip(('className', 'day', 'period'), (cls, str(day), period))))
                lesson = {'className': cls, 'weekday': day, 'period': period,
                          'names': dict(zip(('subject', 'teacher', 'room'), values))}
                lessons.append(lesson)
                for role, (prefix, value) in enumerate(zip(('科目：', '担当：', '教室：'), values)):
                    literal = prefix + value
                    if pen.textlength(literal, font=font) > column-24:
                        raise ValueError('Whole-line source would be clipped')
                    text(literal, label+(period-1)*column+7+role*3, top+index*row+3+role*24)
        rgba = image.convert('RGBA').tobytes('raw', 'BGRA')
        path = out / f'd{ordinal}-{variant}-p{page}.bgra'; path.write_bytes(rgba)
        pages.append({'page': page, 'width': width, 'height': height, 'pixels': asset(path, out)})
    id = f'd{ordinal}-{variant}'
    identity = hashlib.sha256(json.dumps(pages, sort_keys=True).encode()).hexdigest()
    return ({'id': id, 'originalPDFSHA256': identity, 'pages': pages},
            {'id': id, 'expect': 'exact' if variant == 'positive' else 'refuse', 'variant': variant,
             'font': font_path.name, 'schoolYear': 2029, 'allOtherSlotsBlank': True,
             'requiredSlotSet': slots, 'timetable': {'term': '前期', 'lessons': lessons}})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--fonts', type=Path, required=True)
    parser.add_argument('--models', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    fonts = []
    for family, file, size, digest in FONTS:
        path = args.fonts / (family + '-' + file)
        if path.stat().st_size != size or sha(path) != digest:
            raise ValueError('Frozen fresh development font changed: ' + family)
        with TTFont(path) as face:
            licenses = {n.toUnicode() for n in face['name'].names if n.nameID == 13}
            if not any('SIL Open Font License' in value for value in licenses):
                raise ValueError('Font license evidence is missing')
        fonts.append(path)
    for name, digest in [('rec.onnx', 'da72dc72ca4dc220df0dfde68c1dedc31c58d3e76a25871122e5056227d50092'),
                         ('det.onnx', 'a431985659dc921974177a95adcfbb90fd9e51989a5e04d70d0b75f597b6e61d'),
                         ('alphabet.json', 'd858428fbe0ada92b439a5f277b51bc1b318fd985d80946a3ce859d7f2446a92')]:
        source = args.models / name
        if sha(source) != digest: raise ValueError('Pinned production asset changed')
        shutil.copyfile(source, args.output / name)
    write(args.output / 'pre-generation-freeze.json', {'recipe': 'physical-input-framing-fresh-development-v1',
        'generatorSHA256': sha(Path(__file__)), 'fonts': FONTS,
        'variants': ['positive', 'missing-day', 'noncanonical-class'], 'conditions': ['production', 'physical-input-frame'],
        'seedBase': 910701, 'fullObligationsPerDocument': {'pages': 5, 'classes': 17, 'slots': 680, 'bodyValues': 2040},
        'pixelPolicy': 'All original text retained, no model feedback or CTC box clipping',
        'scope': 'New content/fonts development experiment, not independent final or deployment qualification'})
    documents = []; oracle = []
    for ordinal, font in enumerate(fonts):
        for variant in ('positive', 'missing-day', 'noncanonical-class'):
            document, gold = draw(args.output, font, ordinal, variant)
            documents.append(document); oracle.append(gold)
    manifest = {'recipe': 'physical-input-framing-fresh-development-v1', 'documents': documents,
        'detector': asset(args.output/'det.onnx', args.output), 'recognizer': asset(args.output/'rec.onnx', args.output),
        'dictionary': asset(args.output/'alphabet.json', args.output),
        'sourceScope': 'Fresh invented complete 17-class development drawings; original pixels, no PDF-reader/native-render/final qualification'}
    write(args.output/'inputs.json', manifest)
    write(args.output/'oracle.json', oracle)
    print(json.dumps({'newDevelopmentDocuments': len(documents), 'modelCalls': 0}))


if __name__ == '__main__': main()
