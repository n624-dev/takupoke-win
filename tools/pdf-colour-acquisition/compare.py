#!/usr/bin/env python3
"""Require complete source outcomes and unchanged negative contracts."""
import argparse
import json
from pathlib import Path


def compare(before, after):
    if before['fixtureManifestSHA'] != after['fixtureManifestSHA']: raise ValueError('Different frozen source corpus')
    old={r['case']:r for r in before['rows']};new={r['case']:r for r in after['rows']}
    positives={'black','red','blue','gray','mixed'}
    negatives={'near-white','white','transparent','covered','missing-class','missing-body'}
    if set(old)!=positives|negatives or set(new)!=set(old):raise ValueError('Incomplete or foreign comparison cases')
    for name in old:
        left,right=old[name],new[name]
        if left['pdfSHA']!=right['pdfSHA'] or left['expected']!=right['expected']:raise ValueError('Source/oracle comparison changed')
        if right['nativeOcrCalls'] or right['llmCalls'] or right['manualCorrections']:raise ValueError('Source-only contract changed')
        if name in positives:
            if right['classification']!='exact-formal' or not right['literalExact'] or not right['readerComplete'] or right['builderCells']!=680 or right['originalOrderedProofs']!=680:
                raise ValueError('Complete positive source failed: '+name)
        elif right['classification']!='supported-scope-refusal':raise ValueError('Negative source admitted or unassessed: '+name)
    if old['black']['classification']!='exact-formal':raise ValueError('Prior readable black control failed')
    if any(old[name]['classification']!='readable-refusal' for name in positives-{'black'}):raise ValueError('Baseline does not isolate the documented colour refusals')
    return {'completePositiveDocumentsBefore':1,'completePositiveDocumentsAfter':5,
        'newColourDocumentsRecovered':4,'unchangedRefusals':6,'incorrectFormalAcceptances':0,
        'manualCorrections':0,'ocrCalls':0,'llmCalls':0,
        'scope':'Consumed source-colour compatibility; no image-only OCR or downloaded-model qualification'}


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--baseline',required=True,type=Path);p.add_argument('--candidate',required=True,type=Path)
    args=p.parse_args();print(json.dumps(compare(json.loads(args.baseline.read_text()),json.loads(args.candidate.read_text()))))
