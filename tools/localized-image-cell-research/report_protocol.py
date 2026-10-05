"""Post-worker protocol comparison only; no oracle and no quality/adoption gate."""
import json
from pathlib import Path
from protocol import digest
ROOT=Path(__file__).resolve().parent

def report(root):
    responses=root/'responses.jsonl'
    rows=[json.loads(s) for s in responses.read_text().splitlines()] if responses.exists() else []
    if len(rows)>2 or [r['stage'] for r in rows] not in ([],['blind-image'],['blind-image','ocr-compare']):
        raise RuntimeError('CALL_ORDER_OR_CAP_CHANGED')
    baseline=json.loads((root/'retained-text-baseline.jsonl').read_text())
    task=json.loads((root/'inputs.json').read_text())['tasks'][0]
    native_lines=[r['rawText'] for r in task['acquisitionRows']]
    blind=rows[0] if rows else None;comparison=rows[1] if len(rows)==2 else None
    image=blind.get('candidate') if blind else None
    frozen=root/'blind-result-frozen.json'
    blind_sha=digest(frozen) if frozen.exists() else None
    if comparison and comparison['blindFrozenSHA256']!=blind_sha:raise RuntimeError('BLIND_FREEZE_LINK_MISMATCH')
    v={'completedCalls':len(rows),'declaredMaximumCalls':2,'operationalErrors':sum(r['disposition']=='OPERATIONAL_UNASSESSED' for r in rows),
       'missingCallsUNASSESSED':2-len(rows),'modelBaselineRun':37383946385,'newTextBaselineCalls':0,
       'retainedCurrentOCRLines':native_lines,'retainedTextBaselineCandidate':baseline['candidate'],
       'blindImageCandidate':image,'blindResultFrozenSHA256':blind_sha,
       'literalAgreementWithRetainedOCR':image['lines']==native_lines if image and image['state']=='TRANSCRIBED' else 'UNASSESSED',
       'originalIDOCRComparisonCandidate':comparison.get('candidate') if comparison else None,
       'candidateIDsChangedFromRetainedTextBaseline':comparison['candidate']['ids']!=baseline['candidate']['ids'] if comparison and comparison.get('candidate') else 'UNASSESSED',
       'literalAgreementMeaning':'Agreement/disagreement only; OCR is not truth; image is not an ink or role certificate',
       'comparisonContrast':'Image + shared short-row representation vs retained older atom-text condition; prompt differs, no image-only causal claim',
       'correctness':'UNASSESSED_NO_ORACLE_READ','coreValidatorActuallyInvoked':False,'productionAdoption':False,'qualifiedGenAIModels':[],
       'wholeDocumentFormalSlotsUNASSESSED':1270,'wholeDocumentFormalClocksUNASSESSED':70,'confidenceThresholdChanged':False,
       'newImageOriginIDs':0,'maximumManualBodyCorrectionsDocumentWide':3,'headerClassStructureManualCorrectionAllowed':False}
    return v

def main():
    v=report(ROOT);(ROOT/'protocol-report.json').write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n');print(json.dumps(v,ensure_ascii=False))
if __name__=='__main__':main()
