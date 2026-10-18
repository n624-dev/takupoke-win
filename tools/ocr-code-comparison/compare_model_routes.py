"""Compare fixed model bytes through both actual application code routes."""
import argparse
import hashlib
import json
from pathlib import Path
from run import distance, write


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--before', type=Path, required=True)
    parser.add_argument('--after', type=Path, required=True)
    args = parser.parse_args()
    old, new = args.before, args.after
    assert sha(old/'oracle.json') == sha(new/'oracle.json')
    first, second = [json.loads((folder/'inputs.json').read_text()) for folder in [old, new]]
    assert first['rows'] == second['rows']
    previous_receipt = json.loads((old/'worker-receipt.json').read_text())
    receipt = json.loads((new/'receipt.json').read_text())
    assert previous_receipt['exitCode'] == receipt['exitCode'] == 0
    assert sha(old/'raw.jsonl') == previous_receipt['raw']['sha256']
    assert sha(new/'raw.jsonl') == receipt['rawSHA256']
    records = []
    for folder in [old, new]:
        rows = [json.loads(line) for line in (folder/'raw.jsonl').read_text().splitlines()]
        lookup = {(row['id'], row['model']): row for row in rows}
        assert len(lookup) == len(rows)
        records.append(lookup)
    oracle = json.loads((old/'oracle.json').read_text())
    assert len(records[0]) == len(records[1]) == len(oracle)*5
    below = lambda row: any(piece['Confidence'] < .8 for piece in row['pieces'])
    results, changes = [], []
    for route in ['actual-code-route', 'actual-manual-code-route', 'deployed-trained-english-v5']:
        for margin in [2, 6]:
            items = [(g, records[0][g['id'], route], records[1][g['id'], route]) for g in oracle if g['margin'] == margin]
            def score(index):
                return dict(exact=sum(row[index]['text'] == row[0]['value'] for row in items),
                    edits=sum(distance(row[0]['value'], row[index]['text']) for row in items),
                    belowNativeFloor=sum(below(row[index]) for row in items),
                    wrongAboveNativeFloor=sum(not below(row[index]) and row[index]['text'] != row[0]['value'] for row in items),
                    ordinaryExact=sum(row[index]['text'] == row[0]['value'] for row in items if row[0]['group'] == 'ordinary'))
            results.append(dict(route=route, margin=margin, rows=len(items), characters=sum(len(g['value']) for g, _, _ in items),
                before=score(1), after=score(2),
                gains=sum(a['text'] != g['value'] and b['text'] == g['value'] for g, a, b in items),
                losses=sum(a['text'] == g['value'] and b['text'] != g['value'] for g, a, b in items),
                addedPixelReview=sum(below(b) and not below(a) for _, a, b in items),
                removedPixelReview=sum(below(a) and not below(b) for _, a, b in items)))
            if margin == 2:
                changes.extend(dict(route=route, id=g['id'], expected=g['value'], before=a['text'], after=b['text'],
                    font=g['font'], size=g['size'], beforePieces=a['pieces'], afterPieces=b['pieces'])
                    for g, a, b in items if a['text'] != b['text'])
    for g in oracle:
        key = (g['id'], 'japanese-v6')
        assert all(records[0][key][field] == records[1][key][field] for field in ['text', 'pieces', 'paddingSupported'])
    report = dict(scores=results, unchangedJapaneseObservations=len(oracle),
        blankOutputsRemainEmpty=all(not record[g['id'], route]['text'] for record in records for g in oracle
            if g['group'] == 'blank' for route in ['actual-code-route', 'actual-manual-code-route']),
        peakWorkingSetBytes=max(row['peakWorkingSetBytes'] for record in records for row in record.values()),
        rawSHA256=sha(new/'raw.jsonl'), baselineSHA256=sha(old/'raw.jsonl'),
        scope='Same isolated fictional images previously used for manual-route assessment, with checkpoint selected on different fonts before this comparison. No new-image/new-layout credit; separate fresh-font model evaluation exists. No user-review-time or full-document claim.')
    write(new/'paired-model-routes.json', report)
    write(new/'model-route-changes.json', changes)
    print(json.dumps(report, ensure_ascii=False))


if __name__ == '__main__':
    main()
