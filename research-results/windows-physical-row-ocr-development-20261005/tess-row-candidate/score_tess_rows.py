"""Offline original-literal diagnostics; never consumed by native OCR or host input."""
import hashlib
import json
from pathlib import Path

D = Path(__file__).resolve().parent
F = Path('/workspace/recovery-research/ocr-unlabeled-fresh-source-high-20261004')

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def inside(a, b):
    return b[0] <= a[0] and b[1] <= a[1] and a[2] <= b[2] and a[3] <= b[3]

records = [json.loads(line) for line in (D / 'native-raw.log').read_text(encoding='utf-8').splitlines() if line.startswith('{')]
final = next(r for r in records if r['type'] == 'result')
rows = [r for r in records if r['type'] == 'row-result']
input_rows = json.loads((D.parent / 'row-crop-candidate/rows-before-native.json').read_text())['rows']
row_boxes = {r['id']: r['box'] for r in input_rows}
gold = json.loads((F / 'drawing-glyph-preflight.json').read_text())
oracle = json.loads((F / 'literal-formal-oracle.json').read_text())
units = []
for u in final['result']['json_lines']:
    xy = u['boundingBox']
    box = [min(p[0] for p in xy), min(p[1] for p in xy), max(p[0] for p in xy), max(p[1] for p in xy)]
    units.append({'nativeID': u['id'], 'originalPixelRowID': u['originalPixelRowID'], 'text': u['text'], 'box': box,
                  'center': [(box[0]+box[2])/2, (box[1]+box[3])/2], 'nativeWordConfidence': u['nativeWordConfidence'],
                  'insideOriginalRow': inside(box, row_boxes[u['originalPixelRowID']])})
targets = []
for t in gold['paint']:
    box = t['semanticBox']
    assigned = [u for u in units if box[0] <= u['center'][0] < box[2] and box[1] <= u['center'][1] < box[3]]
    # Preserve native IDs/order; neither text selection nor geometric sorting repairs OCR.
    text = ''.join(u['text'] for u in assigned)
    contained = bool(assigned) and all(inside(u['box'], box) and u['insideOriginalRow'] for u in assigned)
    associated_rows = [r for r in rows if any(u['originalPixelRowID'] == r['rowID'] for u in assigned)]
    raw_texts = [r['nativeOutputs']['text'] for r in associated_rows]
    symbols = [s for r in associated_rows for s in r['nativeSymbols']]
    symbol_text = ''.join(s['text'] for s in symbols)
    targets.append({'id': t['id'], 'kind': t['kind'], 'className': t.get('className'), 'day': t.get('day'),
                    'period': t.get('period'), 'role': t.get('role'), 'literal': t['literal'],
                    'originalSemanticBand': box, 'originalPrintedInkBox': t['actualInkBox'], 'nativeWords': assigned,
                    'nativeWordOrderConcatDiagnostic': text, 'rawConcatExact': text == t['literal'],
                    'oneCandidateRawExact': any(u['text'] == t['literal'] for u in assigned),
                    'allAssignedActualBoxesContained': contained, 'strictLiteralAndPosition': text == t['literal'] and contained,
                    'rawNativeText': raw_texts, 'nativeTextWhitespaceRemovedExactDiagnostic': ''.join(''.join(x.split()) for x in raw_texts) == t['literal'],
                    'nativeBOXSymbols': symbols, 'nativeSymbolConcatExactDiagnostic': symbol_text == t['literal'],
                    'allBOXSymbolsInsideOriginalRows': bool(symbols) and all(s['insideOriginalRow'] for s in symbols),
                    'failureStage': 'missing-word-output' if not assigned else 'native-transcription-difference' if text != t['literal'] else 'native-box-crossing' if not contained else 'local-literal-position-exact',
                    'causalStrength': 'confirmed raw output difference; intrinsic model capacity undetermined'})
tuples = []
for lesson in oracle['lessons']:
    fields = [t for t in targets if t['kind'] == 'body' and t['day'] == lesson['day'] and t['period'] == lesson['period']]
    tuples.append({'className': lesson['className'], 'day': lesson['day'], 'period': lesson['period'],
                   'fieldTargets': [t['id'] for t in fields], 'rawThreeValuesExact': len(fields) == 3 and all(t['rawConcatExact'] for t in fields),
                   'threeValuePositionsExact': len(fields) == 3 and all(t['strictLiteralAndPosition'] for t in fields),
                   'fullTupleExact': None, 'reason': 'offline body diagnostics do not establish original headers, role ownership, coverage, acquisition confidence or host formal acceptance'})
body = [t for t in targets if t['kind'] == 'body']; headers = [t for t in targets if t['kind'] != 'body']
counts = {'plannedRows': 170, 'nativeRecognizeReturnedRows': final['nativeRecognizeReturnedRows'],
          'serializationCompletedRows': final['serializationCompletedRows'], 'runtimeErrors': len(final['runtimeErrors']),
          'nativeWordCount': len(units), 'emptyNativeWordCount': sum(u['text'] == '' for u in units),
          'nativeWordsOutsideOriginalRows': sum(not u['insideOriginalRow'] for u in units),
          'nativeBOXSymbolCount': len(final['nativeSymbols']), 'nativeBOXSymbolsOutsideOriginalRows': sum(not s['insideOriginalRow'] for s in final['nativeSymbols']),
          'bodyRawLiteralExact': sum(t['rawConcatExact'] for t in body), 'bodyLiteralAndPositionExact': sum(t['strictLiteralAndPosition'] for t in body),
          'headerRawLiteralExact': sum(t['rawConcatExact'] for t in headers), 'headerLiteralAndPositionExact': sum(t['strictLiteralAndPosition'] for t in headers),
          'bodyTargets': len(body), 'headerTargets': len(headers), 'rawThreeValuesExact': sum(t['rawThreeValuesExact'] for t in tuples),
          'threeValuePositionsExact': sum(t['threeValuePositionsExact'] for t in tuples), 'formalExact': None}
out = {'recipe': 'tess-uniform-original170-pixel-rows-v1', 'rawSHA256': sha(D / 'native-raw.log'),
       'sourceGlyphSHA256': sha(F / 'drawing-glyph-preflight.json'), 'formalOracleSHA256': sha(F / 'literal-formal-oracle.json'),
       'counts': counts, 'targets': targets, 'tuples': tuples, 'noOracleFedToNative': True,
       'scope': 'One consumed invented development image. Actual native TSV words primary; native text/BOX diagnostic only. Confidence not calibrated. No word repair, reordering, inferred characters or automatic best selection.'}
(D / 'per-target-tess-results.json').write_text(json.dumps(out, ensure_ascii=False, indent=2)+'\n')
print(json.dumps(counts, ensure_ascii=False))
