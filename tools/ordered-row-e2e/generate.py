#!/usr/bin/env python3
"""New fictional source PDFs. Literal oracle is assertion-only, never a parser input."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import sys

from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "independent-wide-timetable"))
import generate as public_font

CLASSES = ["1_1", "1_2", "1_3"] + [f"{y}_{c}" for y in range(2, 6) for c in ("CN", "ES", "IT")] + ["AI_1", "AI_2"]
DAYS = ["月", "火", "水", "木", "金"]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def draw(output, case, seed, variant):
    rng = random.Random(seed)
    classes = CLASSES[:]
    rng.shuffle(classes)
    order = list(range(1, 6))
    rng.shuffle(order)
    left, label_w, col_w, row_h = (24, 94, 118, 76) if seed == 84071 else (37, 103, 129, 82)
    width, height = left + label_w + 8 * col_w + 26, 90 + 17 * row_h + 34
    path = output / f"{case}.pdf"
    c = canvas.Canvas(str(path), pagesize=(width, height), invariant=1, pageCompression=1, initialFontName="IndependentJP")
    c.setTitle("独立架空位置時間割 " + case)
    c.setAuthor("Invented source generator; no school document input")
    c.setCreator("ordered-row-e2e-v1")
    slots = []
    def line(x1, y1, x2, y2):
        c.line(x1, height-y1, x2, height-y2)
    def text(value, x, baseline, size=9):
        c.setFont("IndependentJP", size)
        if not value or pdfmetrics.stringWidth(value, "IndependentJP", size) >= col_w-12:
            raise ValueError("Frozen invented body does not fit the independently drawn cell")
        c.drawString(x, height-baseline, value)
    for day in order:
        c.setLineWidth(.6)
        text("2027年度 前期", left, 24, 10)
        bx, right, bottom = left+label_w, left+label_w+8*col_w, 90+17*row_h
        line(bx, 36, right, 36); line(bx, 36, bx, 62); line(right, 36, right, 62)
        if not (variant == "missing-day" and day == 3):
            text(DAYS[day-1], bx+4*col_w-4, 53, 11)
        for y in [62, 90] + [90+n*row_h for n in range(1, 18)]:
            line(left, y, right, y)
        line(left, 62, left, bottom)
        for col in range(9):
            x = bx+col*col_w
            line(x, 62, x, bottom)
        for period in range(1, 9):
            text(str(period), bx+(period-1)*col_w+col_w/2, 80, 10)
        for row, cls in enumerate(classes):
            top = 90+row*row_h
            if not (variant == "missing-class" and cls == "AI_2"):
                text(cls, left+8, top+row_h/2+4, 10)
            for period in range(1, 9):
                # Original literals independent of any OCR output, with variable
                # lengths, literal spaces and lookalikes in each of three roles.
                serial = (day-1)*136 + CLASSES.index(cls)*8 + period + (0 if seed == 84071 else 1000)
                marker = ("lI1", "O0", "B2O3", "B203", "I1l", "0O")[serial % 6]
                space = " " if serial % 7 == 0 else ""
                prefix = "架空" if serial % 3 else "仮想"
                values = [f"{prefix}甲{space}{marker}{serial}", f"{prefix}乙{serial}{space}{marker}", f"仮室丙{marker}{space}{serial}"]
                if variant == "parallel" and day == 1 and row == 0 and period == 1:
                    values[0] = "架空甲・架空丁"
                slots.append({"className": cls, "weekday": day, "period": period,
                              "lessons": [{"subject": values[0], "teacher": values[1], "room": values[2]}]})
                for role, value in enumerate(values):
                    if variant == "missing-body" and day == 2 and row == 0 and period == 1 and role == 1:
                        continue
                    text(value, bx+(period-1)*col_w+6, top+16+role*(row_h-22)/3, 9 if seed == 84071 else 10)
        c.showPage()
    c.save()
    slots.sort(key=lambda s: (s["className"], s["weekday"], s["period"]))
    return {"case": case, "file": path.name, "sha256": sha(path), "bytes": path.stat().st_size,
            "expect": "exact" if variant == "positive" else "refuse",
            "oracle": {"schoolYear": 2027, "term": "前期", "slots": slots}, "seed": seed,
            "layout": {"pageOrder": order, "classOrder": classes, "width": width, "height": height}}


def generate(output):
    output.mkdir(parents=True, exist_ok=False)
    (output/".ordered-row-e2e-owned").write_text("v1\n", encoding="utf-8")
    public_font.install_font(output)
    cases = [draw(output, "development", 84071, "positive"), draw(output, "unseen-content", 613957, "positive")]
    cases += [draw(output, "negative-"+v, 84071, v) for v in ("missing-day", "missing-class", "missing-body", "parallel")]
    manifest = {"recipe": "ordered-row-e2e-v1", "provenance": "Only original invented source designs and pinned public font; no school data or runtime oracle input", "generatorSha256": sha(Path(__file__)), "fontSha256": public_font.FONT_SHA,
                "obligationsPerPositive": {"slots": 680, "bodyValues": 2040}, "cases": cases}
    (output/"manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    result = generate(args.output)
    print(json.dumps({"recipe": result["recipe"], "generatorSha256": result["generatorSha256"],
                      "cases": [{k: c[k] for k in ("case", "sha256", "bytes", "expect")} for c in result["cases"]]}, ensure_ascii=False))
