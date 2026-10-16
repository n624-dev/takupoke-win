"""One fixed official English G2 first-pass greedy baseline on consumed dev."""
import hashlib
import importlib
import importlib.metadata
import json
import math
import resource
import sys
import time
import types
from pathlib import Path

import cv2
import numpy as np
from PIL import Image
import torch

ROOT=Path('/workspace/scratch/ascii-foundation-ctc-20261013-v1')
DEV=Path('/workspace/scratch/ascii-line-ctc-20261013-v1/development')
RESULTS=Path('/workspace/scratch/takupoke-win/research-results/ocr-synthetic-training-20261013/ascii-foundation-ctc-v1')
sys.path.insert(0,str(Path(__file__).parents[1]));from image_tensor import greedy


def load():
    receipt=json.loads((ROOT/'download-receipt.json').read_text(encoding='utf-8'));config=receipt['modelConfig'];path=ROOT/config['filename']
    if hashlib.sha256(path.read_bytes()).hexdigest()!=receipt['pthSha256']:raise ValueError('Weight SHA changed')
    directory=Path(importlib.metadata.distribution('easyocr').locate_file('easyocr/model'))
    for name in ['vgg_model.py','modules.py']:
        expected=receipt['codeFiles']['model-'+name]
        if hashlib.sha256((directory/name).read_bytes()).hexdigest()!=expected:raise ValueError('Official source mismatch')
    package=types.ModuleType('pinned_easyocr_model');package.__path__=[str(directory)];sys.modules[package.__name__]=package
    Model=importlib.import_module('pinned_easyocr_model.vgg_model').Model
    dictionary=['']+list(config['characters']);model=Model(input_channel=1,output_channel=256,hidden_size=256,num_class=len(dictionary)).eval()
    state=torch.load(path,map_location='cpu',weights_only=True)
    if not isinstance(state,dict) or not all(isinstance(v,torch.Tensor) for v in state.values()) or not all(key.startswith('module.') for key in state):raise ValueError('Non-tensor or unexpected state keys')
    model.load_state_dict({key[7:]:value for key,value in state.items()},strict=True)
    return model,dictionary,receipt


def tensor(path):
    # Official recognition AlignCollate64 / NormalizePAD first-pass logic,
    # applied directly to whole-line original crops. No detector, rotation,
    # contrast rerun, lexicon, ignore_char filtering or dynamic quantization.
    gray=cv2.imread(str(path),cv2.IMREAD_GRAYSCALE)
    if gray is None:raise ValueError('Cannot decode original PNG')
    height,width=gray.shape;input_width=max(64,math.ceil(width/height)*64)
    if input_width>960:raise ValueError('Bounded English line width exceeded')
    valid=min(input_width,math.ceil(64*width/height));image=Image.fromarray(gray,'L').resize((valid,64),Image.Resampling.BICUBIC);values=np.asarray(image,dtype=np.float32)/np.float32(255);values=(values-np.float32(.5))/np.float32(.5)
    output=np.empty((1,1,64,input_width),dtype=np.float32);output[0,0,:,:valid]=values;output[0,0,:,valid:]=values[:,-1,None]
    return output,valid


def distance(a,b):
    previous=list(range(len(b)+1))
    for i,left in enumerate(a,1):
        current=[i]
        for j,right in enumerate(b,1):current.append(min(current[-1]+1,previous[j]+1,previous[j-1]+(left!=right)))
        previous=current
    return previous[-1]


def main():
    RESULTS.mkdir(exist_ok=True,parents=True)
    if (RESULTS/'baseline-summary.json').exists():raise ValueError('Fixed baseline already consumed; no overwrite')
    torch.set_num_threads(2);torch.set_num_interop_threads(1);model,dictionary,receipt=load();manifest=json.loads((DEV/'manifest.json').read_text(encoding='utf-8'))
    freeze={'namespace':'research-ascii-foundation-ctc-v1','scope':'Consumed ASCII-v1 development only; no independent final or qualification credit','model':receipt,'dictionary':dictionary,'dictionarySha256':hashlib.sha256(json.dumps(dictionary,ensure_ascii=False,separators=(',',':')).encode('utf-8')).hexdigest(),'modelParameters':sum(p.numel() for p in model.parameters()),'pixelsManifestSha256':hashlib.sha256((DEV/'manifest.json').read_bytes()).hexdigest(),'condition':'Official FP32 Gray64 recognizer first-pass AlignCollate, batch1 rounded width64 multiple, PILBICUBIC, last-column padding, full dictionary, greedy; no detector/lexicon/contrast rerun/quantization','calls':len(manifest['rows']),'threads':2,'loading':'torch.load(weights_only=True),tensor-only,strict state keys and shapes','sources':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in Path(__file__).parent.glob('*.py')}}
    (RESULTS/'baseline-freeze.json').write_text(json.dumps(freeze,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
    records=[];start=time.monotonic();output=RESULTS/'baseline-unjudged.jsonl'
    with output.open('x',encoding='utf-8',newline='\n') as stream,torch.inference_mode():
        for row in manifest['rows']:
            path=DEV/row['file']
            if hashlib.sha256(path.read_bytes()).hexdigest()!=row['sha256']:raise ValueError('Pixel identity mismatch')
            array,valid=tensor(path);timer=time.monotonic();logits=model(torch.from_numpy(array),None);probability=logits.softmax(-1)[0].numpy();elapsed=time.monotonic()-timer;pieces=greedy(probability,dictionary);text=''.join(p['Text'] for p in pieces);minimum=min((p['Confidence'] for p in pieces),default=0);supported=all(p['End']*array.shape[-1]<=valid*probability.shape[0] for p in pieces);ascii_domain=all(32<=ord(char)<=126 for char in text)
            indices=probability.argmax(-1);nonblank=probability.max(-1)[indices!=0];official_confidence=float(np.prod(nonblank)**(2/np.sqrt(len(nonblank)))) if len(nonblank) else 0
            record={'id':row['id'],'prediction':text,'pieces':pieces,'minimumConfidence':minimum,'officialSequenceConfidence':official_confidence,'supported':supported,'printableAscii':ascii_domain,'accepted':bool(text) and minimum>=.8 and supported and ascii_domain,'inputWidth':array.shape[-1],'validWidth':valid,'timeSteps':probability.shape[0],'inferenceMilliseconds':elapsed*1000};records.append(record);stream.write(json.dumps(record,ensure_ascii=False)+'\n');stream.flush()
            if len(records)%128==0:print({'completed':len(records),'calls':len(manifest['rows'])},flush=True)
    # No literals supplied to inference; score only after complete raw file closes.
    rows={r['id']:r for r in manifest['rows']};positive=[r for r in records if not rows[r['id']]['reject']];negative=[r for r in records if rows[r['id']]['reject']]
    exact=lambda r:r['prediction']==rows[r['id']]['text'];summary={'status':'research-only; unqualified/notactivated','scope':freeze['scope'],'positiveRows':len(positive),'exact':sum(exact(r) for r in positive),'correctAccepted':sum(exact(r) and r['accepted'] for r in positive),'incorrectAccepted':sum(not exact(r) and r['accepted'] for r in positive),'cer':sum(distance(rows[r['id']]['text'],r['prediction']) for r in positive)/sum(len(rows[r['id']]['text']) for r in positive),'negativeRows':len(negative),'negativeAccepted':sum(r['accepted'] for r in negative),'groups':{kind:{'rows':len(group),'exact':sum(not rows[r['id']]['reject'] and exact(r) for r in group),'accepted':sum(r['accepted'] for r in group)} for kind in sorted({r['kind'] for r in manifest['rows']}) for group in [[r for r in records if rows[r['id']]['kind']==kind]]},'parameters':freeze['modelParameters'],'weightsBytes':receipt['pthBytes'],'elapsedSeconds':time.monotonic()-start,'maximumRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'medianInferenceMilliseconds':float(np.median([r['inferenceMilliseconds'] for r in records])),'rawSha256':hashlib.sha256(output.read_bytes()).hexdigest(),'limits':'English97 including Euro, no UNK/reject head. CJK/damage/unknown inference is deliberately tested but unsupported. Min-piece CTC score is uncalibrated; no full document/role/ink qualification. Timing under concurrent work; no native-Windows speed claim.'}
    (RESULTS/'baseline-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(summary),flush=True)


if __name__=='__main__':main()
