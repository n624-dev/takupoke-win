#!/usr/bin/env python3
"""Check official Torch checkpoint vs ONNX before any fine-tuning or oracle use."""
import argparse
import base64
import hashlib
import io
import json
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image
import torch
from transformers import PPOCRV6SmallRecForTextRecognition
import yaml

from image_tensor import tensor, greedy


def main():
    p=argparse.ArgumentParser();p.add_argument('--checkpoint',required=True,type=Path)
    p.add_argument('--onnx',required=True,type=Path);p.add_argument('--pixels',required=True,type=Path)
    p.add_argument('--output',required=True,type=Path);args=p.parse_args()
    if args.output.exists():raise ValueError('Parity receipt must be new')
    torch.set_num_threads(2);torch.set_num_interop_threads(1)
    model=PPOCRV6SmallRecForTextRecognition.from_pretrained(args.checkpoint,local_files_only=True).eval()
    options=ort.SessionOptions();options.intra_op_num_threads=2;options.inter_op_num_threads=1
    session=ort.InferenceSession(str(args.onnx),sess_options=options,providers=['CPUExecutionProvider'])
    dictionary=['']+yaml.safe_load((args.checkpoint/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' ']
    records=[]
    for row in json.loads(args.pixels.read_text(encoding='utf-8'))['rows']:
        pixels=base64.b64decode(row['pngBase64'],validate=True)
        if hashlib.sha256(pixels).hexdigest()!=row['pngSha256']:raise ValueError('Pixel identity mismatch')
        array,valid=tensor(Image.open(io.BytesIO(pixels)))
        with torch.inference_mode():actual=model(pixel_values=torch.from_numpy(array[None])).last_hidden_state.numpy()[0]
        reference=session.run(None,{'x':array[None]})[0][0]
        left=greedy(actual,dictionary);right=greedy(reference,dictionary)
        def outcome(pieces):return [{k:p[k] for k in ('Text','Start','End')} for p in pieces]
        records.append({'id':row['id'],'validWidth':valid,'inputWidth':array.shape[-1],
            'onnx':right,'torch':left,'decoderOutcomeExact':outcome(left)==outcome(right),
            'maximumProbabilityDifference':float(np.max(np.abs(actual-reference)))})
    receipt={'purpose':'Initialization parity only; consumed pixels; no oracle, training, qualification or adoption',
        'rows':len(records),'exactDecoderOutcomes':sum(r['decoderOutcomeExact'] for r in records),
        'modelParameters':sum(p.numel() for p in model.parameters()),'records':records}
    args.output.write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n')
    print({k:v for k,v in receipt.items() if k!='records'})
    if receipt['exactDecoderOutcomes']!=len(records):raise ValueError('Checkpoint conversion differs; do not silently replace baseline')


if __name__=='__main__':main()
