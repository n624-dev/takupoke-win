"""Unchanged retained native-source inspection; no model-facing proof work."""
from source_contract import Refusal, inspect_source, _exact_keys

def checked_source(task):
    source = inspect_source(task)
    if task['promptInput']['coordinateScope'] != 'original pixels; CTC character intervals ESTIMATED, not ink boxes':
        raise Refusal('EXPLICIT_ESTIMATED_CTC_PROVENANCE_REQUIRED')
    if task['sourceProof'] != 'NO_PRODUCTION_ROLE_OR_WHOLE_DOCUMENT_CERTIFICATE; CANDIDATE_ONLY':
        raise Refusal('NO_UNVERIFIED_SOURCE_CERTIFICATE')
    if task['developmentConsumed'] is not True:
        raise Refusal('ONLY_CONSUMED_FICTIONAL_DEVELOPMENT')
    rows = task['acquisitionRows']
    if type(rows) is not list or any(type(r) is not dict for r in rows):
        raise Refusal('ACQUISITION_LINEAGE_MISSING')
    by_chunk = {}
    for row in rows:
        _exact_keys(row, ('id', 'rawText', 'currentSafetyFloorPassed', 'belowCurrentConfidenceFloor', 'assessed'), 'ACQUISITION_ROW_KEYS')
        if (type(row['id']) is not int or row['id'] in by_chunk or type(row['rawText']) is not str or
                row['assessed'] is not True or row['currentSafetyFloorPassed'] is not True or
                row['belowCurrentConfidenceFloor'] is not False):
            raise Refusal('ACQUISITION_ROW_UNASSESSED')
        by_chunk[row['id']] = row
    focal = [s for s in task['sources'] if s['owner'] == 'cell']
    for s in focal:
        if (s['fromOcr'] is not True or type(s['chunkID']) is not int or s['chunkID'] not in by_chunk or
                type(s['ctcStart']) is not int or
                type(s['ctcEnd']) is not int or not 0 <= s['ctcStart'] < s['ctcEnd']):
            raise Refusal('ACTUAL_RETAINED_CTC_LINEAGE_REQUIRED')
    if task['originalChunkIDs'] != list(by_chunk) or set(by_chunk) != {s['chunkID'] for s in focal}:
        raise Refusal('INCOMPLETE_ORIGINAL_CHUNK_INVENTORY')
    for chunk, row in by_chunk.items():
        if ''.join(s['text'] for s in focal if s['chunkID'] == chunk) != row['rawText']:
            raise Refusal('ORIGINAL_CHUNK_TEXT_REWRITE')
    return source

