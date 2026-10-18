"""One predeclared original-ink framing condition; no pixel/text repair."""
import hashlib
import json
import math
import resource
import sys
import time

import cv2
import numpy as np
from PIL import Image
import torch

from baseline import ROOT,DEV,RESULTS,load,distance,greedy


def source_tensor(path):
    colour=cv2.imread(str(path),cv2.IMREAD_COLOR);gray=cv2.imread(str(path),cv2.IMREAD_GRAYSCALE)
    foreground=np.any(colour!=255,axis=2);ys,xs=np.where(foreground)
    height,width=gray.shape
    if len(xs):left=max(0,int(xs.min())-2);right=min(width,int(xs.max())+3);top=max(0,int(ys.min())-2);bottom=min(height,int(ys.max())+3)
    else:left,top,right,bottom=0,0,width,height
    if int(foreground.sum())!=int(foreground[top:bottom,left:right].sum()):raise ValueError('Original nonwhite source lost')
    crop=gray[top:bottom,left:right];h,w=crop.shape;input_width=max(64,math.ceil(w/h)*64)
    if input_width>960:raise ValueError('Declared line capacity exceeded')
    valid=min(input_width,math.ceil(64*w/h));image=Image.fromarray(crop).resize((valid,64),Image.Resampling.BICUBIC);values=np.asarray(image,dtype=np.float32)/np.float32(255);values=(values-np.float32(.5))/np.float32(.5);output=np.empty((1,1,64,input_width),dtype=np.float32);output[0,0,:,:valid]=values;output[0,0,:,valid:]=values[:,-1,None]
    return output,valid,{'crop':[left,top,right,bottom],'originalShape':[height,width],'nonwhiteSamples':int(foreground.sum()),'allOriginalNonwhiteRetained':True,'borderInk':bool(foreground[0].any() or foreground[-1].any() or foreground[:,0].any() or foreground[:,-1].any()),'sourceCropSha256':hashlib.sha256(colour[top:bottom,left:right].tobytes()).hexdigest()}


def main():
    if (RESULTS/'framed-summary.json').exists():raise ValueError('Fixed condition already consumed')
    torch.set_num_threads(2);torch.set_num_interop_threads(1);model,dictionary,receipt=load();manifest=json.loads((DEV/'manifest.json').read_text(encoding='utf-8'))
    freeze={'condition':'All RGB samples !=white255 bounding rectangle +2originalpx, clamped only to original canvas. Every nonwhite sample retained. No RGB replacement, denoising, internal-gap change, per-glyph resize, language prior, score/oracle framing choice. Then same official first-pass Gray64 AlignCollate/PILBICUBIC/last-column pad/FP32/greedy.','modelSha256':receipt['pthSha256'],'modelDictionary':dictionary,'pixelsManifestSha256':hashlib.sha256((DEV/'manifest.json').read_bytes()).hexdigest(),'sources':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in __import__('pathlib').Path(__file__).parent.glob('*.py')},'scope':'Second declared condition on consumed development. Not new held-out accuracy, final, output correction or activation.','calls':len(manifest['rows'])}
    (RESULTS/'framed-freeze.json').write_text(json.dumps(freeze,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n');records=[];start=time.monotonic();raw=RESULTS/'framed-unjudged.jsonl'
    with raw.open('x',encoding='utf-8',newline='\n') as stream,torch.inference_mode():
        for row in manifest['rows']:
            path=DEV/row['file']
            if hashlib.sha256(path.read_bytes()).hexdigest()!=row['sha256']:raise ValueError('Original pixel identity changed')
            array,valid,source=source_tensor(path);timer=time.monotonic();prob=model(torch.from_numpy(array),None).softmax(-1)[0].numpy();elapsed=time.monotonic()-timer;pieces=greedy(prob,dictionary);text=''.join(p['Text'] for p in pieces);minimum=min((p['Confidence'] for p in pieces),default=0);supported=all(p['End']*array.shape[-1]<=valid*prob.shape[0] for p in pieces);accepted=bool(text) and all(32<=ord(char)<=126 for char in text) and minimum>=.8 and supported
            record={'id':row['id'],'prediction':text,'pieces':pieces,'minimumConfidence':minimum,'supported':supported,'accepted':accepted,'source':source,'inputWidth':array.shape[-1],'validWidth':valid,'timeSteps':prob.shape[0],'inferenceMilliseconds':elapsed*1000};records.append(record);stream.write(json.dumps(record,ensure_ascii=False)+'\n');stream.flush()
    # Literal comparison occurs only after the full unjudged output closes.
    lookup={r['id']:r for r in manifest['rows']};positive=[r for r in records if not lookup[r['id']]['reject']];negative=[r for r in records if lookup[r['id']]['reject']];exact=lambda r:r['prediction']==lookup[r['id']]['text']
    summary={'status':'research-only; unqualified/notactivated','scope':freeze['scope'],'positiveRows':len(positive),'exact':sum(exact(r) for r in positive),'correctAccepted':sum(exact(r) and r['accepted'] for r in positive),'incorrectAccepted':sum(not exact(r) and r['accepted'] for r in positive),'cer':sum(distance(lookup[r['id']]['text'],r['prediction']) for r in positive)/sum(len(lookup[r['id']]['text']) for r in positive),'negativeRows':len(negative),'negativeAccepted':sum(r['accepted'] for r in negative),'allOriginalNonwhiteRetained':sum(r['source']['allOriginalNonwhiteRetained'] for r in records),'groups':{kind:{'rows':len(group),'exact':sum(not lookup[r['id']]['reject'] and exact(r) for r in group),'accepted':sum(r['accepted'] for r in group)} for kind in sorted({r['kind'] for r in manifest['rows']}) for group in [[r for r in records if lookup[r['id']]['kind']==kind]]},'rawSha256':hashlib.sha256(raw.read_bytes()).hexdigest(),'elapsedSeconds':time.monotonic()-start,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'limits':'Crop-only unknown/damage/script rejection is not established. Original border/contact and full-line/cell provenance remain independent requirements. No formal headers, documents, nativeWindows/iOS or final credit.'}
    (RESULTS/'framed-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(summary),flush=True)


if __name__=='__main__':main()
