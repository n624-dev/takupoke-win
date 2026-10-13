"""Open reserved final only after immutable selection; no production adoption."""
import hashlib
import json
import random
import resource
import sys
import time
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
from PIL import Image,ImageDraw
from safetensors.torch import load_file
import torch

from data import ROOT,FONTS,DICTIONARY,generate,arrays,excluded_literals,line_text,font,tensor,save_json
from model import LineCTC
from train import evaluate
sys.path.insert(0,str(Path(__file__).parents[1]));from image_tensor import greedy

RESULTS=Path('/workspace/scratch/takupoke-win/research-results/ocr-synthetic-training-20261013/ascii-line-ctc-v1')


def outcome(pieces):return [{key:piece[key] for key in ['Text','Start','End']} for piece in pieces]


def make_documents(protocol,forbidden):
    directory=ROOT/'documents';directory.mkdir(exist_ok=False);rng=random.Random(protocol['final']['seed']+31);oracle=[];pixels=[];used=set(forbidden)
    for page in range(8):
        name=protocol['final']['fonts'][page%2];image=Image.new('RGB',(2083,883),'white');draw=ImageDraw.Draw(image)
        for x in range(1,2082,520):draw.line((x,1,x,881),fill='black',width=2)
        for y in range(1,882,110):draw.line((1,y,2081,y),fill='black',width=2)
        for row in range(8):
            for column in range(4):
                kind=['code','printable','lookalike','natural'][(row*4+column)%4]
                face=font(name,42)
                while True:
                    text=line_text(rng,kind)
                    if text not in used and face.getlength(text)<=490:break
                used.add(text);baseline=row*110+80+rng.randint(-5,5);draw.text((column*520+12,baseline),text,font=face,fill=rng.choice(['black',(0,0,90)]),anchor='ls')
                oracle.append({'page':page,'row':row,'column':column,'text':text,'font':name})
        path=directory/f'{page:02d}.png';image.save(path);pixels.append({'page':page,'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
    save_json(directory/'pixels.json',{'pages':pixels,'scope':'raster physical-grid segmentation only; no oracle supplied to recognition'});save_json(directory/'oracle.json',oracle)
    return directory


def physical_grid(image):
    dark=np.min(np.asarray(image.convert('RGB')),axis=2)<40
    def centers(values,threshold):
        indices=np.where(values>threshold)[0];groups=[]
        for index in indices:
            if not groups or index>groups[-1][-1]+1:groups.append([int(index)])
            else:groups[-1].append(int(index))
        return [int(round(float(np.mean(group)))) for group in groups]
    return centers(dark.sum(0),image.height*.8),centers(dark.sum(1),image.width*.8)


def document_recognition(model,directory):
    manifest=json.loads((directory/'pixels.json').read_text(encoding='utf-8'));records=[]
    with torch.inference_mode():
        for page in manifest['pages']:
            path=directory/page['file'];assert hashlib.sha256(path.read_bytes()).hexdigest()==page['sha256'];image=Image.open(path);xs,ys=physical_grid(image)
            for row,(top,bottom) in enumerate(zip(ys,ys[1:])):
                for column,(left,right) in enumerate(zip(xs,xs[1:])):
                    crop=(left+2,top+2,right-1,bottom-1);array,valid=tensor(image.crop(crop));prob,reject=model(torch.from_numpy(array[None]));pieces=greedy(prob[0].numpy(),DICTIONARY);text=''.join(p['Text'] for p in pieces);minimum=min((p['Confidence'] for p in pieces),default=0);supported=all(p['End']*array.shape[-1]<=valid*prob.shape[1] for p in pieces);accepted=bool(text) and '�' not in text and minimum>=.8 and float(reject[0])<=.01 and supported
                    records.append({'page':page['page'],'row':row,'column':column,'crop':crop,'prediction':text,'rejectProbability':float(reject[0]),'minimumConfidence':minimum,'supported':supported,'accepted':accepted,'pieces':pieces,'sourcePositions':[{'Text':p['Text'],'x':crop[0]+p['Start']*array.shape[-1]/(prob.shape[1]*valid)*(crop[2]-crop[0]),'right':crop[0]+p['End']*array.shape[-1]/(prob.shape[1]*valid)*(crop[2]-crop[0]),'y':crop[1],'height':crop[3]-crop[1]} for p in pieces]})
    save_json(RESULTS/'documents-unjudged.json',{'records':records,'scope':'Fixed model on detected physical raster grid cells; no source oracle supplied during inference; not production header/role parsing'})
    # Oracle is first opened only after complete raw outputs are persisted.
    gold={(r['page'],r['row'],r['column']):r for r in json.loads((directory/'oracle.json').read_text(encoding='utf-8'))};reports=[]
    for page in manifest['pages']:
        selected=[r for r in records if r['page']==page['page']];exact=sum(r['prediction']==gold[(r['page'],r['row'],r['column'])]['text'] for r in selected)
        correct_accepted=sum(r['accepted'] and r['prediction']==gold[(r['page'],r['row'],r['column'])]['text'] for r in selected)
        reports.append({'page':page['page'],'fields':len(selected),'expectedFields':32,'exact':exact,'rawCorrectionsNeeded':32-exact,'correctAccepted':correct_accepted,'notCorrectlyAcceptedFields':32-correct_accepted,'incorrectAccepted':sum(r['accepted'] and r['prediction']!=gold[(r['page'],r['row'],r['column'])]['text'] for r in selected),'withinThreeRawCorrections':len(selected)==32 and 32-exact<=3,'withinThreeFieldsNeedingReview':len(selected)==32 and 32-correct_accepted<=3})
    return {'pages':reports,'scope':'Standalone ASCII grids; no actual production headers/roles/body schema, no formal result, qualification or activation'}


def probes(model,protocol):
    rng=random.Random(protocol['final']['seed']+57);records=[]
    with torch.inference_mode():
        for name in protocol['final']['fonts']:
            for kind,text in [('blank',''),('short','1'),('short','I'),('short','l'),('short','0'),('short','O'),('mixed-cjk','Q風7'),('mixed-cjk','Q竹7'),('alias-latin','QO7'),('alias-cyrillic','QО7')]:
                image=Image.new('RGB',(300,100),'white');ImageDraw.Draw(image).text((12,76),text,font=font(name,48),fill='black',anchor='ls');array,valid=tensor(image);prob,reject=model(torch.from_numpy(array[None]));pieces=greedy(prob[0].numpy(),DICTIONARY)
                records.append({'font':name,'kind':kind,'text':text,'pixelsSha256':hashlib.sha256(np.asarray(image).tobytes()).hexdigest(),'prediction':''.join(p['Text'] for p in pieces),'rejectProbability':float(reject[0]),'minimumConfidence':min((p['Confidence'] for p in pieces),default=0),'pieces':pieces})
    return {'records':records,'pixelIdenticalAliasPairs':[name for name in protocol['final']['fonts'] if next(r for r in records if r['font']==name and r['kind']=='alias-latin')['pixelsSha256']==next(r for r in records if r['font']==name and r['kind']=='alias-cyrillic')['pixelsSha256']],'limits':'Already fixed final model. Short strings supply font/source diagnostics, not new literal novelty. Exact Latin/Cyrillic raster aliases cannot be rejected by crop-only Unicode recognition; original-source evidence required.'}


def main():
    RESULTS.mkdir(exist_ok=True,parents=True)
    if (RESULTS/'summary.json').exists():raise ValueError('Final already completed; no overwrite')
    torch.set_num_threads(2);torch.set_num_interop_threads(1)
    protocol=json.loads((Path(__file__).parent/'protocol.json').read_text(encoding='utf-8'));selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));selected=selection['selected'];checkpoint=Path(selected['checkpoint'])
    if hashlib.sha256(checkpoint.read_bytes()).hexdigest()!=selected['sha256']:raise ValueError('Selected checkpoint changed')
    if hashlib.sha256(json.dumps(protocol,sort_keys=True).encode('utf-8')).hexdigest()!=selection['protocolSha256']:raise ValueError('Protocol changed')
    forbidden=excluded_literals()
    for split in ['train','development']:forbidden.update(r['target'] for r in json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'))['rows'])
    manifest=generate(protocol,'final',forbidden);inputs=arrays(manifest);model=LineCTC().eval();model.load_state_dict(load_file(str(checkpoint)));result=evaluate(model,manifest,inputs);save_json(RESULTS/'final-records.json',result);print(json.dumps({'stage':'final','summary':result['summary']}),flush=True)
    forbidden.update(r['target'] for r in manifest['rows']);documents=document_recognition(model,make_documents(protocol,forbidden));save_json(RESULTS/'documents-summary.json',documents);probe=probes(model,protocol);save_json(RESULTS/'safety-probes.json',probe)
    onnx_path=ROOT/'model.onnx';torch.onnx.export(model.eval(),torch.from_numpy(inputs[0].astype(np.float32)[None]),str(onnx_path),input_names=['x'],output_names=['probabilities','rejectProbability'],dynamic_axes={'x':{0:'batch',3:'width'},'probabilities':{0:'batch',1:'steps'},'rejectProbability':{0:'batch'}},opset_version=17,dynamo=False);model.eval();onnx.checker.check_model(onnx.load(onnx_path))
    options=ort.SessionOptions();options.intra_op_num_threads=2;options.inter_op_num_threads=1;session=ort.InferenceSession(str(onnx_path),sess_options=options,providers=['CPUExecutionProvider']);parity=[]
    with torch.inference_mode():
        for row,array in zip(manifest['rows'],inputs):
            array=array.astype(np.float32)[None];actual,reject=session.run(None,{'x':array});reference,ref_reject=model(torch.from_numpy(array));a=greedy(actual[0],DICTIONARY);b=greedy(reference[0].numpy(),DICTIONARY);parity.append({'id':row['id'],'textAndIntervals':outcome(a)==outcome(b),'maximumProbabilityDifference':float(np.max(np.abs(actual-reference.numpy()))),'rejectDifference':float(np.max(np.abs(reject-ref_reject.numpy())))})
    save_json(RESULTS/'onnx-parity.json',{'rows':len(parity),'exact':sum(r['textAndIntervals'] for r in parity),'maximumProbabilityDifference':max(r['maximumProbabilityDifference'] for r in parity),'maximumRejectDifference':max(r['rejectDifference'] for r in parity),'records':parity})
    summary={'status':'research-only; unqualified/notactivated; iOS untouched','selected':selected,'parameters':selection['parameters'],'onnxBytes':onnx_path.stat().st_size,'onnxSha256':hashlib.sha256(onnx_path.read_bytes()).hexdigest(),'final':result['summary'],'documents':documents,'contradictionPixelIdenticalFonts':probe['pixelIdenticalAliasPairs'],'onnxExact':sum(r['textAndIntervals'] for r in parity),'onnxRows':len(parity),'trainingPeakRssMiB':max(r['maxRssMiB'] for r in selection['history']),'limits':'ASCII domain only; independent CJK/reject/position/native full-production qualification still required. Model confidence never proves complete original support or Unicode identity of visually identical scripts.'};save_json(RESULTS/'summary.json',summary);print(json.dumps(summary),flush=True)
    if summary['onnxExact']!=len(parity):raise ValueError('ONNX export changes decoder; no portability claim')


if __name__=='__main__':main()
