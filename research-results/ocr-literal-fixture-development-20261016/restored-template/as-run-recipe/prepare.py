"""Prospectively registered fictional cohort; no inference and no private data."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import re
import string
from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
write = lambda p, v: p.write_text(json.dumps(v, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
asset = lambda p: dict(file=p.name, bytes=p.stat().st_size, sha256=sha(p))
digest = lambda s: hashlib.sha256(s.encode()).hexdigest()


def excluded_literals():
    config = json.loads(Path(__file__).with_name('exclusions.json').read_text())
    basepath = ROOT / config['baseFile']
    assert basepath.stat().st_size == config['baseBytes'] and sha(basepath) == config['baseSHA256']
    base = json.loads(basepath.read_text())
    original = ROOT / base['base']['file']
    assert original.stat().st_size == base['base']['bytes'] and sha(original) == base['base']['sha256']
    known = set(json.loads(original.read_text())['literalSha256'])
    for part in base['parts']:
        path = ROOT / part['file']
        assert path.stat().st_size == part['bytes'] and sha(path) == part['sha256']
        known.update(json.loads(path.read_text()))
    assert len(known) == config['priorExcludedCount']
    extra = ROOT / config['extraFile']
    assert extra.stat().st_size == config['extraBytes'] and sha(extra) == config['extraSHA256']
    known.update(json.loads(extra.read_text()))
    assert len(known) == config['totalExcludedCount']
    return known


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--fonts', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    out = a.output.resolve()
    protocol = json.loads(Path(__file__).with_name('protocol.json').read_text())
    excluded = excluded_literals()
    assert len(excluded) == protocol['excludeLiteralCount']
    fonts = [x for x in protocol['fontPins'] if x['file'].endswith('.ttf')]
    for x in fonts:
        path = a.fonts / x['file']
        assert path.stat().st_size == x['bytes'] and sha(path) == x['sha256']
    out.mkdir(parents=True, exist_ok=False)
    seen, sampled = set(), []
    def unique(factory):
        for _ in range(10000):
            value = factory()
            parts = [value, *re.findall('[A-Za-z0-9_-]+', value)]
            # A nested number/code is reserved before constructing its full
            # Japanese field. Reusing that same component inside its own field
            # is intentional; prior-corpus components remain excluded.
            if digest(value) not in seen and not any(digest(v) in excluded for v in parts):
                seen.update(digest(v) for v in parts)
                sampled.extend(parts)
                return value
        raise ValueError('Literal generation could not avoid frozen exclusions')
    documents, oracle, font_coverage = [], [], []
    for index, spec in enumerate(fonts):
        font = a.fonts / spec['file']
        cmap = TTFont(font).getBestCmap()
        rare_names = ['渡邊', '渡邉', '高﨑', '髙崎', '鄭', '劉', '賈', '祁']
        supported_rare_names = [name for name in rare_names if all(ord(c) in cmap for c in name)]
        assert supported_rare_names, 'Font must support uncommon Japanese names'
        font_coverage.append(dict(font=spec['file'], supportedRareNames=supported_rare_names,
                                  unsupportedRareNames=[name for name in rare_names if name not in supported_rare_names]))
        face = ImageFont.truetype(str(font), protocol['facesize'])
        header = ImageFont.truetype(str(font), protocol['headersize'])
        rng = random.Random(protocol['seedBase'] + index)
        alphabetic = lambda n=None: ''.join(rng.choice(string.ascii_letters) for _ in range(n or rng.randrange(2, 9)))
        number = lambda: unique(lambda: str(rng.randrange(1000, 10000)))
        def mixed():
            while True:
                value = ''.join(rng.choice(string.ascii_letters + string.digits + '_-') for _ in range(rng.randrange(2, 8)))
                if any(c.isdigit() for c in value) and any(c.isalpha() for c in value):
                    return value + (rng.choice('_-') if rng.randrange(8) == 0 else '')
        lessons = []
        for day in range(1, 6):
            for cls in protocol['classes']:
                for period in range(1, 9):
                    pick = rng.random()
                    if pick < .2:
                        subject = unique(lambda: alphabetic(7))
                        subject_tag = 'english-subject'
                    else:
                        subject = unique(lambda: '架空' + rng.choice(['情報', '音楽', '美術', '地理', '家庭', '体育']) + number())
                        subject_tag = 'japanese-subject'
                    pick = rng.random()
                    if pick < .1:
                        teacher = unique(lambda: 'Dr' + alphabetic(6))
                        teacher_tag = 'english-name'
                    else:
                        rare = pick < .3
                        names = supported_rare_names if rare else ['伊藤', '渡辺', '山本', '中村', '小林', '加藤', '佐々木']
                        teacher = unique(lambda: '仮想' + rng.choice(names) + number())
                        teacher_tag = 'rare-japanese-name' if rare else 'japanese-name'
                    pick = rng.random()
                    if pick < .4:
                        code = unique(mixed)
                        room = ('架空室' if rng.random() < .5 else '') + code
                        room_tag = 'mixed-code'
                    elif pick < .65:
                        code = unique(alphabetic)
                        room = ('架空室' if rng.random() < .5 else '') + code
                        room_tag = 'alphabetic-name'
                    elif pick < .8:
                        room = unique(lambda: '仮設' + rng.choice(['実習室', '講義室', '研究室']) + number())
                        room_tag = 'japanese-description'
                    elif pick < .9:
                        room = number()
                        room_tag = 'numeric-only'
                    else:
                        room = unique(lambda: 'R' + number() + '×' + number())
                        room_tag = 'math-symbol'
                    assert digest(room) not in excluded
                    seen.add(digest(room))
                    sampled.append(room)
                    lessons.append(dict(className=cls, weekday=day, period=period,
                                        names=dict(subject=subject, teacher=teacher, room=room),
                                        tags=dict(subject=subject_tag, teacher=teacher_tag, room=room_tag)))
        for variant in protocol['variants']:
            id = f'literalfresh-{index}-{variant}'
            pages = []
            for day in range(1, 6):
                # Retain the previously exercised physical timetable template:
                # the weekday has one printed header spanning all eight periods.
                label, col, top, row = 100, 267, 108, 102
                # Both outer rules are inclusive coordinates. Reserve their
                # pixels instead of silently drawing them beyond the canvas.
                width, height = label + col * 8 + 1, top + row * 3 + 1
                assert label + col * 8 < width and top + row * 3 < height
                image = Image.new('RGB', (width, height), 'white')
                pen = ImageDraw.Draw(image)
                def text(value, x, y, font=face):
                    if any(ord(c) not in cmap for c in value):
                        raise ValueError('Font lacks a requested literal; no tofu/fallback substitution')
                    pen.text((x, y), value, font=font, fill='black')
                text('2034年度 前期', 8, 0, header)
                pen.rectangle((label, 36, width - 1, 70), outline='black', width=1)
                if not (variant == 'missing-day' and day == 1):
                    text(['月', '火', '水', '木', '金'][day - 1], label + 4 * col, 36, header)
                for y in [70, top] + [top + n * row for n in range(1, 4)]:
                    pen.line((0, y, width - 1, y), fill='black', width=1)
                pen.line((0, 70, 0, height - 1), fill='black', width=1)
                for n in range(9):
                    pen.line((label + n * col, 70, label + n * col, height - 1), fill='black', width=1)
                for period in range(1, 9):
                    text(str(period), label + (period - 1) * col + col // 2, 73, header)
                for idx, cls in enumerate(protocol['classes']):
                    if not (variant == 'missing-class' and idx == 2 and day == 1):
                        text(cls, 5, top + idx * row + 33, header)
                    for period in range(1, 9):
                        lesson = next(v for v in lessons if v['className'] == cls and v['weekday'] == day and v['period'] == period)
                        for role, (prefix, value) in enumerate(zip(['科目：', '担当：', '教室：'], lesson['names'].values())):
                            if pen.textlength(prefix + value, font=face) > col - 20:
                                raise ValueError('Printed body overflows its physical cell')
                            text(prefix + value, label + (period - 1) * col + 8, top + idx * row + 4 + role * 31)
                path = out / f'{id}-p{day}.bgra'
                path.write_bytes(image.convert('RGBA').tobytes('raw', 'BGRA'))
                pages.append(dict(page=day, width=width, height=height, pixels=asset(path)))
            original = hashlib.sha256(json.dumps(pages, sort_keys=True).encode()).hexdigest()
            documents.append(dict(id=id, originalPDFSHA256=original, pages=pages))
            oracle.append(dict(id=id, expect='exact' if variant == 'positive' else 'refuse', variant=variant,
                               schoolYear=2034, font=spec['file'], cohort='new-local-font-file', allOtherSlotsBlank=True,
                               requiredSlotSet=[dict(className=v['className'], day=str(v['weekday']), period=v['period']) for v in lessons],
                               timetable=dict(term='前期', lessons=lessons)))
    write(out / 'inputs.json', dict(documents=documents, recipe=protocol['recipe'], researchDeadlineSeconds=1500))
    write(out / 'oracle.json', oracle)
    write(out / 'generation-freeze.json', dict(recipe=protocol['recipe'], sourceSHA256=sha(Path(__file__)),
        protocolSHA256=sha(Path(__file__).with_name('protocol.json')), seed=protocol['seedBase'], exclusionsAvoided=True,
        excludedLiteralCount=len(excluded), sampledLiteralComponentCount=len(set(sampled)), modelCalls=0,
        fontPins=fonts, fontExposure=protocol['fontExposure'], fontCoverage=font_coverage,
        prospectiveCriteria=protocol['criteria']))
    print(json.dumps(dict(documents=len(documents), positiveDocuments=2, fieldsPerPositive=360, modelCalls=0)), flush=True)


if __name__ == '__main__':
    main()
