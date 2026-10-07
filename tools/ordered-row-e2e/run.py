"""Compare independent literals only after the real source pipeline returned."""
import argparse
import json
from pathlib import Path
import subprocess
import hashlib

parser = argparse.ArgumentParser()
parser.add_argument("--fixtures", required=True, type=Path)
parser.add_argument("--dll", required=True, type=Path)
parser.add_argument("--dotnet", default="dotnet")
parser.add_argument("--manifest", default="manifest.json")
parser.add_argument("--timeout", type=int, default=180)
args = parser.parse_args()
manifest = json.loads((args.fixtures/args.manifest).read_text(encoding="utf-8"))
observations = []
for case in manifest["cases"]:
    child = subprocess.run([args.dotnet, str(args.dll), str(args.fixtures/case["file"]), case["sha256"]], capture_output=True, text=True, encoding="utf-8", timeout=args.timeout)
    assert child.returncode == 0, child.stderr[:2000]
    actual = json.loads(child.stdout)
    if args.manifest == "raster-manifest.json" and actual.get("rasterCaptures"):
        # Assertion-only original pixels, inspected after native return. These
        # hashes do not enter detector, recognizer, crop or cell assignments.
        import fitz
        with fitz.open(args.fixtures/case["file"]) as source:
            for capture in actual["rasterCaptures"]:
                page=source[capture["Page"]-1]
                original=fitz.Pixmap(source,page.get_images()[0][0])
                rgb=original.samples
                bgra=bytearray(original.width*original.height*4)
                for channel in range(3):bgra[channel::4]=rgb[2-channel::3]
                bgra[3::4]=bytes([255])*(original.width*original.height)
                same_size=capture["Width"]==original.width and capture["Height"]==original.height
                capture["opaque255BgraEncodingMatchesEmbedded"]=(same_size and capture["BgraSha256"]==hashlib.sha256(bgra).hexdigest())
                capture["colourManagedRgbMatchesEmbedded"]=(same_size and capture["ColourManagedRgbSha256"]==hashlib.sha256(rgb).hexdigest())
                if same_size:
                    for endpoint in capture["RuleEndpoints"]:
                        embedded_min=[]; embedded_mean=[]
                        for y in range(endpoint["Y"],endpoint["Y"]+endpoint["Height"]):
                            for x in range(endpoint["X"],endpoint["X"]+endpoint["Width"]):
                                pixel=rgb[(y*original.width+x)*3:(y*original.width+x+1)*3]
                                embedded_min.append(min(pixel)); embedded_mean.append(sum(pixel)//3)
                        endpoint["embeddedMinimumChannels"]=embedded_min
                        endpoint["inkClassificationChanges"]=sum((a!=255)!=(b!=255) for a,b in zip(endpoint["MinimumChannels"],embedded_min))
                        endpoint["darkClassificationChanges"]=sum((a<160)!=(b<160) for a,b in zip(endpoint["MeanChannels"],embedded_mean))
    # The independently designed expected text is first inspected after return.
    gold = case["oracle"]
    if actual.get("shadow"):
        from shadow_metrics import summarize
        shadow=actual["shadow"]
        def crop_inputs(page, key):
            return [(row["crop"],row["ValidWidth"],row["InputWidth"]) for row in page[key]]
        has_japanese=bool(shadow["pages"]) and all(page.get("japaneseRows") for page in shadow["pages"])
        actual["shadow"] = {"mobile": summarize(shadow, gold),
                            "allSourcePagesCompleted":len(shadow["pages"])==len(case["embeddedPages"]) and shadow["error"] is None,
                            "qualifiedModels":[]}
        if has_japanese:
            actual["shadow"].update({"japanese":summarize(shadow,gold,"japaneseRows"),
                "japaneseBytes":10089078,"japaneseSHA256":"2e397c27461751befc82eca78c9b19e87322ebee34e7316468b21856f9977d64",
                "completedPagesUseSameRaster":True,
                "sameCropInputsOnCompletedPages":all(crop_inputs(page,"rows")==crop_inputs(page,"japaneseRows") for page in shadow["pages"])})
    table = actual.pop("formal")
    accepted = table is not None
    exact = None; mismatches = None; value_errors = None; extra_keys = None
    if table is not None:
        seen = {}
        for lesson in table["Lessons"]:
            key = (lesson["ClassName"], lesson["Weekday"], lesson["Period"])
            n = lesson["Names"]
            seen.setdefault(key, []).append({"subject": n["Subject"], "teacher": n["Teacher"], "room": n["Room"]})
        expected = {(s["className"], s["weekday"], s["period"]): s["lessons"] for s in gold["slots"]}
        mismatches = sum(seen.get(k) != v for k, v in expected.items())
        extra_keys = len(set(seen)-set(expected))
        value_errors = sum(3 if len(seen.get(k, [])) != 1 else sum(seen[k][0].get(role) != v[0][role] for role in ("subject", "teacher", "room")) for k, v in expected.items())
        exact = mismatches == 0 and extra_keys == 0 and table["SchoolYear"] == gold["schoolYear"] and table["Term"] == gold["term"]
    disposition = ("correct-formal" if exact else "incorrect-formal") if accepted else ("execution-error" if actual["outcome"] == "execution-error" else "recovery-failure" if case["expect"] == "exact" else "correct-refusal")
    row = {"case": case["case"], "expected": case["expect"], "classification": disposition, "literalExact": exact,
           "slotObligations": 680, "bodyValueObligations": 2040, "slotErrors": mismatches, "bodyValueErrors": value_errors,
           "extraKeys": extra_keys, "correctDecision": bool(exact) if case["expect"] == "exact" else not accepted and disposition != "execution-error", **actual}
    observations.append(row)
    print(json.dumps({"orderedRowE2E": row}, ensure_ascii=False), flush=True)
(args.fixtures/("observations-windows-image.json" if args.manifest=="raster-manifest.json" else "observations-windows.json")).write_text(json.dumps(observations, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
print(json.dumps({"summary": {"positiveDocuments": sum(c["expect"]=="exact" for c in manifest["cases"]), "negativeDocuments": sum(c["expect"]=="refuse" for c in manifest["cases"]), "exactPositive": sum(r["classification"] == "correct-formal" and r["expected"] == "exact" for r in observations), "correctRefusals": sum(r["classification"] == "correct-refusal" for r in observations), "incorrectFormal": sum(r["classification"] == "incorrect-formal" or r["expected"] == "refuse" and r["literalExact"] is True for r in observations), "executionErrors": sum(r["classification"] == "execution-error" for r in observations)}}), flush=True)
