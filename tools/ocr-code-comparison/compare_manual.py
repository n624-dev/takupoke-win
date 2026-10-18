"""Score only closed actual readers; keep losses and extra pixel-review burden."""
import argparse
import hashlib
import json
from pathlib import Path
from run import distance, write


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--study', type=Path, required=True)
    args = parser.parse_args()
    root = args.study
    freeze = json.loads((root/'freeze.json').read_text())
    for name in ['inputs', 'oracle', 'protocol']:
        assert sha(root/freeze[name]['file']) == freeze[name]['sha256']
    records = []
    for raw_name, receipt_name, hash_key in [
        ('raw.jsonl', 'worker-receipt.json', None),
        ('baseline-verified-raw.jsonl', 'baseline-verified-receipt.json', 'rawSHA256'),
    ]:
        receipt = json.loads((root/receipt_name).read_text())
        assert receipt['exitCode'] == 0, 'Native worker did not close successfully'
        expected = receipt[hash_key] if hash_key else receipt['raw']['sha256']
        assert sha(root/raw_name) == expected
        rows = [json.loads(line) for line in (root/raw_name).read_text().splitlines()]
        assert len({(row['id'], row['model']) for row in rows}) == len(rows)
        records.append({(row['id'], row['model']): row for row in rows})
    current, baseline = records
    oracle = json.loads((root/'oracle.json').read_text())
    baseline_route = 'actual-manual-code-route' if (oracle[0]['id'], 'actual-manual-code-route') in baseline else 'actual-code-route'
    assert len(current) == len(oracle)*5 and len(baseline) == len(oracle)*(5 if baseline_route == 'actual-manual-code-route' else 4)
    identities = ['text', 'pieces', 'paddingSupported', 'glyphs', 'recognizerCalls', 'ValidWidth', 'InputWidth']
    for item in oracle:
        key = (item['id'], 'actual-code-route')
        assert all(current[key][field] == baseline[key][field] for field in identities), 'Default route changed'
    reports = []
    examples = {'gains': [], 'losses': []}
    below = lambda row: any(piece['Confidence'] < .8 for piece in row['pieces'])
    for margin in [2, 6]:
        items = [(item, baseline[item['id'], baseline_route], current[item['id'], 'actual-manual-code-route'])
                 for item in oracle if item['margin'] == margin]
        def summarize(index):
            return dict(exact=sum(row[index]['text'] == row[0]['value'] for row in items),
                edits=sum(distance(row[0]['value'], row[index]['text']) for row in items),
                belowNativeFloor=sum(below(row[index]) for row in items),
                wrongAboveNativeFloor=sum(not below(row[index]) and row[index]['text'] != row[0]['value'] for row in items))
        gains = [(g, a, b) for g, a, b in items if a['text'] != g['value'] and b['text'] == g['value']]
        losses = [(g, a, b) for g, a, b in items if a['text'] == g['value'] and b['text'] != g['value']]
        reports.append(dict(margin=margin, images=len(items), characters=sum(len(g['value']) for g, _, _ in items),
            before=summarize(1), after=summarize(2), gains=len(gains), losses=len(losses),
            changed=sum(a['text'] != b['text'] for _, a, b in items),
            addedPixelReview=sum(below(b) and not below(a) for _, a, b in items),
            removedPixelReview=sum(below(a) and not below(b) for _, a, b in items),
            ordinaryBefore=sum(a['text'] == g['value'] for g, a, b in items if g['group'] == 'ordinary'),
            ordinaryAfter=sum(b['text'] == g['value'] for g, a, b in items if g['group'] == 'ordinary')))
        if margin == 2:
            for label, changes in [('gains', gains), ('losses', losses)]:
                examples[label] = [dict(id=g['id'], expected=g['value'], before=a['text'], after=b['text'],
                    font=g['font'], size=g['size'], beforePieces=a['pieces'], afterPieces=b['pieces']) for g, a, b in changes]
    blank = [item for item in oracle if item['group'] == 'blank']
    report = dict(scores=reports, baselineRoute=baseline_route, defaultRouteIdentityImages=len(oracle),
        blankImages=len(blank), blankOutputsRemainEmpty=all(
            not current[item['id'], 'actual-manual-code-route']['text'] and not baseline[item['id'], 'actual-code-route']['text'] for item in blank),
        peakWorkingSetBytes=max(row['peakWorkingSetBytes'] for rows in records for row in rows.values()),
        rawSHA256=sha(root/'raw.jsonl'), baselineSHA256=sha(root/'baseline-verified-raw.jsonl'),
        scope='Fresh fictional strings in observed fonts, isolated recognition only. Extra low-score images need pixel review; these 384 images are not one document and are not compared to the 128-field document cap. No human correction-time or full-document qualification.')
    write(root/'paired-manual-routes.json', report)
    write(root/'manual-route-examples.json', examples)
    print(json.dumps(report, ensure_ascii=False))


if __name__ == '__main__':
    main()
