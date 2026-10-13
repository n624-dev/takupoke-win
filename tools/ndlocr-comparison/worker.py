#!/usr/bin/env python3
"""Native NDLOCR-Lite recognizers. Receives pixels and artifacts, never an oracle."""
import argparse
import base64
import hashlib
import io
import json
from pathlib import Path
import sys
import time

import numpy as np
import onnxruntime as ort
from PIL import Image
import yaml


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--upstream', type=Path, required=True)
    p.add_argument('--freeze', type=Path, required=True)
    p.add_argument('--pixels', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    freeze = json.loads(args.freeze.read_text(encoding='utf-8'))
    if sha(args.pixels.read_bytes()) != freeze['pixelsSHA']:
        raise ValueError('Immutable pixel packet changed')
    for relative, expected in freeze['upstreamSources'].items():
        if sha((args.upstream / relative).read_bytes()) != expected:
            raise ValueError('Upstream source identity changed')
    sys.path.insert(0, str(args.upstream / 'src'))
    from parseq import PARSEQ
    ort.disable_telemetry_events()
    chars = list(yaml.safe_load((args.upstream / 'src/config/NDLmoji.yaml').read_text(encoding='utf-8'))['model']['charset_train'])
    if len(chars) != 7141 or len(set(chars)) != len(chars):
        raise ValueError('Native alphabet changed')
    packet = json.loads(args.pixels.read_text(encoding='utf-8'))
    if len(packet['rows']) != 192 or len({r['id'] for r in packet['rows']}) != 192:
        raise ValueError('Frozen corpus changed')
    started = time.monotonic()
    completed = 0
    with args.output.open('x', encoding='utf-8', newline='\n') as f:
        for model in freeze['models']:
            path = args.upstream / 'src/model' / model['filename']
            if path.stat().st_size != model['bytes'] or sha(path.read_bytes()) != model['sha256']:
                raise ValueError('Pinned model identity failed')
            reader = PARSEQ(str(path), chars, device='CPU')
            if reader.session.get_providers() != ['CPUExecutionProvider']:
                raise ValueError('CPU provider required')
            if reader.input_shape != [1, 3, 24, model['width']]:
                raise ValueError('Native input shape changed')
            for row in packet['rows']:
                if time.monotonic() - started > 1800:
                    raise TimeoutError('Whole comparison deadline')
                png = base64.b64decode(row['pngBase64'], validate=True)
                if sha(png) != row['pngSha256']:
                    raise ValueError('Source PNG identity changed')
                with Image.open(io.BytesIO(png)) as image:
                    if image.size != (row['width'], row['height']):
                        raise ValueError('Source geometry changed')
                    rgb = np.array(image.convert('RGB'))
                # Exactly upstream's input: PIL RGB, then native PARSEQ's
                # orientation, cv2 bilinear resize, channel reversal and [-1,1].
                tensor = reader.preprocess(rgb)
                watch = time.monotonic()
                logits = reader.session.run(reader.output_names, {reader.input_names[0]: tensor})[0]
                seconds = time.monotonic() - watch
                if logits.shape != (1, model['maximumCharacters'] + 1, 7142) or not np.isfinite(logits).all():
                    raise ValueError('Native logits invalid')
                indices = np.argmax(logits[0], axis=1)
                stop = np.where(indices == 0)[0]
                end = int(stop[0]) if stop.size else len(indices)
                tokens = indices[:end].tolist()
                text = ''.join(chars[i - 1] for i in tokens)
                # PARSeq emits logits, not Paddle CTC probabilities. This
                # diagnostic includes EOS, has no production confidence floor,
                # and is never used to create CTC positions or accepted evidence.
                shifted = logits[0] - np.max(logits[0], axis=1, keepdims=True)
                probabilities = np.exp(shifted)
                probabilities /= probabilities.sum(axis=1, keepdims=True)
                count = end + 1 if stop.size else end
                confidence = probabilities[np.arange(count), indices[:count]].tolist()
                record = {'id': row['id'], 'model': model['id'], 'pngSHA': row['pngSha256'],
                          'text': text, 'indicesIncludingEos': indices[:count].tolist(),
                          'sequenceTokenProbabilitiesIncludingEos': confidence,
                          'minimumTokenProbabilityIncludingEos': min(confidence) if confidence else None,
                          'eosObserved': bool(stop.size), 'inferenceSeconds': seconds,
                          'tensorSHA': sha(tensor.tobytes()), 'outputLogitsSHA': sha(logits.tobytes()),
                          'outputShape': list(logits.shape), 'logitsMinimum': float(logits.min()),
                          'logitsMaximum': float(logits.max()), 'rotationApplied': rgb.shape[0] > rgb.shape[1] * .8}
                f.write(json.dumps(record, ensure_ascii=False, separators=(',', ':')) + '\n')
                f.flush()
                completed += 1
                if completed % 96 == 0:
                    print(json.dumps({'callsCompleted': completed, 'callsPlanned': freeze['callsPlanned']}), flush=True)
            del reader
    if completed != freeze['callsPlanned']:
        raise ValueError('Unassessed calls remain')
    print(json.dumps({'callsCompleted': completed, 'seconds': time.monotonic() - started,
                      'qualification': 'unassessed', 'fullDocuments': 0, 'adoptions': 0}), flush=True)


if __name__ == '__main__':
    main()
