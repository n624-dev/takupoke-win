"""Frozen bounded fictional ASCII transfer; development selection includes base."""
import copy
import hashlib
import json
from pathlib import Path
import random
import resource
import sys
import time

import numpy as np
from safetensors.torch import save_file
import torch
import torch.nn.functional as F

from data import ROOT,REPO,FONTS,DICTIONARY,INDEX,save,exclusion,generate,arrays
from model import load_base,Transfer,BASE_SHA
sys.path.insert(0,str(Path(__file__).parents[1]))
from image_tensor import greedy
sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-foundation-ctc'))
from diagnose import edits

RESULTS=REPO/'research-results/ocr-synthetic-training-20261013/ascii-paddle-transfer-v1'


def evaluate(name,model,values,manifest,dictionary,trained_reject=False):
    model.eval();raw=RESULTS/f'{name}-unjudged.jsonl';records=[]
    if raw.exists():raise ValueError('Fixed development call cannot overwrite')
    with raw.open('x',encoding='utf-8',newline='\n') as stream,torch.inference_mode():
        for array,row in zip(values,manifest['rows']):
            x=torch.from_numpy(array[None].astype(np.float32))
            if isinstance(model,Transfer):prob,reject=model(x);reject_probability=float(reject[0])
            else:prob=model(pixel_values=x).last_hidden_state;reject_probability=None
            probability=prob[0].numpy();pieces=greedy(probability,dictionary);prediction=''.join(piece['Text'] for piece in pieces)
            minimum=min((piece['Confidence'] for piece in pieces),default=0)
            supported=all(piece['End']*array.shape[-1]<=row['validWidth']*probability.shape[0] for piece in pieces)
            accepted=bool(prediction) and all(32<=ord(char)<=126 for char in prediction) and minimum>=.8 and supported and (not trained_reject or reject_probability<=.05)
            record={'id':row['id'],'prediction':prediction,'pieces':pieces,'minimumConfidence':minimum,'rejectProbability':reject_probability,'accepted':accepted,'supported':supported,'inputWidth':array.shape[-1],'validWidth':row['validWidth'],'timeSteps':probability.shape[0],'source':row['source']}
            stream.write(json.dumps(record,ensure_ascii=False)+'\n');records.append(record)
    # No literal scoring until complete raw output closes. No output corrections.
    lookup={row['id']:row for row in manifest['rows']};positive=[row for row in records if not lookup[row['id']]['reject']];negative=[row for row in records if lookup[row['id']]['reject']]
    exact=lambda row:row['prediction']==lookup[row['id']]['text']
    mismatch={row['id']:edits(lookup[row['id']]['text'],row['prediction']) for row in positive}
    result={'name':name,'positiveRows':len(positive),'exact':sum(exact(row) for row in positive),'correctAccepted':sum(exact(row) and row['accepted'] for row in positive),'incorrectAccepted':sum(not exact(row) and row['accepted'] for row in positive),'negativeRows':len(negative),'negativeAccepted':sum(row['accepted'] for row in negative),'cer':sum(len(mismatch[row['id']]) for row in positive)/sum(len(lookup[row['id']]['text']) for row in positive),'paddingRows':sum(not row['supported'] for row in records),'caseMismatchAccepted':sum(row['accepted'] and any(a and b and a!=b and a.lower()==b.lower() for a,b in mismatch[row['id']]) for row in positive),'confusableMismatchAccepted':sum(row['accepted'] and any(a and b and a!=b and (a in 'Il1' and b in 'Il1' or a in 'O0' and b in 'O0') for a,b in mismatch[row['id']]) for row in positive),'groups':{kind:{'rows':len(group),'exact':sum(not lookup[row['id']]['reject'] and exact(row) for row in group),'correctAccepted':sum(not lookup[row['id']]['reject'] and exact(row) and row['accepted'] for row in group),'incorrectAccepted':sum(not lookup[row['id']]['reject'] and not exact(row) and row['accepted'] for row in group),'accepted':sum(row['accepted'] for row in group),'nonempty':sum(bool(row['prediction']) for row in group),'asciiNonempty':sum(bool(row['prediction']) and all(32<=ord(c)<=126 for c in row['prediction']) for row in group)} for kind in sorted({row['kind'] for row in manifest['rows']}) for group in [[row for row in records if lookup[row['id']]['kind']==kind]]},'rawSha256':hashlib.sha256(raw.read_bytes()).hexdigest(),'status':'research-only/unqualified/inactive; independent development, final unopened'}
    save(RESULTS/f'{name}-summary.json',result);print(json.dumps({'stage':'development','result':result}),flush=True)
    return result


def main():
    protocol_path=Path(__file__).parent/'protocol.json';protocol=json.loads(protocol_path.read_text(encoding='utf-8'));spec=protocol['training']
    if (ROOT/'freeze.json').exists():raise ValueError('Frozen run already started; no restart/overwrite')
    ROOT.mkdir(parents=True,exist_ok=True);RESULTS.mkdir(parents=True,exist_ok=True)
    torch.set_num_threads(spec['threads']);torch.set_num_interop_threads(1);torch.manual_seed(spec['seed']);np.random.seed(spec['seed']);random.seed(spec['seed'])
    forbidden=exclusion()
    freeze={'protocol':protocol,'protocolSha256':hashlib.sha256(json.dumps(protocol,sort_keys=True).encode('utf-8')).hexdigest(),'sourceHashes':{path.name:hashlib.sha256(path.read_bytes()).hexdigest() for path in Path(__file__).parent.glob('*') if path.suffix in ['.py','.json']},'fontHashes':{name:hashlib.sha256(path.read_bytes()).hexdigest() for name,path in FONTS.items()},'fontPriorExposure':'Train fonts previously used. Dev KiwiMaru/HinaMincho parent confirmed unconsumed; ZenMaru previously consumed and downloaded only here, NOT used in new dev. Final Sawarabi reserved untouched.','exclusionReceiptSha256':hashlib.sha256((ROOT/'exclusion-receipt.json').read_bytes()).hexdigest(),'oldRuntimeSourceHashes':{name:hashlib.sha256((Path(__file__).parents[1]/name).read_bytes()).hexdigest() for name in ['image_tensor.py']}}
    save(ROOT/'freeze.json',freeze);save(RESULTS/'freeze.json',freeze);save(RESULTS/'exclusion-receipt.json',json.loads((ROOT/'exclusion-receipt.json').read_text(encoding='utf-8')))
    train=generate(protocol,'train',forbidden);forbidden.update(row['text'] for row in train['rows'] if row['text'])
    development=generate(protocol,'development',forbidden)
    save(RESULTS/'corpus-receipt.json',{'trainManifestSha256':hashlib.sha256((ROOT/'train/manifest.json').read_bytes()).hexdigest(),'developmentManifestSha256':hashlib.sha256((ROOT/'development/manifest.json').read_bytes()).hexdigest(),'trainRows':len(train['rows']),'developmentRows':len(development['rows']),'allNonwhiteRetained':all(row['source']['allNonwhiteRetained'] for row in train['rows']+development['rows'])})
    train_values=arrays(train);dev_values=arrays(development)
    base,official_dictionary=load_base();baseline=evaluate('original-full18710',base,dev_values,development,official_dictionary)
    teacher=copy.deepcopy(base.head).eval()
    for parameter in teacher.parameters():parameter.requires_grad=False
    model=Transfer(base,official_dictionary)
    # This diagnostic isolates dictionary reduction, before any gradient update;
    # the untrained reject head is not used as a baseline rejection shortcut.
    reduced=evaluate('reduced-untrained',model,dev_values,development,DICTIONARY)
    save(RESULTS/'initialization.json',{'asciiAndBlankRowsExactlyCopied':True,'copiedOfficialIndices':model.copiedOfficialIndices.tolist(),'dictionary':DICTIONARY,'reducedParameters':sum(p.numel() for p in model.parameters()),'trainableParameters':sum(p.numel() for p in model.parameters() if p.requires_grad),'unkInitializer':'Mean excluded original linear rows/biases; not logsumexp or retained probability mass','rejectInitializer':'Zero weights/bias =>0.5, bypassed only in reduced-untrained diagnostic','baseSha256':BASE_SHA})
    optimizer=torch.optim.AdamW([{'params':model.recognizer.head.encoder.parameters(),'lr':spec['encoderLearningRate']},{'params':model.recognizer.head.head.parameters(),'lr':spec['classifierLearningRate']},{'params':model.reject.parameters(),'lr':spec['rejectLearningRate']}],weight_decay=spec['weightDecay'])
    loss_fn=torch.nn.CTCLoss(blank=0,zero_infinity=False);started=time.monotonic();candidates=[];history=[];stopped=False
    for epoch in range(1,spec['epochs']+1):
        model.eval();order=list(range(len(train['rows'])));random.Random(spec['seed']+epoch).shuffle(order);losses=[]
        for offset in range(0,len(order),spec['batchSize']):
            if time.monotonic()-started>spec['maximumTrainingSeconds']:stopped=True;break
            chosen=order[offset:offset+spec['batchSize']];width=max(train_values[i].shape[-1] for i in chosen);batch=np.zeros((len(chosen),3,48,width),dtype=np.float32)
            for j,i in enumerate(chosen):batch[j,:,:,:train_values[i].shape[-1]]=train_values[i]
            labels=[torch.tensor([INDEX[c] for c in train['rows'][i]['target']],dtype=torch.long) for i in chosen]
            negative=torch.tensor([train['rows'][i]['reject'] for i in chosen],dtype=torch.float32)
            with torch.no_grad():
                features=model.recognizer.model(torch.from_numpy(batch)).last_hidden_state
                full_prob=teacher(features).last_hidden_state
                copied=full_prob.index_select(-1,model.copiedOfficialIndices)
                excluded=(full_prob.sum(-1)-copied.sum(-1)).clamp_min(0)
                projected=torch.cat([copied,excluded[...,None]],dim=-1)
                del full_prob
            logits,reject_logit=model.from_features(features);log_prob=logits.log_softmax(-1)
            ctc=loss_fn(log_prob.transpose(0,1),torch.cat(labels),torch.full((len(chosen),),logits.shape[1],dtype=torch.long),torch.tensor([len(label) for label in labels]))
            bce=F.binary_cross_entropy_with_logits(reject_logit,negative)
            kl=F.kl_div(log_prob,projected,reduction='sum')/(len(chosen)*logits.shape[1])
            loss=ctc+spec['rejectLossWeight']*bce+spec['teacherKlWeight']*kl
            if not torch.isfinite(loss):raise ValueError('Nonfinite loss')
            optimizer.zero_grad(set_to_none=True);loss.backward();torch.nn.utils.clip_grad_norm_(model.parameters(),spec['gradientClip']);optimizer.step();losses.append([float(ctc.detach()),float(bce.detach()),float(kl.detach())])
            rss=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024
            if rss>spec['rssLimitMiB']:raise MemoryError('Own-process RSS budget exceeded')
            if offset%512==0:print(json.dumps({'stage':'training','epoch':epoch,'completed':offset+len(chosen),'ctc':losses[-1][0],'rejectBce':losses[-1][1],'teacherKl':losses[-1][2],'seconds':time.monotonic()-started,'peakRssMiB':rss}),flush=True)
        if stopped:break
        history.append({'epoch':epoch,'meanLosses':np.mean(losses,axis=0).tolist()})
        if epoch in spec['evaluateEpochs']:
            checkpoint=ROOT/f'epoch-{epoch}.safetensors';save_file({key:value.detach().contiguous().cpu() for key,value in model.state_dict().items()},str(checkpoint))
            result=evaluate(f'epoch-{epoch}',model,dev_values,development,DICTIONARY,trained_reject=True)
            candidates.append({'name':f'epoch-{epoch}','epoch':epoch,'path':str(checkpoint),'sha256':hashlib.sha256(checkpoint.read_bytes()).hexdigest(),'metrics':result})
    original={'name':'original-full18710','epoch':0,'path':'/workspace/scratch/ocr-training-assets/model.safetensors','sha256':BASE_SHA,'metrics':baseline}
    eligible=[original]+[candidate for candidate in candidates if candidate['metrics']['exact']>=baseline['exact'] and candidate['metrics']['incorrectAccepted']<=baseline['incorrectAccepted'] and candidate['metrics']['negativeAccepted']<=baseline['negativeAccepted']]
    selected=sorted(eligible,key=lambda candidate:(-candidate['metrics']['correctAccepted'],-candidate['metrics']['exact'],candidate['metrics']['incorrectAccepted'],candidate['metrics']['negativeAccepted'],candidate['metrics']['cer'],candidate['epoch']))[0]
    receipt={'protocolSha256':freeze['protocolSha256'],'original':original,'reducedUntrainedDiagnostic':reduced,'candidates':candidates,'selected':selected,'history':history,'budgetStopped':stopped,'trainingSeconds':time.monotonic()-started,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'status':'research-only/unqualified/inactive; development selection not full-document eligibility; final unopened'}
    save(ROOT/'selection.json',receipt);save(RESULTS/'selection.json',receipt)
    print(json.dumps({'stage':'selection','selected':selected['name'],'metrics':selected['metrics'],'budgetStopped':stopped}),flush=True)


if __name__=='__main__':main()
