#!/usr/bin/env python3
"""Invented dense timetable. No school PDF, OCR output, or private font input.

PDFs/images/font intermediates belong only to the requested temporary output.
The expected document is assertion data; neither reader receives it as input.
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import re
import urllib.request

import fitz
from fontTools.ttLib import TTFont as Font
from fontTools.varLib.instancer import instantiateVariableFont
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas

SEED = 20320819
FONT_REV = "295d98a7a0c17c68f1341eaeea354e7960ea70d3"
FONT_SHA = "c2f3b4d463500a2ddcd3849cded1fceeb9fd6d1c32e6cbecd568453ba50fc68f"
LICENSE_SHA = "1c05c68c34f9708415aada51f17e1b0092d2cea709bf4a94cd38114f9e73d7d9"
FONT_URL = f"https://raw.githubusercontent.com/google/fonts/{FONT_REV}/ofl/notosansjp/NotoSansJP%5Bwght%5D.ttf"
W, H = 3052, 990
LEFT, GRADE_W, CLASS_W, COL_W, TOP, HEAD_H, ROW_H = 32, 46, 54, 72, 56, 24, 48
BODY_X = LEFT + GRADE_W + CLASS_W
BODY_Y = TOP + 2 * HEAD_H
DAY_NAMES = ["月", "火", "水", "木", "金"]
VARIANTS = ["unlabeled", "labeled-control", "missing-class", "parallel-mismatch", "unreadable-body"]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def canonical_classes(source_root, class_contract=None):
    """Fail on source contract changes instead of importing a private class list."""
    source = class_contract.read_text(encoding="utf-8") if class_contract else (source_root / "Takupoke/RecoveryValidator.swift").read_text(encoding="utf-8")
    line = next(line.strip() for line in source.splitlines() if "static let specialClasses =" in line)
    expected = 'static let specialClasses = ["1_1", "1_2", "1_3"] + (2...5).flatMap { year in ["CN", "ES", "IT"].map { "\\(year)_\\($0)" } } + ["AI_1", "AI_2"]'
    if line != expected:
        raise ValueError("Canonical class source changed; review this fixture contract")
    return ["1_1", "1_2", "1_3"] + [f"{year}_{course}" for year in range(2, 6) for course in ("CN", "ES", "IT")] + ["AI_1", "AI_2"], digest(line.encode())


def design(classes):
    rng = random.Random(SEED)
    cells = []
    serial = 0
    for row, class_name in enumerate(classes):
        for day in range(1, 6):
            period = 1
            while period <= 8:
                remaining = 9 - period
                span = min(remaining, rng.choice([1] * 7 + [2, 3]))
                blank = rng.random() < .16
                parallel = not blank and span >= 2 and rng.random() < .28
                lessons = []
                for _ in range(2 if parallel else (0 if blank else 1)):
                    serial += 1
                    lessons.append({"subject": f"架空科目{serial:03d}",
                                    "teacher": "" if rng.random() < .13 else f"架空教員{serial:03d}",
                                    "room": "" if rng.random() < .17 else f"架空室{serial:03d}"})
                cells.append({"className": class_name, "row": row, "weekday": day,
                              "firstPeriod": period, "periodCount": span, "lessons": lessons})
                period += span
    # Keep an explicit independently designed paired lesson fully readable in
    # both variants. This is generator design, not a source-to-role annotation.
    cell = next(cell for cell in cells if len(cell["lessons"]) == 2)
    for lesson in cell["lessons"]:
        suffix = re.search(r"\d+$", lesson["subject"])[0]
        lesson["teacher"], lesson["room"] = f"架空教員{suffix}", f"架空室{suffix}"
    return cells


def expected(classes, cells):
    slots = []
    for cell in cells:
        for period in range(cell["firstPeriod"], cell["firstPeriod"] + cell["periodCount"]):
            slots.append({"className": cell["className"], "weekday": cell["weekday"],
                          "period": period, "lessons": cell["lessons"]})
    slots.sort(key=lambda slot: (classes.index(slot["className"]), slot["weekday"], slot["period"]))
    keys = [(slot["className"], slot["weekday"], slot["period"]) for slot in slots]
    assert len(keys) == len(set(keys)) == 680
    return {"schemaVersion": 1, "scope": "independently invented 17-class complete timetable assertion only",
            "seed": SEED, "schoolYear": 2032, "term": "後期", "classes": classes,
            "weekdays": [1, 2, 3, 4, 5], "periods": list(range(1, 9)), "slots": slots}


def install_font(out, font_file=None):
    original = out / "public-font.ttf"
    data = Path(font_file).read_bytes() if font_file else urllib.request.urlopen(FONT_URL, timeout=60).read()
    if digest(data) != FONT_SHA:
        raise ValueError("Pinned public font mismatch")
    original.write_bytes(data)
    font = Font(original, recalcTimestamp=False)
    font = instantiateVariableFont(font, {"wght": 400}, inplace=True)
    font.recalcTimestamp = False
    fixed = out / "public-font-static.ttf"
    font.save(fixed, reorderTables=False)
    pdfmetrics.registerFont(TTFont("IndependentJP", str(fixed)))


def draw(out, variant, classes, cells, omit_unused_font=False):
    suffix = "-no-unused-font" if omit_unused_font else ""
    path = out / f"{variant}{suffix}.pdf"
    # Retain normal ReportLab output as the primary regression. The separate
    # diagnostic changes ONLY its unused initial Helvetica Tf selection.
    options = {"initialFontName": "IndependentJP"} if omit_unused_font else {}
    c = canvas.Canvas(str(path), pagesize=(W, H), invariant=1, pageCompression=1, **options)
    c.setTitle("完全独立架空時間割 " + variant)
    c.setAuthor("Independent fictional test generator")
    c.setCreator("independent-wide-timetable v1")
    def line(x1, y1, x2, y2):
        c.line(x1, H-y1, x2, H-y2)
    def text(value, x, y, size=7, centered=True):
        c.setFont("IndependentJP", size)
        if centered:
            c.drawCentredString(x, H-y, value)
        else:
            c.drawString(x, H-y, value)
    text("令和14年度 後期 完全独立架空時間割", LEFT, 29, 16, False)
    c.setLineWidth(.55)
    bottom = BODY_Y + len(classes) * ROW_H
    right = BODY_X + 40 * COL_W
    line(LEFT, TOP, right, TOP)
    line(LEFT, BODY_Y, right, BODY_Y)
    line(BODY_X, TOP+HEAD_H, right, TOP+HEAD_H)
    for x in [LEFT, LEFT+GRADE_W, BODY_X, right]:
        line(x, TOP, x, bottom)
    for day in range(5):
        x = BODY_X + day * 8 * COL_W
        line(x, TOP, x, BODY_Y)
        text(DAY_NAMES[day], x + 4*COL_W, TOP+16, 11)
    for column in range(40):
        x = BODY_X + column * COL_W
        line(x, TOP+HEAD_H, x, BODY_Y)
        text(str(column % 8+1), x+COL_W/2, TOP+HEAD_H+16, 10)
    visible_classes = classes[:-1] if variant == "missing-class" else classes
    for row, cls in enumerate(classes):
        y = BODY_Y + row * ROW_H
        line(LEFT+GRADE_W, y, right, y)
        if cls in visible_classes:
            text(cls.split("_")[1], LEFT+GRADE_W+CLASS_W/2, y+27, 10)
    # Grade cells have independent merged rails, standard public class semantics.
    start = 0
    while start < len(classes):
        grade = classes[start].split("_")[0]
        end = start + 1
        while end < len(classes) and classes[end].split("_")[0] == grade:
            end += 1
        line(LEFT, BODY_Y+start*ROW_H, LEFT+GRADE_W, BODY_Y+start*ROW_H)
        text(grade, LEFT+GRADE_W/2, BODY_Y+(start+end)*ROW_H/2+3, 12)
        start = end
    line(LEFT, bottom, right, bottom)
    paired_mutated, unreadable_mutated = False, False
    for cell in cells:
        row, day, first, span = cell["row"], cell["weekday"], cell["firstPeriod"], cell["periodCount"]
        x = BODY_X + ((day-1)*8+first-1)*COL_W
        y = BODY_Y + row*ROW_H
        line(x, y, x, y+ROW_H)
        line(x+span*COL_W, y, x+span*COL_W, y+ROW_H)
        if cell["className"] not in visible_classes or not cell["lessons"]:
            continue
        lessons = cell["lessons"]
        if variant == "unreadable-body" and not unreadable_mutated and len(lessons) == 1:
            # A visible opaque vector mark replacing the body is not EMPTY.
            # Its actual reader/recovery treatment is measured, never pre-certified.
            c.setFillColorRGB(0, 0, 0)
            c.rect(x+8, H-(y+29), 35, 13, fill=1, stroke=0)
            unreadable_mutated = True
            continue
        for role_index, role in enumerate(["subject", "teacher", "room"]):
            values = [lesson[role] for lesson in lessons]
            if variant == "parallel-mismatch" and len(lessons) == 2 and role == "teacher" and not paired_mutated:
                values = values[:1]
                paired_mutated = True
            value = "・".join(values)
            if variant == "labeled-control":
                value = ["科目：", "担当：", "教室："][role_index] + value
            if value:
                text(value, x+span*COL_W/2, y+12+role_index*13, 7)
    text("架空注記：この表は独立生成した試験専用資料です。実在の授業とは関係ありません。", LEFT, bottom+27, 9, False)
    c.showPage()
    c.save()
    # This health check is NOT the iOS reader, and cannot qualify extraction.
    with fitz.open(path) as pdf:
        extracted = pdf[0].get_text()
        if variant != "unreadable-body" and "架空科目" not in extracted:
            raise ValueError("Independent PDF text health failed")
    return path


def generate(source_root, output, font_file=None, images=False, class_contract=None):
    output.mkdir(parents=True, exist_ok=False)
    (output / ".independent-wide-timetable-owned").write_text("v1\n", encoding="utf-8", newline="\n")
    classes, class_source_sha = canonical_classes(source_root, class_contract)
    cells = design(classes)
    oracle = expected(classes, cells)
    (output / "expected.json").write_text(json.dumps(oracle, ensure_ascii=False, indent=2)+"\n", encoding="utf-8", newline="\n")
    install_font(output, font_file)
    artifacts = []
    for variant in VARIANTS:
        path = draw(output, variant, classes, cells)
        disposition = "complete680" if variant in VARIANTS[:2] else ("subset16ClassScopeNotInvalidByItself" if variant == "missing-class" else "diagnosticNegative")
        artifacts.append({"variant": variant, "file": path.name, "bytes": path.stat().st_size,
                          "sha256": digest(path.read_bytes()), "expectedDisposition": disposition})
        if images and variant in VARIANTS[:2]:
            with fitz.open(path) as doc:
                pix = doc[0].get_pixmap(matrix=fitz.Matrix(1, 1), alpha=False)
                image = output / f"{variant}.png"
                pix.save(image)
                # Lossless image-only PDF from our own new raster; no old pixels.
                raster = fitz.open()
                page = raster.new_page(width=W, height=H)
                page.insert_image(page.rect, filename=str(image))
                raster_path = output / f"{variant}-raster.pdf"
                raster.save(raster_path, garbage=4, deflate=True, no_new_id=True)
                raster.close()
                artifacts.append({"variant": variant+"-raster", "file": raster_path.name,
                                  "bytes": raster_path.stat().st_size, "sha256": digest(raster_path.read_bytes()),
                                  "expectedDisposition": "complete680-ocrUnmeasured"})
    for variant in VARIANTS[:2]:
        path = draw(output, variant, classes, cells, omit_unused_font=True)
        artifacts.append({"variant": variant+"-no-unused-font", "file": path.name,
                          "bytes": path.stat().st_size, "sha256": digest(path.read_bytes()),
                          "expectedDisposition": "complete680-diagnosticSetupDifferenceOnly"})
    manifest = {"schemaVersion": 1, "seed": SEED, "provenance": "new coordinates/content/merge decisions; no original or private input read",
                "generatorSha256": digest(Path(__file__).read_bytes()), "canonicalClassSourceLineSha256": class_source_sha,
                "font": {"url": FONT_URL, "revision": FONT_REV, "sha256": FONT_SHA, "licenseSha256": LICENSE_SHA},
                "expectedSha256": digest((output/"expected.json").read_bytes()), "plannedSlots": 680,
                "physicalCells": len(cells), "mergedCells": sum(cell["periodCount"] > 1 for cell in cells),
                "parallelCells": sum(len(cell["lessons"]) == 2 for cell in cells),
                "blankCells": sum(not cell["lessons"] for cell in cells), "artifacts": artifacts,
                "limits": ["Missing-class PDF is a different 16-row source; normal timetable need not reject it solely for subset scope.",
                           "The opaque unreadable mark must not be called a proved blank; actual reader/ink handling is evaluated.",
                           "Labeled-control success does not establish unlabeled-main success.",
                           "PyMuPDF text health is not iOS or Windows reader proof."]}
    (output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+"\n", encoding="utf-8", newline="\n")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--font-file", type=Path)
    parser.add_argument("--class-contract", type=Path, help="public source-line snapshot for cross-OS generation; exact source definition required")
    parser.add_argument("--images", action="store_true")
    args = parser.parse_args()
    result = generate(args.source_root, args.output, args.font_file, args.images, args.class_contract)
    print(json.dumps(result, ensure_ascii=False))
