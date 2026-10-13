"""Open final pixels only after selection, export ONNX and compare fixed outputs."""
import argparse
import hashlib
import json
import resource
import time
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
import torch
from transformers import PPOCRV6SmallRecForTextRecognition
import yaml

from corpus import generate, load_arrays
from image_tensor import greedy
from train import evaluate


class Export(torch.nn.Module):
    def __init__(self, model):
        super().__init__();self.model=model
    def forward(self,x):
        return self.model(pixel_values=x).last_hidden_state


def outcome(pieces):
    return [{key:piece[key] for key in ['Text','Start','End']} for piece in pieces]


def confidence_metrics(result):
    for row in result['records']:
        row['minimumConfidence']=min((piece['Confidence'] for piece in row['pieces']),default=None)
    for key,group in result['groups'].items():
        rows=result['records'] if key=='all' else [r for r in result['records'] if r['category']==key or r['font']==key]
        above=lambda row:row['minimumConfidence'] is not None and row['minimumConfidence']>=.8
        group.update({'correctAtFloor08':sum(r['exact'] and above(r) for r in rows),'incorrectAtFloor08':sum(not r['exact'] and above(r) for r in rows),'belowFloor08OrEmpty':sum(not above(r) for r in rows)})
    return result


def controls(model,arrays,rows,dictionary):
    result=evaluate(model,arrays,rows,dictionary)
    for group in result['groups'].values():
        group.pop('exact');group.pop('cer')
    for category in ['blank','clipped','multiline']:
        records=[r for r in result['records'] if r['category']==category]
        result['groups'][category].update({'nonemptyOutputs':sum(bool(r['prediction']) for r in records),'outputsWithAnyCharacterConfidenceAtLeast099':sum(any(p['Confidence']>=.99 for p in r['pieces']) for r in records),'nonemptyOutputsWithMinimumConfidenceAtLeast08':sum(bool(r['pieces']) and min(p['Confidence'] for p in r['pieces'])>=.8 for r in records)})
    result['interpretation']='Recognizer alone cannot establish damage/multiline rejection. Clipped/multiline records are probes, never scored as fully recoverable text.'
    return result


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--assets',required=True,type=Path);parser.add_argument('--experiment',required=True,type=Path);parser.add_argument('--results',required=True,type=Path);args=parser.parse_args()
    args.results.mkdir(exist_ok=True,parents=True)
    if (args.results/'final-summary.json').exists():raise ValueError('Final already opened; do not overwrite')
    torch.set_num_threads(2);torch.set_num_interop_threads(1)
    protocol=json.loads((Path(__file__).parent/'protocol.json').read_text(encoding='utf-8'));selection=json.loads((args.experiment/'selection.json').read_text(encoding='utf-8'));chosen=selection['selected'];path=Path(chosen['path'])
    if hashlib.sha256((path/'model.safetensors').read_bytes()).hexdigest()!=chosen['sha256']:raise ValueError('Selected checkpoint hash changed')
    if hashlib.sha256(json.dumps(protocol,sort_keys=True).encode()).hexdigest()!=selection['protocolSha256']:raise ValueError('Protocol changed')
    forbidden=[]
    for split in ['train','development']:forbidden.extend(row['text'] for row in json.loads((args.experiment/split/'manifest.json').read_text(encoding='utf-8'))['rows'])
    manifest=generate(protocol,'final',args.experiment/'final',forbidden);arrays=load_arrays(manifest,args.experiment/'final');control_arrays=load_arrays(manifest,args.experiment/'final',True)
    dictionary=['']+yaml.safe_load((args.assets/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' ']
    baseline=PPOCRV6SmallRecForTextRecognition.from_pretrained(args.assets,local_files_only=True).eval();trained=PPOCRV6SmallRecForTextRecognition.from_pretrained(path,local_files_only=True).eval()
    results={}
    for name,model in [('baseline',baseline),('selected',trained)]:
        result=confidence_metrics(evaluate(model,arrays,manifest['rows'],dictionary));results[name]=result
        (args.results/f'final-{name}-records.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n')
        probe=controls(model,control_arrays,manifest['controls'],dictionary);(args.results/f'final-{name}-controls.json').write_text(json.dumps(probe,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n')
        print(json.dumps({'stage':'final','model':name,'groups':result['groups'],'controls':probe['groups']},ensure_ascii=False),flush=True)
    exported=args.experiment/'selected.onnx'
    # Legacy export yields a single portable artifact, not external data/weights.
    # The wrapper must also be eval: legacy export restores the wrapper's
    # original mode recursively, otherwise it changes the child back to train.
    torch.onnx.export(Export(trained).eval(),torch.from_numpy(arrays[0][None]),str(exported),input_names=['x'],output_names=['probabilities'],dynamic_axes={'x':{0:'batch',3:'width'},'probabilities':{0:'batch',1:'steps'}},opset_version=17,dynamo=False)
    trained.eval()
    graph=onnx.load(exported);onnx.checker.check_model(graph)
    options=ort.SessionOptions();options.intra_op_num_threads=2;options.inter_op_num_threads=1
    session=ort.InferenceSession(str(exported),sess_options=options,providers=['CPUExecutionProvider'])
    parity=[];start=time.monotonic()
    for array,row,reference in zip(arrays,manifest['rows'],results['selected']['records']):
        prob=session.run(None,{'x':array[None]})[0][0];pieces=greedy(prob,dictionary)
        with torch.inference_mode():expected=trained(pixel_values=torch.from_numpy(array[None])).last_hidden_state[0].numpy()
        parity.append({'id':row['id'],'width':array.shape[-1],'exactTextAndIntervals':outcome(pieces)==outcome(reference['pieces']),'maximumProbabilityDifference':float(np.max(np.abs(prob-expected)))})
    # Explicit dynamic-width and batch probes extend beyond generated widths.
    dynamic=[]
    for width in [320,481,640,960]:
        array=np.zeros((2,3,48,width),dtype=np.float32);array[:,:,:,:min(width,arrays[0].shape[-1])]=arrays[0][None,:,:,:min(width,arrays[0].shape[-1])]
        actual=session.run(None,{'x':array})[0]
        with torch.inference_mode():reference=trained(pixel_values=torch.from_numpy(array)).last_hidden_state.numpy()
        dynamic.append({'width':width,'batch':2,'shape':list(actual.shape),'exactTextAndIntervals':all(outcome(greedy(a,dictionary))==outcome(greedy(b,dictionary)) for a,b in zip(actual,reference)),'maximumProbabilityDifference':float(np.max(np.abs(actual-reference)))})
    parity_receipt={'rows':len(parity),'exactTextAndIntervals':sum(p['exactTextAndIntervals'] for p in parity),'maximumProbabilityDifference':max(p['maximumProbabilityDifference'] for p in parity),'dynamicProbes':dynamic,'onnxBytes':exported.stat().st_size,'onnxSha256':hashlib.sha256(exported.read_bytes()).hexdigest(),'elapsedSecondsWithConcurrentWork':time.monotonic()-start,'records':parity}
    (args.results/'onnx-parity.json').write_text(json.dumps(parity_receipt,indent=2)+'\n', encoding='utf-8',newline='\n')
    summary={'status':'research-only; not qualified, not activated','selection':chosen,'protocolSha256':selection['protocolSha256'],'trainRows':protocol['train']['rows'],'developmentRows':protocol['development']['rows'],'finalGroups':{k:v['groups'] for k,v in results.items()},'improvements':sum(not a['exact'] and b['exact'] for a,b in zip(results['baseline']['records'],results['selected']['records'])),'regressions':sum(a['exact'] and not b['exact'] for a,b in zip(results['baseline']['records'],results['selected']['records'])),'onnxParity':{k:v for k,v in parity_receipt.items() if k!='records'},'trainingMaxRssMiB':selection['maxRssMiB'],'evaluationMaxRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'finalPixelManifestSha256':hashlib.sha256((args.experiment/'final'/'manifest.json').read_bytes()).hexdigest(),'limits':'Synthetic crop evaluation cannot establish full-document OCR, position/cell membership, validator safety, damage rejection, <=3 corrections, arbitrary school-independent unknown words, or broad 18,710-class regression coverage.'}
    (args.results/'final-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n');print(json.dumps(summary,ensure_ascii=False),flush=True)
    if len(parity)!=parity_receipt['exactTextAndIntervals'] or not all(x['exactTextAndIntervals'] for x in dynamic):raise ValueError('ONNX decoder differs; no adoption')


if __name__=='__main__':main()
