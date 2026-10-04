"""Run only two pinned invented PDFs; score literal expectations after each pipeline result.

No expected content/roles/scopes/blank flags are passed to the native process.
"""
import hashlib
import json
from pathlib import Path
import subprocess
import sys

repo, fixture_root, dll = map(Path, sys.argv[1:])
pins = json.loads((repo / "tools/independent-wide-timetable/artifact-pins.json").read_text(encoding="utf-8"))
observations = []
for name in ("unlabeled", "labeled-control"):
    pin = next(p for p in pins["artifacts"] if p["variant"] == name)
    pdf = fixture_root / pin["file"]
    content = pdf.read_bytes()
    assert len(content) == pin["bytes"] and hashlib.sha256(content).hexdigest() == pin["sha256"]
    child = subprocess.run(["dotnet", str(dll), str(pdf), pin["sha256"], "fictional-wide-" + name],
                           capture_output=True, encoding="utf-8", timeout=240)
    assert len(child.stdout.encode("utf-8")) <= 64 * 1024 * 1024
    result = json.loads(child.stdout)
    print(json.dumps({"case": name, "nativeReport": result, "processExitCode": child.returncode}, ensure_ascii=False), flush=True)
    # Gold enters only this independent post-execution assertion, never the reader.
    gold_bytes = (fixture_root / "expected.json").read_bytes()
    assert hashlib.sha256(gold_bytes).hexdigest() == pins["expectedSha256"]
    gold = json.loads(gold_bytes)
    timetable = result["strictResult"] or ((result["formal"] or {}).get("Timetable"))
    exact = None
    mismatch_count = None
    extras = None
    if timetable is not None:
        actual = {}
        for lesson in timetable["Lessons"]:
            key = (lesson["ClassName"], lesson["Weekday"], lesson["Period"])
            fields = lesson["Names"]
            actual.setdefault(key, []).append({"subject": fields["Subject"], "teacher": fields["Teacher"], "room": fields["Room"]})
        expected = {(s["className"], s["weekday"], s["period"]): s["lessons"] for s in gold["slots"]}
        assert len(expected) == 680
        mismatch_count = sum(actual.get(k, []) != v for k, v in expected.items())
        extras = len(set(actual) - set(expected))
        exact = (mismatch_count == 0 and extras == 0 and timetable["SchoolYear"] == gold["schoolYear"] and timetable["Term"] == gold["term"])
    accepted = result["outcome"] in ("strict-returned", "formal-returned-unscored")
    observations.append({"case": name, "outcome": result["outcome"], "stage": result["stage"], "failure": result["failure"],
                         "processExitCode": child.returncode, "accepted": accepted, "literalExact": exact,
                         "incorrectAcceptance": (accepted and exact is False) if exact is not None else None, "literalAssessmentStatus": "assessed" if exact is not None else "unassessed", "slotObligations": 680,
                         "slotMismatches": mismatch_count, "extraSlots": extras, "nativeOcrCalls": result["nativeOcrCalls"], "llmCalls": result["llmCalls"]})
print(json.dumps({"recipe": "fictional-windows-vector-render-blank-v1", "observations": observations,
                  "scope": "Independent generated vector main and inline-label control; actual source/blank pipeline. No OCR, model qualification, or negative safety denominator."}, ensure_ascii=False), flush=True)
# Refusals and execution errors remain explicit. This research exit means the
# two reports were retained; it never asserts semantic quality or catalog PASS.
