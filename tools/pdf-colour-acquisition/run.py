#!/usr/bin/env python3
"""Score source-only complete-PDF observations after each reader child exits."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess


def main():
    p=argparse.ArgumentParser();p.add_argument('--fixtures',required=True,type=Path)
    p.add_argument('--dll',required=True,type=Path);p.add_argument('--dotnet',default='dotnet')
    p.add_argument('--output',required=True,type=Path);args=p.parse_args()
    if args.output.exists():raise ValueError('Source observations must be new')
    manifest=json.loads((args.fixtures/'manifest.json').read_text(encoding='utf-8'))
    rows=[]
    for case in manifest['cases']:
        path=args.fixtures/case['file']
        if hashlib.sha256(path.read_bytes()).hexdigest()!=case['sha256']:raise ValueError('Frozen PDF changed')
        child=subprocess.run([args.dotnet,str(args.dll),str(path),case['sha256']],
            capture_output=True,text=True,encoding='utf-8',timeout=180)
        if child.returncode:raise RuntimeError('Native source child failed: '+child.stderr[-1000:])
        actual=json.loads(child.stdout); formal=actual.pop('formal')
        # Exact expected literals and roles enter only this post-child scorer.
        expected={(s['className'],s['weekday'],s['period']):s['lessons'] for s in case['oracle']['slots']}
        seen={}
        if formal is not None:
            for lesson in formal['Lessons']:
                key=(lesson['ClassName'],lesson['Weekday'],lesson['Period']);n=lesson['Names']
                seen.setdefault(key,[]).append({'subject':n['Subject'],'teacher':n['Teacher'],'room':n['Room']})
        exact = formal is not None and seen==expected and formal['SchoolYear']==case['oracle']['schoolYear'] and formal['Term']==case['oracle']['term']
        accepted=formal is not None; error=actual['outcome']=='execution-error'
        disposition=('exact-formal' if exact else 'incorrect-formal') if accepted else 'execution-error' if error else 'readable-refusal' if case['expect']=='exact' else 'supported-scope-refusal'
        row={'case':case['case'],'expected':case['expect'],'classification':disposition,
            'literalExact':exact,'slots':len(expected),'bodyValues':3*len(expected),
            'manualCorrections':0,'pdfSHA':case['sha256'],**actual}
        rows.append(row);print(json.dumps(row,ensure_ascii=False),flush=True)
    report={'recipe':manifest['recipe'],'fixtureManifestSHA':hashlib.sha256((args.fixtures/'manifest.json').read_bytes()).hexdigest(),
        'rows':rows,'summary':{k:sum(r['classification']==k for r in rows) for k in ('exact-formal','incorrect-formal','readable-refusal','supported-scope-refusal','execution-error')},
        'scope':manifest['scope'],'modelQualification':False,'savedAdoptions':0}
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report['summary']),flush=True)
    if report['summary']['execution-error'] or report['summary']['incorrect-formal']:raise RuntimeError('Unsafe or unassessed source comparison')


if __name__=='__main__':main()
