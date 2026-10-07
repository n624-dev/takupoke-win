"""Compare independent literals only after the real source pipeline returned."""
import argparse
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--fixtures", required=True, type=Path)
parser.add_argument("--dll", required=True, type=Path)
parser.add_argument("--dotnet", default="dotnet")
args = parser.parse_args()
manifest = json.loads((args.fixtures/"manifest.json").read_text(encoding="utf-8"))
observations = []
for case in manifest["cases"]:
    child = subprocess.run([args.dotnet, str(args.dll), str(args.fixtures/case["file"]), case["sha256"]], capture_output=True, text=True, encoding="utf-8", timeout=180)
    assert child.returncode == 0, child.stderr[:2000]
    actual = json.loads(child.stdout)
    # The independently designed expected text is first inspected after return.
    gold = case["oracle"]
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
           "extraKeys": extra_keys, "correctDecision": exact if case["expect"] == "exact" else not accepted and disposition != "execution-error", **actual}
    observations.append(row)
    print(json.dumps({"orderedRowE2E": row}, ensure_ascii=False), flush=True)
(args.fixtures/"observations-windows.json").write_text(json.dumps(observations, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
print(json.dumps({"summary": {"positiveDocuments": 2, "negativeDocuments": 4, "exactPositive": sum(r["classification"] == "correct-formal" and r["expected"] == "exact" for r in observations), "correctRefusals": sum(r["classification"] == "correct-refusal" for r in observations), "incorrectFormal": sum(r["classification"] == "incorrect-formal" or r["expected"] == "refuse" and r["literalExact"] is True for r in observations), "executionErrors": sum(r["classification"] == "execution-error" for r in observations)}}), flush=True)
