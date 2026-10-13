"""Verify export with fresh eval-only Torch; no literal gold is consulted."""
import argparse
import hashlib
import json
import time
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch
from transformers import PPOCRV6SmallRecForTextRecognition
import yaml

from corpus import load_arrays
from image_tensor import greedy
from final_evaluate import outcome


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',required=True,type=Path);p.add_argument('--experiment',required=True,type=Path);p.add_argument('--results',required=True,type=Path);a=p.parse_args()
    torch.set_num_threads(2);torch.set_num_interop_threads(1)
    selection=json.loads((a.experiment/'selection.json').read_text(encoding='utf-8'));chosen=Path(selection['selected']['path'])
    if hashlib.sha256((chosen/'model.safetensors').read_bytes()).hexdigest()!=selection['selected']['sha256']:raise ValueError('Selected checkpoint changed')
    model=PPOCRV6SmallRecForTextRecognition.from_pretrained(chosen,local_files_only=True).eval()
    dictionary=['']+yaml.safe_load((a.assets/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' ']
    manifest=json.loads((a.experiment/'final'/'manifest.json').read_text(encoding='utf-8'));arrays=load_arrays(manifest,a.experiment/'final')
    options=ort.SessionOptions();options.intra_op_num_threads=2;options.inter_op_num_threads=1
    exported=a.experiment/'selected.onnx';session=ort.InferenceSession(str(exported),sess_options=options,providers=['CPUExecutionProvider'])
    records=[];dynamic=[];start=time.monotonic()
    with torch.inference_mode():
        for array,row in zip(arrays,manifest['rows']):
            actual=session.run(None,{'x':array[None]})[0][0];reference=model(pixel_values=torch.from_numpy(array[None])).last_hidden_state[0].numpy()
            pieces=greedy(actual,dictionary)
            records.append({'id':row['id'],'width':array.shape[-1],'exactTextAndIntervals':outcome(pieces)==outcome(greedy(reference,dictionary)),'maximumProbabilityDifference':float(np.max(np.abs(actual-reference))),'unsupportedPadding':any(piece['End']*array.shape[-1]>row['validWidth']*actual.shape[0] for piece in pieces)})
            if len(records)%96==0:print({'completed':len(records),'rows':len(arrays)},flush=True)
        for width in [320,481,640,960]:
            array=np.zeros((2,3,48,width),dtype=np.float32);array[:,:,:,:min(width,arrays[0].shape[-1])]=arrays[0][None,:,:,:min(width,arrays[0].shape[-1])]
            actual=session.run(None,{'x':array})[0];reference=model(pixel_values=torch.from_numpy(array)).last_hidden_state.numpy()
            dynamic.append({'width':width,'batch':2,'shape':list(actual.shape),'exactTextAndIntervals':all(outcome(greedy(x,dictionary))==outcome(greedy(y,dictionary)) for x,y in zip(actual,reference)),'maximumProbabilityDifference':float(np.max(np.abs(actual-reference)))})
    if model.training or any(module.training for module in model.modules()):raise ValueError('Evaluation mode changed')
    receipt={'rows':len(records),'exactTextAndIntervals':sum(r['exactTextAndIntervals'] for r in records),'maximumProbabilityDifference':max(r['maximumProbabilityDifference'] for r in records),'dynamicProbes':dynamic,'onnxBytes':exported.stat().st_size,'onnxSha256':hashlib.sha256(exported.read_bytes()).hexdigest(),'elapsedSecondsWithConcurrentWork':time.monotonic()-start,'records':records,'torchEvaluationModeVerified':True,'selectedRowsTouchingPadding':sum(r['unsupportedPadding'] for r in records),'comparisonCorrection':'Fresh eval-only Torch reload. Prior post-export Torch probability comparison invalid because legacy exporter restored a train-mode wrapper recursively. Model and checkpoint selection unchanged; no gold used.'}
    previous=a.results/'onnx-parity.json';old=json.loads(previous.read_text(encoding='utf-8'));old['status']='INVALID post-export Torch mode comparison; retained for audit; original ONNX decoding vs pre-export eval text/intervals was valid'
    invalid=a.results/'onnx-parity-invalid-torch-mode.json'
    if invalid.exists():raise ValueError('Correction already recorded')
    invalid.write_text(json.dumps(old,indent=2)+'\n', encoding='utf-8',newline='\n');previous.write_text(json.dumps(receipt,indent=2)+'\n', encoding='utf-8',newline='\n')
    summary_path=a.results/'final-summary.json';summary=json.loads(summary_path.read_text(encoding='utf-8'));summary['onnxParity']={k:v for k,v in receipt.items() if k!='records'};summary_path.write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n')
    print(json.dumps({k:v for k,v in receipt.items() if k!='records'}),flush=True)
    if receipt['exactTextAndIntervals']!=len(records) or not all(r['exactTextAndIntervals'] for r in dynamic):raise ValueError('Export fidelity failed')


if __name__=='__main__':main()
