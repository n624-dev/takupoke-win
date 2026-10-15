"""Retrospective typing-aid coverage on closed fictional OCR, never new OCR credit."""
import argparse
import hashlib
import json
import random
import shutil
import subprocess
from pathlib import Path
from run import asset, sha, write

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--study', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', required=True)
    args = parser.parse_args()
    src, out = args.study, args.output
    out.mkdir(parents=True, exist_ok=False)
    receipt = json.loads((src/'worker-receipt.json').read_text())
    assert receipt['exitCode'] == 0 and sha(src/'raw.jsonl') == receipt['raw']['sha256']
    oracle = json.loads((src/'oracle.json').read_text())
    literals = {g['value'] for g in oracle if g['value']}
    known = {v for v in literals if int(hashlib.sha256(v.encode()).hexdigest(), 16) % 2 == 0}
    rng, unused = random.Random(202610161515), set()
    while len(unused) < 64:
        value = '仮設室' + ''.join(rng.choice('Il1O0') for _ in range(3)) + str(rng.randrange(20, 99))
        if value not in literals:
            unused.add(value)
    rooms = sorted(known | unused)
    rows = [dict(id=r['id'], text=r['text']) for r in map(json.loads, (src/'raw.jsonl').read_text().splitlines())
            if r['model'] == 'actual-manual-code-route']
    write(out/'inputs.json', dict(rooms=rooms, rows=rows))
    write(out/'roster.json', dict(rooms=rooms, unused=sorted(unused),
        scope='SHA parity on whole fictional literals after OCR closed, before candidate worker. Retrospective reference coverage; no new OCR evaluation.'))
    baseline = subprocess.check_output(['git', 'show', '469e49960b4b24ee48322b551baced32d4dae07b:src/Takupoke.Core/Recovery/RecoveryCodeCandidates.cs'], cwd=ROOT)
    baseline = baseline.replace(b'public static class RecoveryCodeCandidates', b'internal static class PreviousRecoveryCodeCandidates')
    (out/'Baseline.cs').write_bytes(baseline)
    project = Path(__file__).parent/'candidate-review/CandidateReview.csproj'
    subprocess.run([args.dotnet, 'build', str(project), '-c', 'Release', f'-p:BaselineSourcePath={out.resolve()/"Baseline.cs"}', '--verbosity', 'quiet'], check=True)
    reader = out/'reader'
    shutil.copytree(project.parent/'bin/Release/net10.0', reader)
    write(out/'freeze.json', dict(inputs=asset(out/'inputs.json', out), roster=asset(out/'roster.json', out),
        originalRawSHA256=sha(src/'raw.jsonl'), oracleSHA256=sha(src/'oracle.json'),
        baselineSource=asset(out/'Baseline.cs', out), currentSourceSHA256=sha(ROOT/'src/Takupoke.Core/Recovery/RecoveryCodeCandidates.cs'),
        reader=[asset(p, out) for p in sorted(reader.rglob('*')) if p.is_file()]))
    with (out/'stdout.txt').open('x') as stdout, (out/'stderr.txt').open('x') as stderr:
        child = subprocess.run([args.dotnet, str(reader/'Takupoke.CandidateReview.dll'), str(out/'inputs.json'), str(out/'raw.jsonl')], stdout=stdout, stderr=stderr, timeout=60)
    write(out/'receipt.json', dict(exitCode=child.returncode, raw=asset(out/'raw.jsonl', out)))
    assert child.returncode == 0
    observed = {r['id']: r for r in map(json.loads, (out/'raw.jsonl').read_text().splitlines())}
    assert len(observed) == len(rows) == len(oracle)
    reports = []
    for margin in [2, 6]:
        selected = [g for g in oracle if g['margin'] == margin]
        for route in ['before', 'after']:
            wrong = [g for g in selected if observed[g['id']]['text'] != g['value']]
            reports.append(dict(margin=margin, route=route, rows=len(selected), ocrExact=len(selected)-len(wrong),
                wrongWithReference=sum(g['value'] in rooms for g in wrong),
                wrongWithCorrectCandidate=sum(g['value'] in observed[g['id']][route] for g in wrong),
                alreadyCorrectWithAlternatives=sum(bool(observed[g['id']][route]) and observed[g['id']]['text'] == g['value'] for g in selected),
                unknownWithAlternatives=sum(bool(observed[g['id']][route]) and g['value'] not in rooms for g in selected),
                withCandidates=sum(bool(observed[g['id']][route]) for g in selected)))
    report = dict(scores=reports, rawSHA256=sha(out/'raw.jsonl'),
        scope='Retrospective reference-roster typing-aid coverage, not new OCR accuracy, completed corrections or human review time. Japanese/space characters must match literally; no automatic substitution.')
    write(out/'report.json', report)
    print(json.dumps(report, ensure_ascii=False))


if __name__ == '__main__':
    main()
