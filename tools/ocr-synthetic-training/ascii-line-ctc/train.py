"""One fixed bounded CPU experiment; dev selection precedes final opening."""
import hashlib
import json
import random
import resource
import sys
import time
from pathlib import Path

import numpy as np
import torch
from safetensors.torch import save_file

from data import ROOT,DICTIONARY,INDEX,generate,arrays,excluded_literals,save_json
from model import LineCTC
sys.path.insert(0,str(Path(__file__).parents[1]));from image_tensor import greedy


def edit_distance(a,b):
    previous=list(range(len(b)+1))
    for i,left in enumerate(a,1):
        current=[i]
        for j,right in enumerate(b,1):current.append(min(current[-1]+1,previous[j]+1,previous[j-1]+(left!=right)))
        previous=current
    return previous[-1]


def evaluate(model,manifest,inputs):
    model.eval();records=[]
    with torch.inference_mode():
        for row,array in zip(manifest['rows'],inputs):
            probability,reject=model(torch.from_numpy(array.astype(np.float32)[None]));pieces=greedy(probability[0].numpy(),DICTIONARY);text=''.join(p['Text'] for p in pieces);minimum=min((p['Confidence'] for p in pieces),default=0);supported=all(p['End']*row['inputWidth']<=row['validWidth']*probability.shape[1] for p in pieces)
            accepted=bool(text) and '�' not in text and minimum>=.8 and float(reject[0])<=.01 and supported
            records.append({'id':row['id'],'kind':row['kind'],'font':row['font'],'gold':row['text'],'negative':row['reject'],'prediction':text,'rejectProbability':float(reject[0]),'minimumConfidence':minimum,'supported':supported,'accepted':accepted,'exact':not row['reject'] and text==row['text'],'pieces':pieces})
    positive=[r for r in records if not r['negative']];negative=[r for r in records if r['negative']];exact=sum(r['exact'] for r in positive);rejected=sum(not r['accepted'] for r in negative);accuracy=exact/len(positive);specificity=rejected/len(negative)
    summary={'positiveRows':len(positive),'exact':exact,'correctAccepted':sum(r['exact'] and r['accepted'] for r in positive),'incorrectAccepted':sum(not r['exact'] and r['accepted'] for r in positive),'negativeRows':len(negative),'negativeRejected':rejected,'negativeAccepted':len(negative)-rejected,'harmonic':2*accuracy*specificity/max(1e-12,accuracy+specificity),'cer':sum(edit_distance(r['gold'],r['prediction']) for r in positive)/sum(len(r['gold']) for r in positive),'groups':{kind:{'rows':sum(r['kind']==kind for r in records),'accepted':sum(r['kind']==kind and r['accepted'] for r in records),'exact':sum(r['kind']==kind and r['exact'] for r in records)} for kind in sorted({r['kind'] for r in records})}}
    return {'summary':summary,'records':records}


def main():
    protocol=json.loads((Path(__file__).parent/'protocol.json').read_text(encoding='utf-8'));p=protocol['training'];ROOT.mkdir(exist_ok=True,parents=True)
    if (ROOT/'selection.json').exists():raise ValueError('Already selected; no overwrite')
    torch.set_num_threads(p['threads']);torch.set_num_interop_threads(1);torch.manual_seed(protocol['seed']);random.seed(protocol['seed']);np.random.seed(protocol['seed']);resource.setrlimit(resource.RLIMIT_AS,(12*1024**3,12*1024**3))
    forbidden=excluded_literals();train=generate(protocol,'train',forbidden);forbidden.update(r['target'] for r in train['rows']);dev=generate(protocol,'development',forbidden);inputs=arrays(train);dev_inputs=arrays(dev)
    model=LineCTC();optimizer=torch.optim.AdamW(model.parameters(),lr=p['learningRate'],weight_decay=p['weightDecay']);ctc=torch.nn.CTCLoss(blank=0,zero_infinity=False);bce=torch.nn.BCEWithLogitsLoss();start=time.monotonic();candidates=[];history=[]
    print(json.dumps({'stage':'start','parameters':sum(p.numel() for p in model.parameters()),'trainRows':len(inputs),'devRows':len(dev_inputs),'protocolSha256':train['protocolSha256']}),flush=True)
    for epoch in range(1,p['epochs']+1):
        model.train();order=list(range(len(inputs)));random.Random(protocol['seed']+epoch).shuffle(order);losses=[]
        for offset in range(0,len(order),p['batchSize']):
            selected=order[offset:offset+p['batchSize']];width=max(inputs[i].shape[-1] for i in selected);batch=np.zeros((len(selected),3,64,width),dtype=np.float32)
            for j,i in enumerate(selected):batch[j,:,:,:inputs[i].shape[-1]]=inputs[i]
            targets=[torch.tensor([INDEX[c] for c in train['rows'][i]['target']],dtype=torch.long) for i in selected];logits,reject=model.logits(torch.from_numpy(batch));lengths=torch.full((len(selected),),logits.shape[1],dtype=torch.long)
            loss=ctc(logits.log_softmax(-1).transpose(0,1),torch.cat(targets),lengths,torch.tensor([len(target) for target in targets]))+.5*bce(reject,torch.tensor([float(train['rows'][i]['reject']) for i in selected]))
            if not torch.isfinite(loss):raise ValueError('Nonfinite loss')
            optimizer.zero_grad(set_to_none=True);loss.backward();torch.nn.utils.clip_grad_norm_(model.parameters(),p['clip']);optimizer.step();losses.append(float(loss.detach()))
        history.append({'epoch':epoch,'loss':float(np.mean(losses)),'elapsedSeconds':time.monotonic()-start,'maxRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024});print(json.dumps({'stage':'epoch',**history[-1]}),flush=True)
        if epoch in p['developmentEpochs']:
            model.eval();checkpoint=ROOT/f'epoch-{epoch}.safetensors';save_file(model.state_dict(),str(checkpoint));result=evaluate(model,dev,dev_inputs);save_json(ROOT/f'development-epoch-{epoch}.json',result);candidate={'epoch':epoch,'checkpoint':str(checkpoint),'sha256':hashlib.sha256(checkpoint.read_bytes()).hexdigest(),**result['summary']};candidates.append(candidate);print(json.dumps({'stage':'development','candidate':candidate}),flush=True)
    chosen=sorted(candidates,key=lambda c:(-c['harmonic'],-c['exact'],c['cer'],c['epoch']))[0]
    save_json(ROOT/'selection.json',{'status':'research-only; final not opened; not qualified or activated','protocolSha256':train['protocolSha256'],'selected':chosen,'candidates':candidates,'history':history,'parameters':sum(p.numel() for p in model.parameters())});print(json.dumps({'stage':'selection','selected':chosen}),flush=True)


if __name__=='__main__':main()
