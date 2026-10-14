#!/usr/bin/env python3
"""Japanese image-to-character foundation; fixed pixels and literal-free worker."""
import argparse
import base64
import hashlib
import importlib.metadata
import io
import json
import os
from pathlib import Path
import resource
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
PIXELS = ROOT/'tools/vision-ocr-comparison/fictional-pixels.json'
GOLD = ROOT/'tools/vision-ocr-comparison/oracle.json'
REVISION = 'aa6573bd10b0d446cbf622e29c3e084914df9741'


def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()


def write(p,x): p.write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


def worker(a):
    import torch
    from PIL import Image
    from transformers import AutoImageProcessor, AutoTokenizer, VisionEncoderDecoderModel
    f = json.loads((a.output/'freeze.json').read_text())
    if sha(PIXELS) != f['pixelsSha256']: raise ValueError('Original packet changed')
    for n,v in f['model']['files'].items():
        if sha(a.assets/n) != v['sha256'] or (a.assets/n).stat().st_size != v['bytes']: raise ValueError('Artifact changed')
    torch.set_num_threads(2); torch.set_num_interop_threads(1); torch.manual_seed(17)
    processor = AutoImageProcessor.from_pretrained(a.assets,local_files_only=True,trust_remote_code=False,backend='pil')
    tokenizer = AutoTokenizer.from_pretrained(a.assets,local_files_only=True,trust_remote_code=False)
    model = VisionEncoderDecoderModel.from_pretrained(a.assets,local_files_only=True,trust_remote_code=False,
        weights_only=True,dtype=torch.float32,attn_implementation='eager').eval()
    omit = {model.config.decoder_start_token_id, model.config.eos_token_id, model.config.pad_token_id}
    rows = json.loads(PIXELS.read_text())['rows']; started = time.monotonic()
    with (a.output/'observations.jsonl').open('x',encoding='utf-8',newline='\n') as stream, torch.inference_mode():
        for number,row in enumerate(rows,1):
            if time.monotonic()-started > 1500: raise TimeoutError('Fixed inference budget')
            png = base64.b64decode(row['pngBase64'],validate=True)
            if hashlib.sha256(png).hexdigest() != row['pngSha256']: raise ValueError('PNG changed')
            with Image.open(io.BytesIO(png)) as image:
                if image.size != (row['width'],row['height']): raise ValueError('Original shape changed')
                pixels = processor(images=image.convert('RGB'),return_tensors='pt').pixel_values
            if tuple(pixels.shape) != (1,3,224,224) or pixels.dtype != torch.float32: raise ValueError('Native RGB224 shape changed')
            timer = time.monotonic()
            generated = model.generate(pixels,max_new_tokens=64,do_sample=False,num_beams=4,
                use_cache=True,return_dict_in_generate=True,output_scores=True)
            elapsed = time.monotonic()-timer
            transitions = model.compute_transition_scores(generated.sequences,generated.scores,
                beam_indices=generated.beam_indices,normalize_logits=True)
            ids = generated.sequences[0].tolist(); pieces = tokenizer.convert_ids_to_tokens(ids)
            # Character vocabulary IDs map directly to scalars. The tokenizer's
            # display joins tokens with spaces; preserve that raw string too.
            # Only known BOS/EOS/PAD are omitted; UNK remains a visible failure.
            literal = ''.join(piece for index,piece in zip(ids,pieces) if index not in omit)
            probabilities = transitions.exp()[0].tolist()
            record = {'id':row['id'],'text':literal,'rawTokenizerDecoded':tokenizer.decode(ids,skip_special_tokens=True,clean_up_tokenization_spaces=False),
                'nativeTokenIdsIncludingSpecials':ids,'nativeTokenStringsIncludingSpecials':pieces,
                'generatedProbabilitiesIncludingEos':probabilities,
                'minimumGeneratedProbabilityIncludingEos':min(probabilities) if probabilities else None,
                'eosObserved':bool(ids and ids[-1] == model.config.eos_token_id),'budgetReached':len(generated.scores) >= 64,
                'inputTensorSha256':hashlib.sha256(pixels.numpy().tobytes()).hexdigest(), 'inferenceSeconds':elapsed,
                'processPeakRssBytes':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss*1024,
                'sourceGeometry':'Whole original crop only; autoregressive characters do not supply source glyph boxes', 'qualification':False}
            stream.write(json.dumps(record,ensure_ascii=False,separators=(',',':'))+'\n');stream.flush()
            if number%24 == 0: print(json.dumps({'completed':number,'planned':192}),flush=True)
    write(a.output/'worker-report.json',{'callsCompleted':len(rows),'elapsedSeconds':time.monotonic()-started,
        'modelParameters':sum(p.numel() for p in model.parameters()),'generationConfig':model.generation_config.to_dict(),
        'loading':'torch weights_only=True; CPU FP32 eager, local files only, no remote code','qualification':False})


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',required=True,type=Path);p.add_argument('--output',required=True,type=Path)
    p.add_argument('--worker',action='store_true');a=p.parse_args()
    if a.worker: worker(a);return
    a.output.mkdir(parents=True,exist_ok=False)
    receipt=json.loads((a.assets/'download-receipt.json').read_text())
    if receipt['repository']!='kha-white/manga-ocr-base' or receipt['revision']!=REVISION: raise ValueError('Wrong model')
    if receipt['files']['pytorch_model.bin']!={'bytes':444135475,'sha256':'c63e0bb5b3ff798c5991de18a8e0956c7ee6d1563aca6729029815eda6f5c2eb'}: raise ValueError('Pinned weights changed')
    if sha(PIXELS)!='6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff':raise ValueError('Consumed pixels changed')
    vocab=(a.assets/'vocab.txt').read_text().splitlines()
    if len(vocab)!=6144:raise ValueError('Vocabulary changed')
    freeze={'recipe':'manga-ocr-original-whole-lines-v1','model':receipt,'pixelsSha256':sha(PIXELS),'oracleSha256':sha(GOLD),
        'sources':{Path(__file__).name:sha(Path(__file__))},'callsPlanned':192,'threads':2,'seed':17,'dtype':'float32',
        'runtime':{n:importlib.metadata.version(n) for n in ['torch','transformers','Pillow','fugashi','unidic-lite']},
        'processor':'Official PIL RGB224 bilinear/normalization, no glyph crop/contrast/rotation',
        'generation':'Original beam4/length_penalty2/no_repeat_ngram3/early_stopping plus fixed64 new-token budget; no sampling',
        'output':'Literal scalar vocabulary concatenation; only known BOS/EOS/PAD removed. Raw tokenizer display/native IDs/probabilities remain separate. No Unicode/width/case normalization or whitespace repair.',
        'literalSpaceInVocabulary':' ' in vocab,'scope':'Consumed fictional component development; this vocabulary lacks literal space, so arbitrary space preservation cannot be qualified',
        'qualification':False,'modelActivation':False}
    write(a.output/'freeze.json',freeze)
    env={k:v for k,v in os.environ.items() if not any(s in k.upper() for s in ['TOKEN','SECRET','PASSWORD','CREDENTIAL'])}
    env.update({'HF_HUB_OFFLINE':'1','TRANSFORMERS_OFFLINE':'1','TOKENIZERS_PARALLELISM':'false'})
    with (a.output/'worker-stdout.txt').open('w') as stdout,(a.output/'worker-stderr.txt').open('w') as stderr:
        child=subprocess.run([sys.executable,str(Path(__file__).resolve()),'--worker','--assets',str(a.assets),'--output',str(a.output)],
            env=env,stdout=stdout,stderr=stderr,timeout=1800)
    raw=a.output/'observations.jsonl';write(a.output/'raw-receipt.json',{'exitCode':child.returncode,'sha256':sha(raw) if raw.exists() else None,'bytes':raw.stat().st_size if raw.exists() else None})
    if child.returncode:raise RuntimeError('Native inference failed; retain partial raw, no quality credit')
    if sha(GOLD)!=freeze['oracleSha256'] or sha(Path(__file__))!=freeze['sources'][Path(__file__).name]:raise ValueError('Source/oracle changed')
    gold={r['id']:r for r in json.loads(GOLD.read_text())};observed=[json.loads(l) for l in raw.read_text().splitlines()]
    if len(observed)!=192 or {r['id'] for r in observed}!=set(gold):raise ValueError('Missing/duplicate calls')
    groups={}
    for r in observed:
        g=gold[r['id']];v=groups.setdefault(g['cohort']+'/'+g['kind'],{'rows':0,'exact':0,'correctAboveDiagnosticFloor':0,'incorrectAboveDiagnosticFloor':0,'budgetOrNoEos':0})
        exact=r['text']==g['literal'];above=bool(r['text']) and r['minimumGeneratedProbabilityIncludingEos'] is not None and r['minimumGeneratedProbabilityIncludingEos']>=.8 and r['eosObserved'] and not r['budgetReached']
        v['rows']+=1;v['exact']+=exact;v['correctAboveDiagnosticFloor']+=exact and above;v['incorrectAboveDiagnosticFloor']+=not exact and above;v['budgetOrNoEos']+=r['budgetReached'] or not r['eosObserved']
    report={'rawSha256':sha(raw),'groups':groups,'callsCompleted':192,'totalInferenceSeconds':sum(r['inferenceSeconds'] for r in observed),
        'peakRssBytes':max(r['processPeakRssBytes'] for r in observed),'qualification':False,'modelActivation':False,
        'confidence':'Uncalibrated autoregressive beam-path probabilities including EOS, not CTC scores or source ownership'}
    write(a.output/'report.json',report);print(json.dumps(report,ensure_ascii=False),flush=True)


if __name__=='__main__':main()
