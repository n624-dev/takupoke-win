"""Bounded local CPU CTC adaptation with a frozen full-vocabulary teacher."""
import argparse
import copy
import hashlib
import json
import random
import resource
import time
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from transformers import PPOCRV6SmallRecForTextRecognition
import yaml

from corpus import generate, load_arrays
from image_tensor import greedy


def decode(prob,dictionary):
    return ''.join(x['Text'] for x in greedy(prob,dictionary))


def distance(left,right):
    prior=list(range(len(right)+1))
    for i,a in enumerate(left,1):
        current=[i]
        for j,b in enumerate(right,1):current.append(min(current[-1]+1,prior[j]+1,prior[j-1]+(a!=b)))
        prior=current
    return prior[-1]


def evaluate(model,arrays,rows,dictionary):
    model.eval();records=[];start=time.monotonic()
    with torch.inference_mode():
        for array,row in zip(arrays,rows):
            prob=model(pixel_values=torch.from_numpy(array[None])).last_hidden_state[0].numpy()
            pieces=greedy(prob,dictionary);pred=''.join(x['Text'] for x in pieces)
            records.append({'id':row['id'],'category':row['category'],'font':row['font'],'gold':row['text'],'prediction':pred,'exact':pred==row['text'],'editDistance':distance(row['text'],pred),'pieces':pieces})
    groups={}
    for key in ['all']+sorted({r['category'] for r in rows})+sorted({r['font'] for r in rows}):
        selected=records if key=='all' else [r for r in records if r['category']==key or r['font']==key]
        groups[key]={'rows':len(selected),'exact':sum(r['exact'] for r in selected),'cer':sum(r['editDistance'] for r in selected)/max(1,sum(len(r['gold']) for r in selected))}
    return {'groups':groups,'elapsedSecondsWithConcurrentWork':time.monotonic()-start,'records':records}


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',type=Path,required=True);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
    protocol=json.loads((Path(__file__).parent/'protocol.json').read_text(encoding='utf-8'));spec=protocol['training'];args.output.mkdir(exist_ok=True,parents=True)
    if (args.output/'selection.json').exists():raise ValueError('Experiment already selected; do not overwrite')
    torch.set_num_threads(spec['threads']);torch.set_num_interop_threads(1);torch.manual_seed(spec['seed']);np.random.seed(spec['seed']);random.seed(spec['seed'])
    # 8GiB own-process address space ceiling, allowing existing runtime maps.
    resource.setrlimit(resource.RLIMIT_AS,(12*1024**3,12*1024**3))
    dictionary=['']+yaml.safe_load((args.assets/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' ']
    index={c:i for i,c in enumerate(dictionary)}
    train=generate(protocol,'train',args.output/'train');dev=generate(protocol,'development',args.output/'development',[r['text'] for r in train['rows']])
    train_arrays=load_arrays(train,args.output/'train');dev_arrays=load_arrays(dev,args.output/'development')
    for row in train['rows']:
        if any(c not in index for c in row['text']):raise ValueError(f'Label outside official dictionary: {row["text"]}')
    model=PPOCRV6SmallRecForTextRecognition.from_pretrained(args.assets,local_files_only=True).eval()
    for parameter in model.model.parameters():parameter.requires_grad=False
    teacher=copy.deepcopy(model.head).eval()
    for parameter in teacher.parameters():parameter.requires_grad=False
    optimizer=torch.optim.AdamW([{'params':model.head.encoder.parameters(),'lr':spec['encoderLearningRate']},{'params':model.head.head.parameters(),'lr':spec['classifierLearningRate']}],weight_decay=spec['weightDecay'])
    baseline=evaluate(model,dev_arrays,dev['rows'],dictionary);(args.output/'development-baseline.json').write_text(json.dumps(baseline,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n')
    print(json.dumps({'stage':'baseline-development','groups':baseline['groups']},ensure_ascii=False),flush=True)
    candidates=[{'name':'baseline','epoch':0,'exact':baseline['groups']['all']['exact'],'cer':baseline['groups']['all']['cer'],'path':str(args.assets),'sha256':hashlib.sha256((args.assets/'model.safetensors').read_bytes()).hexdigest()}]
    loss_fn=torch.nn.CTCLoss(blank=0,zero_infinity=False);start=time.monotonic();history=[]
    for epoch in range(1,spec['epochs']+1):
        order=list(range(len(train['rows'])));random.Random(spec['seed']+epoch).shuffle(order)
        losses=[]
        # eval freezes BN statistics; gradients remain enabled for encoder/head.
        model.eval()
        for offset in range(0,len(order),spec['batchSize']):
            chosen=order[offset:offset+spec['batchSize']];width=max(train_arrays[i].shape[-1] for i in chosen)
            batch=np.zeros((len(chosen),3,48,width),dtype=np.float32)
            for j,i in enumerate(chosen):batch[j,:,:,:train_arrays[i].shape[-1]]=train_arrays[i]
            targets=[torch.tensor([index[c] for c in train['rows'][i]['text']],dtype=torch.long) for i in chosen]
            with torch.no_grad():features=model.model(torch.from_numpy(batch)).last_hidden_state;teacher_prob=teacher(features).last_hidden_state
            logits=model.head.head(model.head.encoder(features).last_hidden_state)
            log_prob=logits.log_softmax(-1)
            lengths=torch.full((len(chosen),),logits.shape[1],dtype=torch.long)
            ctc=loss_fn(log_prob.transpose(0,1),torch.cat(targets),lengths,torch.tensor([len(t) for t in targets]))
            kl=F.kl_div(log_prob,teacher_prob,reduction='sum')/(log_prob.shape[0]*log_prob.shape[1])
            loss=ctc+.1*kl
            if not torch.isfinite(loss):raise ValueError('Nonfinite training loss')
            optimizer.zero_grad(set_to_none=True);loss.backward();torch.nn.utils.clip_grad_norm_(model.head.parameters(),spec['gradientClip']);optimizer.step()
            losses.append([float(ctc.detach()),float(kl.detach())])
            if offset%256==0:print(json.dumps({'stage':'training','epoch':epoch,'completedRows':offset+len(chosen),'ctc':losses[-1][0],'teacherKl':losses[-1][1],'elapsedSeconds':time.monotonic()-start,'maxRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024}),flush=True)
        checkpoint=args.output/f'epoch-{epoch}';model.save_pretrained(checkpoint)
        (checkpoint/'inference.yml').write_bytes((args.assets/'inference.yml').read_bytes())
        result=evaluate(model,dev_arrays,dev['rows'],dictionary);(args.output/f'development-epoch-{epoch}.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n')
        candidate={'name':f'epoch-{epoch}','epoch':epoch,'exact':result['groups']['all']['exact'],'cer':result['groups']['all']['cer'],'path':str(checkpoint),'sha256':hashlib.sha256((checkpoint/'model.safetensors').read_bytes()).hexdigest()};candidates.append(candidate)
        history.append({'epoch':epoch,'meanCtc':float(np.mean(losses,axis=0)[0]),'meanTeacherKl':float(np.mean(losses,axis=0)[1]),'groups':result['groups']})
        print(json.dumps({'stage':'development','epoch':epoch,'groups':result['groups']},ensure_ascii=False),flush=True)
    selected=sorted(candidates,key=lambda c:(-c['exact'],c['cer'],c['epoch']))[0]
    receipt={'protocolSha256':hashlib.sha256(json.dumps(protocol,sort_keys=True).encode()).hexdigest(),'candidates':candidates,'selected':selected,'history':history,'maxRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'elapsedSeconds':time.monotonic()-start,'status':'research-only; final not yet opened; not qualified or activated'}
    (args.output/'selection.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n');print(json.dumps({'stage':'selection','selected':selected},ensure_ascii=False),flush=True)


if __name__=='__main__':main()
