#!/usr/bin/env python3
"""Fixed new input recipe on CLOSED development drawings, with frozen binaries."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

from run import ROOT, sha, write
from score import assess


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--closed-development', type=Path, required=True)
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    source = args.closed_development
    manifest = json.loads((source/'inputs.json').read_text(encoding='utf-8'))
    if manifest['recipe'] != 'physical-input-framing-fresh-development-v1' or len(manifest['documents']) != 6:
        raise ValueError('Closed development cohort changed')
    # Hash and verify closure before reuse; never treat this as a fresh final.
    original_raw = (source/'observations.jsonl').read_bytes()
    rows = [json.loads(x) for x in original_raw.decode().splitlines()]
    if len(rows) != 12: raise ValueError('Original development worker was not closed')
    args.output.mkdir(parents=True, exist_ok=False)
    assets = [manifest[key] for key in ('detector', 'recognizer', 'dictionary')]
    assets += [p['pixels'] for d in manifest['documents'] for p in d['pages']]
    for asset in assets:
        p = source / asset['file']
        if p.stat().st_size != asset['bytes'] or sha(p) != asset['sha256']:
            raise ValueError('Closed original pixel/model identity changed')
        shutil.copyfile(p, args.output/asset['file'])
    for doc in manifest['documents']:
        # A priori condition set, not selected from prediction quality or gold.
        doc['plannedConditions'] = ['production', 'physical-input-frame', 'ink-input-frame'] if doc['id'].endswith('-positive') else ['ink-input-frame']
    manifest['sourceScope'] = 'Consumed full 17-class development drawings, new fixed original-image/2px ink-input recipe; not a fresh final or deployment qualification'
    write(args.output/'inputs.json', manifest)
    env = {**os.environ, 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1', 'ORT_TELEMETRY_DISABLED': '1'}
    subprocess.run([args.dotnet, 'build', str(ROOT/'Probe.csproj'), '-c', 'Release', '--verbosity', 'quiet'], env=env, check=True, timeout=240)
    # A dedicated runtime copy prevents future local builds or colleagues from
    # changing any assembly while the measurement process is active.
    reader = args.output/'reader'
    shutil.copytree(ROOT/'bin/Release/net10.0', reader)
    binary_identity = {str(p.relative_to(reader)): {'bytes': p.stat().st_size, 'sha256': sha(p)}
                       for p in sorted(reader.rglob('*')) if p.is_file()}
    write(args.output/'pre-inference-freeze.json', {
        'recipe': 'consumed-dense-original-image-ink-input-2px-v1', 'closedPriorRawSHA256': hashlib.sha256(original_raw).hexdigest(),
        'closedGeneratorFreezeSHA256': sha(source/'pre-generation-freeze.json'),
        'sources': {p.name: sha(p) for p in (ROOT/'Program.cs', ROOT/'PhysicalCropFrame.cs', ROOT/'InkCropFrame.cs',
            ROOT/'OriginalImageRead.cs', ROOT/'run_ink_development.py', ROOT/'prepare_pdf.py')},
        'plannedConditions': {d['id']: d['plannedConditions'] for d in manifest['documents']},
        'fixedOriginalPixelMargin': 2, 'preserve': 'All nonwhite/nonrule original pixels, native decoder, scores and confidence floor',
        'readerFiles': binary_identity, 'oracleSHA256': sha(source/'oracle.json'),
        'scope': 'Already consumed development cases, stable worker copy; no independent final/production credit'})
    subprocess.run([sys.executable, '-B', str(ROOT/'prepare_pdf.py'), '--manifest', str(args.output/'inputs.json')], check=True, timeout=120)
    safe = {k:v for k,v in env.items() if not any(s in k.upper() for s in ('TOKEN','SECRET','PASSWORD','CREDENTIAL'))}
    dll = reader/'Takupoke.OcrComparison.dll'
    subprocess.run([args.dotnet, str(dll), '--extract-original-images', str(args.output/'pdf-inputs.json')], env=safe, check=True, timeout=240)
    raw = args.output/'observations.jsonl'
    with (args.output/'worker-stdout.txt').open('w', encoding='utf-8') as stdout, (args.output/'worker-stderr.txt').open('w', encoding='utf-8') as stderr:
        child = subprocess.run([args.dotnet, str(dll), str(args.output/'source-original-inputs.json'), str(raw)],
                               env=safe, stdout=stdout, stderr=stderr, timeout=1000)
    write(args.output/'raw-receipt.json', {'exitCode': child.returncode, 'bytes': raw.stat().st_size if raw.exists() else None,
                                         'rawSHA256': sha(raw) if raw.exists() else None})
    for name, expected in binary_identity.items():
        if sha(reader/name) != expected['sha256']: raise ValueError('Worker binary changed during native measurement')
    if child.returncode: raise RuntimeError('Native execution failed; partial observations retained and unassessed')
    # Only after the native child exits may the separate oracle be opened.
    gold = {d['id']:d for d in json.loads((source/'oracle.json').read_text(encoding='utf-8'))}
    observations = [json.loads(x) for x in raw.read_text(encoding='utf-8').splitlines()]
    expected = {(d['id'], c) for d in manifest['documents'] for c in d['plannedConditions']}
    if len(observations) != len(expected) or {(o['id'],o['condition']) for o in observations} != expected:
        raise ValueError('Missing or duplicated planned condition, unassessed')
    summary = []
    for o in observations:
        g = gold[o['id']]
        if g['expect'] == 'exact': scored = assess(o, g)
        else: scored = {'id':o['id'], 'condition':o['condition'], 'outcome':o['outcome'], 'failure':o['failure'],
            'quality':'unassessed' if o['outcome']=='execution-error-unassessed' else 'negative-refused-unattributed' if o['formal'] is None else 'negative-wrongly-returned',
            'formalReturned':o['formal'] is not None, 'exactDocument':False,
            'scope':'Refusal does not prove the intended defect caused it; compare complete positive/source evidence separately'}
        scored.update({k:o[k] for k in ('requiredSlots','cells','lowConfidenceSources','recognizerCalls','detectorCalls','milliseconds','processPeakWorkingSetBytes')})
        summary.append(scored)
    report = {'recipe':'consumed-dense-original-image-ink-input-2px-v1', 'rawSHA256':sha(raw), 'readerBinaryIdentityStable':True,
        'scope':'Consumed development drawing pixels from actual verified PDF source images; no heldout/device/production qualification', 'observations':summary}
    write(args.output/'report.json', report)
    print(json.dumps(report, ensure_ascii=False), flush=True)


if __name__ == '__main__': main()
