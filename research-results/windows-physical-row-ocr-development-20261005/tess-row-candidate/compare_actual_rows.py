"""Posthoc same-input causes; no native calls or replacement of evidence."""
import json
import hashlib
from pathlib import Path

D = Path(__file__).resolve().parent
P = D.parent / 'row-crop-candidate'

def read(path):
    return [json.loads(x) for x in path.read_text(encoding='utf-8').splitlines() if x.startswith('{')]

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

tess = {r['rowID']: r for r in read(D / 'native-raw.log') if r['type'] == 'row-result'}
parseq = {arm: {r['rowID']: r for r in read(P / ('model-'+arm+'-raw.log')) if r['type'] == 'row-result'} for arm in ['30','100']}
inputs = json.loads((P / 'rows-before-native.json').read_text())['rows']
score = json.loads((D / 'per-target-tess-results.json').read_text())
rows = []
for original in inputs:
    rid = original['id']; t = tess[rid]
    p30 = parseq['30'][rid]; p100 = parseq['100'][rid]
    assert t['originalInputRGBSHA256'] == p30['actualInputRGBSHA256'] == p100['actualInputRGBSHA256'] == original['originalRGBSHA256']
    targets = [g for g in score['targets'] if any(u['originalPixelRowID'] == rid for u in g['nativeWords'])]
    words = t['nativeWords']; text = ''.join(u['text'] for u in words)
    known = len(targets) == 1
    literal = targets[0]['literal'] if known else None
    strengths = [{'finding': 'same original RGB input across all three arms', 'strength': 'confirmed', 'evidence': original['originalRGBSHA256']},
                 {'finding': 'Tesseract and PARSeq settings, preprocessing, engine and model differ; model-only causation is not isolated', 'strength': 'confirmed'},
                 {'finding': 'tight crop context, jpn_vert dependency warning or intrinsic capacity caused this row difference', 'strength': 'undetermined'}]
    if known and text != literal:
        strengths.append({'finding': 'returned native transcription differs from original literal', 'strength': 'confirmed', 'evidence': {'literal': literal, 'actualNativeWordConcat': text}})
    if known and p100['unit']['text'] == literal and text != literal:
        strengths.append({'finding': 'this source is transcribable by another measured pipeline under its fixed recipe', 'strength': 'supported', 'evidence': {'PARSeq100': p100['unit']['text']}})
    rows.append({'rowID': rid, 'originalRGBSHA256': original['originalRGBSHA256'], 'originalPixelSupportBox': original['box'],
                 'offlineTargetIDs': [g['id'] for g in targets], 'offlineLiteral': literal,
                 'TessNativeReadReturned': t['nativeRecognizeReturned'], 'TessSerializationCompleted': t['serializationCompleted'],
                 'TessNativeTextExact': t['nativeOutputs']['text'], 'TessNativeWords': words, 'TessNativeSymbols': t['nativeSymbols'],
                 'TessNativeWordOrderConcat': text, 'PARSeq30': p30['unit']['text'], 'PARSeq100': p100['unit']['text'],
                 'TessLiteralExact': text == literal if known else None,
                 'failureStage': 'returned-transcription' if known and text != literal else 'local-literal-match' if known else 'offline-association-unresolved',
                 'causes': strengths})
out = {'scope': 'One consumed invented image; same original 170 RGB rows. Per-row causes are posthoc diagnostics, not adopted model selection or qualified formal recovery.',
       'rawSHA256': {'Tess': sha(D / 'native-raw.log'), **{a: sha(P / ('model-'+a+'-raw.log')) for a in ['30','100']}},
       'runtimeErrors': 0, 'rows': rows,
       'nextStepDecision': 'Stop Tesseract context/language/PSM tuning on this control. Any future heading/body domain routing must be a fixed pixel-geometry contract with all outputs and fresh holdout evaluation, never best-of-output selection.',
       'warningScope': 'jpn_vert load warning is retained native initialization capability information, not 170 native runtime errors and not a proven explanation for every incorrect row.'}
(D / 'per-row-causes.json').write_text(json.dumps(out, ensure_ascii=False, indent=2)+'\n')
print(json.dumps({'rows': len(rows), 'allOriginalRGBIdentityVerified': True, 'raw': out['rawSHA256']}))
