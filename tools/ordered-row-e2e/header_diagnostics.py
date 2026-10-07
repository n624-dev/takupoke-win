"""Assertion-only source-position trace, evaluated after the native child exits.

This is not recognition, field ownership, or an adoption oracle. Original span
bounds are inspection aids: unmatched/overlapping centers stay unclassified.
"""
from collections import Counter
import hashlib
import re


def classify(sources, traces, spans):
    ids = [s['Id'] for s in sources]
    assert len(set(ids)) == len(ids), 'Native source IDs must not change or repeat'
    acquired = {i: [] for i in range(len(spans))}
    low = Counter(header=0, body=0, unclassified=0)
    unclassified = 0
    for source in sources:
        g = source['Glyph']
        cx, cy = g['X'] + g['Width']/2, g['Y'] + g['Height']/2
        owners = [i for i, span in enumerate(spans) if span['page'] == source['Page']
                  and span['box'][0] <= cx <= span['box'][2]
                  and span['box'][1] <= cy <= span['box'][3]]
        if len(owners) != 1:
            unclassified += 1
            role = 'unclassified'
        else:
            owner = owners[0]
            acquired[owner].append(source)
            role = 'body' if spans[owner]['body'] else 'header'
        if source['Confidence'] < .8:
            low[role] += 1
    labels = [label for trace in traces if trace['Stage'] == 'extracted-labels' for label in trace['Labels']]
    semantic = [label for trace in traces if trace['Stage'] == 'semantic-headers' for label in trace['Labels']]
    cells = [cell for trace in traces if trace['Stage'] == 'built-cells' for cell in trace['Cells']]
    cell_ids = {sid for cell in cells for sid in cell['SourceIds']}
    header_ids = {sid for cell in cells for sid in cell['HeaderIds']}
    rows, body_exact, body_covered, low_body_fields = [], 0, 0, 0
    for i, span in enumerate(spans):
        native = acquired[i]  # preserve actual native array order
        owned_ids = {s['Id'] for s in native}
        reconstructed = ''.join(s['Glyph']['Text'] for s in native)
        if span['body']:
            body_covered += bool(native)
            body_exact += reconstructed == span['text']
            low_body_fields += any(s['Confidence'] < .8 for s in native)
            continue
        rows.append({'page': span['page'], 'expected': span['text'], 'nativeText': reconstructed,
                     'sourceIds': [s['Id'] for s in native],
                     'minimumConfidence': min((s['Confidence'] for s in native), default=None),
                     'extracted': [{'value': l['Value'], 'ids': l['Ids']} for l in labels if owned_ids.intersection(l['Ids'])],
                     'semantic': [{'role': l['Role'], 'value': l['Value'], 'ids': l['Ids']} for l in semantic if owned_ids.intersection(l['Ids'])],
                     'usedByCellHeader': sorted(owned_ids & header_ids)})
    missing = [r for r in rows if r['nativeText'] != r['expected'] or not r['semantic']
               and re.fullmatch(r'[1-8]|[月火水木金]|[1-5]_(?:[1-3]|CN|ES|IT)|AI_[12]', r['expected'])]
    return {'nonAdoptable': True, 'association': 'original-painted-span center only; not field ownership',
            'nativeSourceCount': len(sources), 'unclassifiedNativeSources': unclassified,
            'lowConfidenceNativeSources': dict(low), 'lowConfidenceBodySpanCount': low_body_fields,
            'expectedBodySpans': sum(s['body'] for s in spans), 'bodySpansWithNativeSources': body_covered,
            'bodyLiteralReconstructionExact': body_exact,
            'nativeSourceIdsUsedInCells': len(cell_ids), 'nativeSourceIdsUsedInHeaders': len(header_ids),
            'builtCells': len(cells), 'builtSlots': sum(len(c['Slots']) for c in cells),
            'semanticValues': {role: sorted({l['Value'] for l in semantic if l['Role'] == role}) for role in ('class', 'day', 'period')},
            'headerSpanCount': len(rows), 'headerLiteralExact': sum(r['nativeText'] == r['expected'] for r in rows),
            'headerInspectionCount': len(missing), 'headerInspection': missing[:80], 'inspectionTruncated': len(missing) > 80}


def inspect(fixtures, case, actual):
    import fitz
    path = fixtures / case['file'].replace('-image.pdf', '.pdf')
    assert hashlib.sha256(path.read_bytes()).hexdigest() == case['sourcePdfSha256']
    spans = []
    with fitz.open(path) as pdf:
        assert len(pdf) == len(case['embeddedPages'])
        for page_index, page in enumerate(pdf):
            dimensions = case['embeddedPages'][page_index]
            sx, sy = dimensions['width']/page.rect.width, dimensions['height']/page.rect.height
            for block in page.get_text('dict')['blocks']:
                for line in block.get('lines', []):
                    for span in line['spans']:
                        x1, y1, x2, y2 = span['bbox']
                        spans.append({'page': page_index+1, 'text': span['text'],
                                      'body': bool(re.fullmatch(r'架空(?:科目|教員|室)\d+', span['text'])),
                                      'box': [x1*sx, y1*sy, x2*sx, y2*sy]})
    return classify(actual.pop('nativeSources'), actual.pop('builderTrace'), spans)
