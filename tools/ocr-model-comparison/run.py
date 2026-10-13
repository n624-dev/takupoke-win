#!/usr/bin/env python3
"""Fixed local-CPU recognition comparison; no school or arbitrary image input."""
import argparse
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import random
import shutil
import subprocess
import sys
import urllib.request

from PIL import Image, ImageChops, ImageDraw, ImageFont
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
import yaml

ROOT = Path(__file__).resolve().parents[2]
RECIPE = "independent-ocr-model-color-v1"
FONT_REV = "295d98a7a0c17c68f1341eaeea354e7960ea70d3"
FONTS = {
    "sans": ("notosansjp/NotoSansJP%5Bwght%5D.ttf", "c2f3b4d463500a2ddcd3849cded1fceeb9fd6d1c32e6cbecd568453ba50fc68f"),
    "serif": ("notoserifjp/NotoSerifJP%5Bwght%5D.ttf", "4c6b4670b73d0843c7b2d30b9e2fbcfa596aef6fd3937f894929ab0b8d659d1e"),
}
MODELS = [
    {"id": "v5-mobile", "revision": "ed152b8b495f84de93cda5709d768548a9127622", "repo": "PP-OCRv5_mobile_rec_onnx", "bytes": 16534782,
     "sha256": "da72dc72ca4dc220df0dfde68c1dedc31c58d3e76a25871122e5056227d50092", "symbols": 18385},
    {"id": "v6-medium", "revision": "50c7eacafc52fa7bcf4194e8cd08e46f8558504b", "repo": "PP-OCRv6_medium_rec_onnx", "bytes": 76554979,
     "sha256": "9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba", "symbols": 18710},
]
COLORS = [(0, 0, 0), (12, 25, 76), (140, 20, 20), (12, 89, 12)]
HEADERS = ["1_1", "1_2", "1_3"] + [f"{y}_{c}" for y in range(2, 6) for c in ("CN", "ES", "IT")] + ["AI_1", "AI_2", "Al_1", "Al_2", "A1_1", "A1_2"]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def asset(path, root):
    data = path.read_bytes()
    return {"path": path.relative_to(root).as_posix(), "bytes": len(data), "sha256": sha(data)}


def download(url, path, digest, maximum, expected_bytes=None):
    # Every download precedes measurement; URLs contain only public artifact IDs.
    with urllib.request.urlopen(url, timeout=60) as response, path.open("xb") as output:
        count = 0
        while chunk := response.read(1024 * 1024):
            count += len(chunk)
            if count > maximum:
                raise ValueError("Public artifact exceeds fixed bound")
            output.write(chunk)
    data = path.read_bytes()
    if sha(data) != digest or expected_bytes is not None and len(data) != expected_bytes:
        raise ValueError("Pinned artifact mismatch: " + path.name)


def make_models(out):
    models = []
    for pinned in MODELS:
        path = out / (pinned["id"] + ".onnx")
        download(f'https://huggingface.co/PaddlePaddle/{pinned["repo"]}/resolve/{pinned["revision"]}/inference.onnx',
                 path, pinned["sha256"], pinned["bytes"], pinned["bytes"])
        dictionary = out / (pinned["id"] + "-dictionary.json")
        if pinned["id"] == "v5-mobile":
            data = (ROOT / "src/Takupoke.Win/Assets/Recovery/ocr-characters.json").read_bytes()
            if sha(data) != "d858428fbe0ada92b439a5f277b51bc1b318fd985d80946a3ce859d7f2446a92":
                raise ValueError("Production V5 dictionary mismatch")
        else:
            config = out / "v6-inference.yml"
            download(f'https://huggingface.co/PaddlePaddle/{pinned["repo"]}/resolve/{pinned["revision"]}/inference.yml',
                     config, "991b700facf5b50a7de193468207d5f4255b538dde0d312ae3b7c7a9b6873129", 200000)
            characters = yaml.safe_load(config.read_text(encoding="utf-8"))["PostProcess"]["character_dict"]
            data = (json.dumps([""] + characters + [" "], ensure_ascii=False, separators=(",", ":")) + "\n").encode()
            if sha(data) != "a49d51120786924a642c9e8a14b2a2ab73c019699abf5b2b6970ec7355c0415f":
                raise ValueError("Derived V6 dictionary mismatch")
        dictionary.write_bytes(data)
        if len(json.loads(data)) != pinned["symbols"]:
            raise ValueError("Pinned model/alphabet size mismatch")
        models.append({**pinned, "file": asset(path, out), "dictionary": asset(dictionary, out)})
    return models


def pixels(text, font, color):
    # Bounds come from every actual nonwhite source pixel, never OCR answers.
    # All 4 arms receive identical crop geometry; gray changes only luminance.
    image = Image.new("RGB", (360, 60), "white")
    ImageDraw.Draw(image).text((12, 12), text, font=font, fill=color)
    box = ImageChops.difference(image, Image.new("RGB", image.size, "white")).getbbox()
    if box:
        if box[2] >= image.width or box[3] >= image.height:
            raise ValueError("Independent literal clipped during drawing")
        image = image.crop((max(0, box[0] - 4), max(0, box[1] - 4), min(image.width, box[2] + 4), min(image.height, box[3] + 4)))
    bgr, gray = bytearray(), bytearray()
    for r, g, b in image.getdata():
        value = (299 * r + 587 * g + 114 * b + 500) // 1000
        bgr.extend((b, g, r, 255)); gray.extend((value, value, value, 255))
    return image.size, bytes(bgr), bytes(gray)


def make_rows(out):
    rows, gold, font_receipts = [], [], {}
    for cohort, (name, seed) in enumerate((("sans", 924113), ("serif", 570683))):
        source, digest = FONTS[name]
        original = out / (name + "-font.ttf")
        download(f"https://raw.githubusercontent.com/google/fonts/{FONT_REV}/ofl/{source}", original, digest, 65000000)
        font_data = TTFont(original, recalcTimestamp=False)
        font_data = instantiateVariableFont(font_data, {"wght": 400}, inplace=True)
        font_data.recalcTimestamp = False
        fixed = out / (name + "-static.ttf"); font_data.save(fixed, reorderTables=False); font_data.close()
        font_receipts[name] = {"source": asset(original, out), "static": asset(fixed, out)}
        rng = random.Random(seed)
        literals = []
        for n in range(24):
            number = rng.randrange(1100, 9900)
            subject = rng.choice(["架空演習", "仮想計測", "空想設計", "模擬研究"])
            teacher = rng.choice(["架空講師", "仮想担当", "空想指導", "模擬教員"])
            marker = rng.choice(["O0", "0O", "I1l", "l1I", "B2O3", "B203"])
            gap = " " if n % 5 == 0 else ""
            literals.extend((("subject", f"{subject}{gap}{number}"), ("teacher", f"{teacher}{gap}{number}"), ("room", f"仮室{marker}{gap}{number}")))
        literals += [("header", text) for text in HEADERS] + [("blank", "")] * 3
        for n, (kind, text) in enumerate(literals):
            row_id = f"n{len(rows):03d}"
            font = ImageFont.truetype(str(fixed), (14, 18, 22)[n % 3])
            (width, height), bgr, gray = pixels(text, font, COLORS[n % len(COLORS)])
            paths = {}
            for condition, data in (("bgr", bgr), ("gray", gray)):
                path = out / (row_id + "-" + condition + ".bgra"); path.write_bytes(data)
                paths[condition] = asset(path, out)
            rows.append({"id": row_id, "width": width, "height": height, **paths})
            gold.append({"id": row_id, "cohort": name, "kind": kind, "literal": text})
    if len(rows) != 192:
        raise ValueError("Frozen corpus count changed")
    return rows, gold, font_receipts


def edit_distance(expected, actual):
    # Unicode scalar CER; punctuation, spaces and case are never normalized.
    previous = list(range(len(actual) + 1))
    for i, left in enumerate(expected, 1):
        current = [i]
        for j, right in enumerate(actual, 1):
            current.append(min(current[-1] + 1, previous[j] + 1, previous[j - 1] + (left != right)))
        previous = current
    return previous[-1]


def score(raw, gold):
    expected = {r["id"]: r for r in gold}
    if len(expected) != 192:
        raise ValueError("Duplicate or incomplete oracle rows")
    seen, summary = set(), {}
    for row in raw:
        key = (row["id"], row["model"], row["condition"])
        if key in seen or row["id"] not in expected or row["model"] not in {m["id"] for m in MODELS} or row["condition"] not in ("bgr", "gray"):
            raise ValueError("Unexpected or duplicate observation")
        seen.add(key); oracle = expected[row["id"]]
        group = (oracle["cohort"], row["model"], row["condition"], oracle["kind"])
        aggregate = summary.setdefault(group, {"observed": 0, "exact": 0, "correctAboveFloor": 0, "incorrectAboveFloor": 0,
            "belowFloorOrEmpty": 0, "unsupportedPadding": 0, "characterEdits": 0, "sourceCharacters": 0,
            "legacyDecodeMilliseconds": 0, "currentDecodeMilliseconds": 0, "inferenceMilliseconds": 0})
        exact = row["text"] == oracle["literal"]
        above = row["minimumConfidence"] is not None and row["minimumConfidence"] >= .8
        aggregate["observed"] += 1; aggregate["exact"] += exact
        aggregate["unsupportedPadding"] += row["unsupportedPadding"]
        aggregate["correctAboveFloor"] += exact and above and not row["unsupportedPadding"]
        aggregate["incorrectAboveFloor"] += not exact and above and not row["unsupportedPadding"]
        aggregate["belowFloorOrEmpty"] += not above
        aggregate["characterEdits"] += edit_distance(oracle["literal"], row["text"])
        aggregate["sourceCharacters"] += len(oracle["literal"])
        for field in ("legacyDecodeMilliseconds", "currentDecodeMilliseconds", "inferenceMilliseconds"):
            aggregate[field] += row[field]
        if not row["decodeExact"]:
            raise ValueError("Changed decoder output")
    if len(seen) != 768:
        raise ValueError("Missing calls remain unassessed; no partial quality score")
    return [{"cohort": k[0], "model": k[1], "condition": k[2], "kind": k[3], **v,
             "cer": v["characterEdits"] / v["sourceCharacters"] if v["sourceCharacters"] else None} for k, v in sorted(summary.items())]


def main():
    parser = argparse.ArgumentParser(); parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--dotnet", default="dotnet"); parser.add_argument("--cleanup", action="store_true")
    args = parser.parse_args(); out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    (out / ".ocr-comparison-owned").write_text(RECIPE + "\n", encoding="utf-8")
    try:
        models = make_models(out); rows, gold, fonts = make_rows(out)
        write_json(out / "inputs.json", {"recipe": RECIPE, "models": models, "rows": rows})
        write_json(out / "oracle.json", gold)
        freeze = {"recipe": RECIPE, "inputs": asset(out / "inputs.json", out), "oracle": asset(out / "oracle.json", out), "fonts": fonts,
                  "pythonVersion": sys.version.split()[0], "pythonPackages": {p: importlib.metadata.version(p) for p in ("Pillow", "fonttools", "PyYAML")},
                  "source": {p.name: sha(p.read_bytes()) for p in Path(__file__).parent.iterdir() if p.suffix in (".cs", ".py", ".csproj")},
                  "productionDecoderSha256": sha((ROOT / "src/Takupoke.Infrastructure/Recovery/OcrCtcDecoder.cs").read_bytes()),
                  "preprocessingSha256": sha((ROOT / "src/Takupoke.Infrastructure/Recovery/OcrInputTransform.cs").read_bytes()),
                  "callsPlanned": 768, "seedSans": 924113, "seedSerif": 570683,
                  "qualification": "unassessed", "scope": "Recognition components; no detector, PDF, role binding, whole-document validation or adoption"}
        write_json(out / "freeze.json", freeze)
        print(json.dumps({"frozen": sha((out / "freeze.json").read_bytes()), "callsPlanned": 768}), flush=True)
        env = {**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1", "ORT_TELEMETRY_DISABLED": "1"}
        subprocess.run([args.dotnet, "build", str(Path(__file__).parent / "Probe.csproj"), "-c", "Release", "--verbosity", "quiet"], env=env, check=True, timeout=180)
        dll = Path(__file__).parent / "bin/Release/net10.0/Takupoke.OcrComparison.dll"
        worker = subprocess.run([args.dotnet, str(dll), str(out / "inputs.json"), str(out / "observations.jsonl")], env=env, capture_output=True, text=True, encoding="utf-8", timeout=900)
        (out / "worker-stdout.txt").write_text(worker.stdout, encoding="utf-8")
        (out / "worker-stderr.txt").write_text(worker.stderr, encoding="utf-8")
        if worker.returncode:
            print(worker.stderr[-4096:], file=sys.stderr); raise RuntimeError("Recognition worker failed; missing calls unassessed")
        # Freeze all raw responses before opening the expected text for scoring.
        raw_path = out / "observations.jsonl"; raw_digest = sha(raw_path.read_bytes())
        write_json(out / "raw-receipt.json", {"sha256": raw_digest, "bytes": raw_path.stat().st_size})
        observed = [json.loads(line) for line in raw_path.read_text(encoding="utf-8").splitlines()]
        gold = json.loads((out / "oracle.json").read_text(encoding="utf-8"))
        if asset(out / "oracle.json", out) != freeze["oracle"]:
            raise ValueError("Oracle changed after freeze")
        report = {**freeze, "rawSha256": raw_digest, "worker": [json.loads(line) for line in worker.stdout.splitlines() if line.startswith("{")],
                  "aggregates": score(observed, gold), "modelActivation": False}
        write_json(out / "report.json", report)
        print(json.dumps(report, ensure_ascii=False), flush=True)
    finally:
        if args.cleanup and (out / ".ocr-comparison-owned").read_text(encoding="utf-8") == RECIPE + "\n":
            shutil.rmtree(out)
            print(json.dumps({"ownedScratchRemoved": not out.exists(), "modelActivation": False}), flush=True)


if __name__ == "__main__":
    main()
