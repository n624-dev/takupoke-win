"""Post-run deterministic diagnostics; no model loading or inference."""
import ast
from collections import Counter
import hashlib
import json
from pathlib import Path

from data import ROOT,REPO,save

RESULTS=REPO/'research-results/ocr-synthetic-training-20261013/ascii-paddle-transfer-v1'
SOURCE=Path(__file__).parent


def main():
    selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'))
    freeze=json.loads((ROOT/'freeze.json').read_text(encoding='utf-8'))
    for name,expected in freeze['sourceHashes'].items():
        assert hashlib.sha256((SOURCE/name).read_bytes()).hexdigest()==expected
    manifest=json.loads((ROOT/'development/manifest.json').read_text(encoding='utf-8'))
    lookup={row['id']:row for row in manifest['rows']}
    original=[json.loads(line) for line in (RESULTS/'original-full18710-unjudged.jsonl').read_text(encoding='utf-8').splitlines()]
    reduced=[json.loads(line) for line in (RESULTS/'reduced-untrained-unjudged.jsonl').read_text(encoding='utf-8').splitlines()]
    before={row['id']:row for row in original};reduced_by_id={row['id']:row for row in reduced}
    diagnostics=[]
    for candidate in [selection['original']]+selection['candidates']:
        name=candidate['name'];raw=RESULTS/f'{name}-unjudged.jsonl'
        assert hashlib.sha256(raw.read_bytes()).hexdigest()==candidate['metrics']['rawSha256']
        records=[json.loads(line) for line in raw.read_text(encoding='utf-8').splitlines()]
        by_id={row['id']:row for row in records};positives=[row for row in records if not lookup[row['id']]['reject']]
        exact=lambda group,key:group[key]['prediction']==lookup[key]['text']
        groups={}
        for font in sorted(manifest['fonts']):
            group=[row for row in positives if lookup[row['id']]['font']==font]
            groups[font]={'rows':len(group),'exact':sum(row['prediction']==lookup[row['id']]['text'] for row in group),'correctAccepted':sum(row['accepted'] and row['prediction']==lookup[row['id']]['text'] for row in group),'incorrectAccepted':sum(row['accepted'] and row['prediction']!=lookup[row['id']]['text'] for row in group)}
        wrong=[row for row in positives if row['accepted'] and row['prediction']!=lookup[row['id']]['text']]
        diagnostics.append({'name':name,'fonts':groups,'improvementsVsOriginal':sum(not exact(before,row['id']) and exact(by_id,row['id']) for row in positives),'regressionsVsOriginal':sum(exact(before,row['id']) and not exact(by_id,row['id']) for row in positives),'improvementsVsReducedUntrained':sum(not exact(reduced_by_id,row['id']) and exact(by_id,row['id']) for row in positives),'regressionsVsReducedUntrained':sum(exact(reduced_by_id,row['id']) and not exact(by_id,row['id']) for row in positives),'maximumWrongAcceptedMinimumConfidence':max((row['minimumConfidence'] for row in wrong),default=None),'wrongAcceptedIds':[row['id'] for row in wrong]})
    gate=json.loads((RESULTS/'final-opening-gate.json').read_text(encoding='utf-8'));selected=selection['selected'];selected_diagnostic=next(row for row in diagnostics if row['name']==selected['name']);metrics=selected['metrics'];n=metrics['positiveRows']
    conditions={'selectedTrained':selected['epoch']>0,'rawExactAtLeast97percent':metrics['exact']/n>=gate['minimumPositiveRawExactFraction'],'correctAcceptedAtLeast90percent':metrics['correctAccepted']/n>=gate['minimumCorrectAcceptedFraction'],'incorrectAcceptedZero':metrics['incorrectAccepted']<=gate['maximumIncorrectAccepted'],'negativeAcceptedZero':metrics['negativeAccepted']<=gate['maximumNegativeAccepted'],'eachDevFontExactAtLeast95percent':all(row['exact']/row['rows']>=gate['minimumPerDevelopmentFontRawExactFraction'] for row in selected_diagnostic['fonts'].values())}
    result={'scope':'Independent development diagnostics; no final, activation or inference correction','selected':selected['name'],'diagnostics':diagnostics,'continuationGate':conditions,'mayPlanSeparateFinalAndPortableStage':all(conditions.values()),'limits':'Even passing a research continuation gate cannot qualify literal header/source ownership, contradictory source/script provenance or full-document correctness.'}
    save(RESULTS/'development-diagnosis.json',result)
    # Verify prior-exclusion snapshot only when original frozen bytes remain.
    # Concurrent parent research may modify files; an immutable receipt still
    # records exactly the bytes used, without replaying their newer labels.
    exclusion=json.loads((RESULTS/'exclusion-receipt.json').read_text(encoding='utf-8'));unchanged=0;changed=[];used=set()
    def walk(value):
        if isinstance(value,str) and len(value)<=256:used.add(value)
        elif isinstance(value,list):
            for item in value:walk(item)
        elif isinstance(value,dict):
            for item in value.values():walk(item)
    for entry in exclusion['files']:
        path=Path(entry['path'])
        if not path.exists() or hashlib.sha256(path.read_bytes()).hexdigest()!=entry['sha256']:
            changed.append(str(path));continue
        unchanged+=1;content=path.read_text(encoding='utf-8')
        if path.suffix=='.jsonl':
            for line in content.splitlines():
                if line.strip():walk(json.loads(line))
        else:walk(json.loads(content))
    train_manifest=json.loads((ROOT/'train/manifest.json').read_text(encoding='utf-8'));training={row['text'] for row in train_manifest['rows'] if row['text']};development={row['text'] for row in manifest['rows'] if row['text']}
    assert not training.intersection(used) and not training.intersection(development) and not development.intersection(used)
    for path in SOURCE.glob('*.py'):
        for node in ast.walk(ast.parse(path.read_text(encoding='utf-8'))):
            if isinstance(node,ast.Call) and isinstance(node.func,ast.Attribute) and node.func.attr in ['read_text','write_text']:
                keys={keyword.arg for keyword in node.keywords};assert 'encoding' in keys
                if node.func.attr=='write_text':assert 'newline' in keys
    registration=json.loads((RESULTS/'gate-registration.json').read_text(encoding='utf-8'));prefix=(ROOT/'training.log').read_bytes()[:registration['trainingLogPrefixBytes']]
    assert hashlib.sha256(prefix).hexdigest()==registration['trainingLogPrefixSha256']
    assert not registration['trainedCheckpointSummariesExist']
    assert not (ROOT/'final').exists()
    archive=Path('/workspace/scratch/ocr-finetune-20261013/ocr-synthetic-research-portable-20261013-v2.zip');assert hashlib.sha256(archive.read_bytes()).hexdigest()=='66af7ced21e66f30594db39d5e329e37249360a03c22d2653c09d7eb4e791c13'
    save(RESULTS/'verification.json',{'frozenTrainingSourceHashes':True,'allCandidateRawHashes':True,'trainingDevelopmentDisjoint':True,'unchangedPriorLiteralExclusionIntersection':0,'unchangedExclusionFiles':unchanged,'exclusionFilesChangedAfterSnapshot':changed,'gateRegisteredBeforeFirstTrainedDevelopmentCheckpoint':True,'utf8LfSourceIoAudit':True,'finalDirectoryAbsent':True,'previousPortableZipUnchanged':True,'scope':'Source and saved-artifact verification only, no inference repeated'})
    print(json.dumps(result))


if __name__=='__main__':main()
