"""Read-only native-confidence and case-sensitive header contrast report."""
import hashlib
import json
from pathlib import Path

RESULTS = Path('/workspace/scratch/takupoke-win/research-results/ocr-synthetic-training-20261013/ascii-foundation-ctc-v1/consumed-headers')


def main():
    judged = json.loads((RESULTS/'judged.json').read_text(encoding='utf-8'))
    raw = [json.loads(line) for line in (RESULTS/'unjudged.jsonl').read_text(encoding='utf-8').splitlines()]
    records = {row['id']:row for row in raw}
    header = [row for row in judged if row['literal']]
    native_accepted = [row for row in header if row['prediction'] and row['supported'] and all(32 <= ord(c) <= 126 for c in row['prediction']) and row['officialSequenceConfidence'] >= .8]
    result = {'scope': 'Post-inference consumed-development diagnosis; preserves original outputs and experimental acceptance. No threshold selection or calibration.', 'nativeSequenceConfidenceAtFixedDescriptiveFloor08': {'headers':len(header), 'correct':sum(row['exact'] for row in native_accepted), 'incorrect':sum(not row['exact'] for row in native_accepted), 'maximumWrongConfidence':max(row['officialSequenceConfidence'] for row in header if not row['exact'])}, 'pairedHeaders': [row for row in judged if row['literal'] in ['AI_1','AI_2','Al_1','Al_2','A1_1','A1_2']], 'rawSha256':hashlib.sha256((RESULTS/'unjudged.jsonl').read_bytes()).hexdigest(), 'sourceBoxLimits':'Offsets and unchanged width/validWidth/timeSteps retained per row. Float source boxes are unclamped CTC estimates, not original glyph or cell ownership proof.'}
    assert len(result['pairedHeaders']) == 12
    (RESULTS/'native-confidence-diagnosis.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(result,ensure_ascii=False))


if __name__ == '__main__':
    main()
