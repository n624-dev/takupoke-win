"""Frozen consumed-header suitability inference. No oracle access in this process."""
import base64
import hashlib
import json
from pathlib import Path
import resource
import time

import cv2
import numpy as np
import torch

from baseline import load
from framed import source_tensor
from image_tensor import greedy

REPO = Path('/workspace/scratch/takupoke-win')
PIXELS = REPO / 'tools/vision-ocr-comparison/fictional-pixels.json'
PIXELS_SHA = '6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff'
ASSETS = Path('/workspace/scratch/ascii-foundation-ctc-20261013-v1/consumed-headers')
RESULTS = REPO / 'research-results/ocr-synthetic-training-20261013/ascii-foundation-ctc-v1/consumed-headers'
ORDINALS = [f'n{i:03d}' for i in [*range(72, 96), *range(168, 192)]]


def main():
    assert hashlib.sha256(PIXELS.read_bytes()).hexdigest() == PIXELS_SHA
    ASSETS.mkdir(parents=True, exist_ok=True)
    RESULTS.mkdir(parents=True, exist_ok=True)
    raw = RESULTS / 'unjudged.jsonl'
    if raw.exists():
        raise ValueError('Fixed consumed-header inference already started; no overwrite')
    packet = json.loads(PIXELS.read_text(encoding='utf-8'))
    lookup = {row['id']: row for row in packet['rows']}
    torch.set_num_threads(2)
    torch.set_num_interop_threads(1)
    model, dictionary, model_receipt = load()
    freeze = {'scope': 'Parent-requested fixed ordinals from consumed development, suitability only; no training/final/activation credit', 'ordinals': ORDINALS, 'pixelsSha256': PIXELS_SHA, 'model': model_receipt, 'dictionary': dictionary, 'threads': 2, 'condition': 'English G2 FP32 full official97 dictionary/greedy/first pass; all RGB!=255 ink bbox+2originalpx; Gray64 PILbicubic, official64-multiple width, last-column padding; unchanged research acceptance minpiece>=.8 and nonempty printableASCII and no padding intervals', 'sources': {name: hashlib.sha256((Path(__file__).parent / name).read_bytes()).hexdigest() for name in ['baseline.py', 'framed.py', 'header_infer.py']}}
    (RESULTS / 'freeze.json').write_text(json.dumps(freeze, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
    started = time.monotonic()
    timings = []
    with raw.open('x', encoding='utf-8', newline='\n') as stream, torch.inference_mode():
        for ordinal in ORDINALS:
            row = lookup[ordinal]
            png = base64.b64decode(row['pngBase64'], validate=True)
            assert hashlib.sha256(png).hexdigest() == row['pngSha256']
            colour = cv2.imdecode(np.frombuffer(png, dtype=np.uint8), cv2.IMREAD_COLOR)
            assert colour.shape[:2] == (row['height'], row['width'])
            bgra = np.concatenate([colour, np.full((*colour.shape[:2], 1), 255, dtype=np.uint8)], axis=2)
            assert hashlib.sha256(bgra.tobytes()).hexdigest() == row['bgraSha256']
            path = ASSETS / (ordinal + '.png')
            path.write_bytes(png)
            array, valid, source = source_tensor(path)
            timer = time.monotonic()
            probabilities = model(torch.from_numpy(array), None).softmax(-1)[0].numpy()
            milliseconds = (time.monotonic() - timer) * 1000
            timings.append(milliseconds)
            pieces = greedy(probabilities, dictionary)
            prediction = ''.join(piece['Text'] for piece in pieces)
            minimum = min((piece['Confidence'] for piece in pieces), default=0)
            left, top, right, bottom = source['crop']
            input_width, time_steps = array.shape[-1], probabilities.shape[0]
            source_boxes = []
            for piece in pieces:
                column_start = piece['Start'] * input_width / time_steps
                column_end = piece['End'] * input_width / time_steps
                supported = column_end <= valid
                source_boxes.append({'text': piece['Text'], 'columnStart': column_start, 'columnEnd': column_end, 'supported': supported, 'left': left + column_start * (right-left) / valid, 'right': left + column_end * (right-left) / valid, 'top': top, 'bottom': bottom, 'mapping': 'Unclamped CTC extent; not independent original-glyph/cell-ownership proof'})
            supported = all(box['supported'] for box in source_boxes)
            indices = probabilities.argmax(-1)
            nonblank = probabilities.max(-1)[indices != 0]
            official_confidence = float(np.prod(nonblank) ** (2 / np.sqrt(len(nonblank)))) if len(nonblank) else 0
            record = {'id': ordinal, 'pngSha256': row['pngSha256'], 'bgraSha256': row['bgraSha256'], 'prediction': prediction, 'pieces': pieces, 'minimumConfidence': minimum, 'officialSequenceConfidence': official_confidence, 'accepted': bool(prediction) and all(32 <= ord(c) <= 126 for c in prediction) and minimum >= .8 and supported, 'supported': supported, 'source': source, 'sourceBoxes': source_boxes, 'inputWidth': input_width, 'validWidth': valid, 'paddingColumns': input_width-valid, 'timeSteps': time_steps, 'inferenceMilliseconds': milliseconds}
            stream.write(json.dumps(record, ensure_ascii=False) + '\n')
            stream.flush()
    receipt = {'completed': len(ORDINALS), 'rawSha256': hashlib.sha256(raw.read_bytes()).hexdigest(), 'elapsedSeconds': time.monotonic()-started, 'medianInferenceMilliseconds': float(np.median(timings)), 'peakRssMiB': resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024, 'oracleRead': False, 'scope': freeze['scope']}
    (RESULTS / 'inference-complete.json').write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(receipt))


if __name__ == '__main__':
    main()
