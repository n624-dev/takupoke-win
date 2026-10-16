#!/usr/bin/env python3
"""Offline CPU GOT-OCR inference on frozen ordinal images, without answers."""
import argparse
import base64
import hashlib
import io
import json
from pathlib import Path
import resource
import time

from PIL import Image
import torch
from transformers import AutoModelForImageTextToText, AutoProcessor


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',type=Path,required=True)
    p.add_argument('--pixels',type=Path,required=True);p.add_argument('--freeze',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True);args=p.parse_args()
    freeze=json.loads(args.freeze.read_text(encoding='utf-8'))
    if sha(args.pixels)!=freeze['pixelsSHA']:raise ValueError('Pixel packet changed')
    for item in freeze['modelFiles']:
        file=args.assets/item['file']
        if file.stat().st_size!=item['bytes'] or sha(file)!=item['sha256']:raise ValueError('Pinned artifact changed')
    torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.manual_seed(17)
    model=AutoModelForImageTextToText.from_pretrained(str(args.assets),local_files_only=True,
               torch_dtype=torch.float32,attn_implementation='eager').eval().to('cpu')
    processor=AutoProcessor.from_pretrained(str(args.assets),local_files_only=True,use_fast=False)
    packet=json.loads(args.pixels.read_text(encoding='utf-8'))
    if len(packet['rows'])!=192 or len({r['id'] for r in packet['rows']})!=192:raise ValueError('Frozen count changed')
    started=time.monotonic();completed=0
    with args.output.open('x',encoding='utf-8',newline='\n') as f,torch.inference_mode():
        for row in packet['rows']:
            if time.monotonic()-started>5100:raise TimeoutError('Global inference deadline')
            data=base64.b64decode(row['pngBase64'],validate=True)
            if hashlib.sha256(data).hexdigest()!=row['pngSha256']:raise ValueError('PNG changed')
            with Image.open(io.BytesIO(data)) as image:
                if image.size!=(row['width'],row['height']):raise ValueError('Geometry changed')
                image=image.convert('RGB')
                inputs=processor(image,return_tensors='pt',format=False)
            watch=time.monotonic()
            generated=model.generate(**inputs,do_sample=False,tokenizer=processor.tokenizer,
                     stop_strings='<|im_end|>',max_new_tokens=128,use_cache=True)
            seconds=time.monotonic()-watch
            ids=generated[0,inputs['input_ids'].shape[1]:].tolist()
            text=processor.decode(ids,skip_special_tokens=True)
            peak=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss*1024
            record={'id':row['id'],'model':'got-ocr2-fp32-cpu','pngSHA':row['pngSha256'],'text':text,
                    'generatedTokenIds':ids,'decodedWithSpecialTokens':processor.decode(ids,skip_special_tokens=False),
                    'inputShapes':{k:list(v.shape) for k,v in inputs.items()},'inferenceSeconds':seconds,
                    'tokenBudgetReached':len(ids)==128,'eosObserved':model.generation_config.eos_token_id in ids,
                    'processPeakRssBytes':peak,'confidence':'unassessed','adoptable':False}
            f.write(json.dumps(record,ensure_ascii=False,separators=(',',':'))+'\n');f.flush();completed+=1
            if peak>8*1024**3:raise MemoryError('Fixed 8GiB reader envelope exceeded')
            if completed%12==0:print(json.dumps({'callsCompleted':completed,'callsPlanned':192,'peakRssBytes':peak}),flush=True)
    print(json.dumps({'callsCompleted':completed,'seconds':time.monotonic()-started,
                      'peakRssBytes':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss*1024,'qualification':'unassessed'}),flush=True)


if __name__=='__main__':main()
