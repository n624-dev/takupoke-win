#!/usr/bin/env python3
"""Official printed TrOCR inference, fixed header ordinals and no oracle."""
import argparse
import base64
import hashlib
import io
import json
from pathlib import Path
import resource
import time

import torch
from PIL import Image
from transformers import AutoImageProcessor, AutoTokenizer, TrOCRProcessor, VisionEncoderDecoderModel


def sha(data): return hashlib.sha256(data).hexdigest()


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',type=Path,required=True)
    p.add_argument('--pixels',type=Path,required=True);p.add_argument('--freeze',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    f=json.loads(a.freeze.read_text(encoding='utf-8'))
    if sha(a.pixels.read_bytes())!=f['pixelsSHA256']:raise ValueError('Pixel packet changed')
    for name,v in f['files'].items():
        path=a.assets/name
        if path.stat().st_size!=v['bytes'] or sha(path.read_bytes())!=v['sha256']:raise ValueError('Frozen official artifact changed')
    torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.manual_seed(17)
    started=time.monotonic()
    # This older checkpoint lacks a composite processor_class. AutoProcessor
    # alone resolves its Roberta tokenizer, which cannot receive images.
    image_processor=AutoImageProcessor.from_pretrained(a.assets,local_files_only=True,trust_remote_code=False,backend='pil')
    tokenizer=AutoTokenizer.from_pretrained(a.assets,local_files_only=True,trust_remote_code=False)
    processor=TrOCRProcessor(image_processor=image_processor,tokenizer=tokenizer)
    model=VisionEncoderDecoderModel.from_pretrained(a.assets,local_files_only=True,trust_remote_code=False,
        dtype=torch.float32,attn_implementation='eager').eval()
    if next(model.parameters()).device.type!='cpu':raise ValueError('CPU only')
    rows={r['id']:r for r in json.loads(a.pixels.read_text(encoding='utf-8'))['rows']}
    with a.output.open('x',encoding='utf-8',newline='\n') as out,torch.inference_mode():
        for number,id in enumerate(f['plannedOrdinals']):
            if time.monotonic()-started>1500:raise TimeoutError('Fixed inference deadline')
            row=rows[id];png=base64.b64decode(row['pngBase64'],validate=True)
            if sha(png)!=row['pngSha256']:raise ValueError('Original PNG changed')
            with Image.open(io.BytesIO(png)) as im:
                if im.size!=(row['width'],row['height']):raise ValueError('Image geometry changed')
                pixels=processor(images=im.convert('RGB'),return_tensors='pt').pixel_values
            if tuple(pixels.shape)!=(1,3,384,384) or pixels.dtype!=torch.float32:raise ValueError('Official processor shape changed')
            watch=time.monotonic()
            generated=model.generate(pixels,max_new_tokens=32,do_sample=False,num_beams=1,use_cache=True,
                return_dict_in_generate=True,output_scores=True)
            seconds=time.monotonic()-watch
            transitions=model.compute_transition_scores(generated.sequences,generated.scores,normalize_logits=True)
            probabilities=transitions.exp()[0].tolist();tokens=generated.sequences[0].tolist()
            text=processor.batch_decode(generated.sequences,skip_special_tokens=True,clean_up_tokenization_spaces=False)[0]
            record={'id':id,'pngSHA256':row['pngSha256'],'text':text,'nativeTokenIdsIncludingSpecials':tokens,
                'generatedTokenProbabilitiesIncludingEos':probabilities,'minimumGeneratedTokenProbabilityIncludingEos':min(probabilities) if probabilities else None,
                'eosObserved':bool(tokens and tokens[-1]==model.generation_config.eos_token_id),'budgetReached':len(generated.scores)>=32,
                'inputTensorSHA256':sha(pixels.numpy().tobytes()),'inferenceSeconds':seconds,
                'processPeakRssBytes':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss*1024,
                'processorClass':type(processor.image_processor).__name__,'modelParameters':sum(x.numel() for x in model.parameters()),
                'qualification':False,'sourceGeometry':'Original whole crop only; autoregressive tokens supply no original glyph boxes'}
            out.write(json.dumps(record,ensure_ascii=False,separators=(',',':'))+'\n');out.flush()
            if (number+1)%12==0:print(json.dumps({'completed':number+1,'planned':48}),flush=True)


if __name__=='__main__':main()
