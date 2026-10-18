#!/usr/bin/env python3
"""Qualify the complete independent source cases only, never an OCR model."""
import argparse
import json
from pathlib import Path


def check(report):
    if report['recipe']!='independent-source-colour-final-v1':raise ValueError('Final recipe mismatch')
    expected={f'{font}-{kind}' for font in ('kosugi','sawarabi') for kind in ('positive','missing-day','missing-body')}
    if {r['case'] for r in report['rows']}!=expected or len(report['rows'])!=6:raise ValueError('Final case coverage changed')
    for row in report['rows']:
        if row['nativeOcrCalls'] or row['llmCalls'] or row['manualCorrections']:raise ValueError('Source-only contract changed')
        if row['processPeakWorkingSetBytes']<=0 or row['processPeakWorkingSetBytes']>2*1024**3:raise ValueError('Source method exceeds the fixed CPU16GB component budget')
        if row['case'].endswith('-positive'):
            if row['classification']!='exact-formal' or not row['readerComplete'] or row['builderCells']!=680 or row['originalOrderedProofs']!=680:raise ValueError('Independent complete source failed: '+row['case'])
        elif row['classification']!='supported-scope-refusal':raise ValueError('Independent missing source admitted or unassessed')
    return {'recipe':report['recipe'],'completeExactPositiveDocuments':2,'correctRefusals':4,
        'incorrectAcceptances':0,'manualCorrections':0,'ocrCalls':0,'llmCalls':0,
        'largestProcessPeakWorkingSetBytes':max(r['processPeakWorkingSetBytes'] for r in report['rows']),
        'qualification':'Complete fictional source-only route; image OCR/learned model/device deployment still separate'}


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--report',required=True,type=Path);args=p.parse_args()
    print(json.dumps(check(json.loads(args.report.read_text(encoding='utf-8')))))
