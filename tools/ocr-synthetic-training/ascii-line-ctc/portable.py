"""Standard portable research bundle; model/reject scores remain unqualified."""
import hashlib
import json
import shutil
from pathlib import Path

import numpy as np
import torch
from safetensors.torch import load_file
import onnx

from data import ROOT,DICTIONARY,save_json
from model import LineCTC

RESULTS=Path('/workspace/scratch/takupoke-win/research-results/ocr-synthetic-training-20261013/ascii-line-ctc-v1')


def main():
    selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));chosen=selection['selected'];checkpoint=Path(chosen['checkpoint'])
    if hashlib.sha256(checkpoint.read_bytes()).hexdigest()!=chosen['sha256']:raise ValueError('Checkpoint changed')
    torch.set_num_threads(2);torch.set_num_interop_threads(1);model=LineCTC().eval();model.load_state_dict(load_file(str(checkpoint)))
    directory=ROOT/'portable';directory.mkdir(exist_ok=False);shutil.copyfile(checkpoint,directory/'weights.safetensors');shutil.copyfile(ROOT/'model.onnx',directory/'model.onnx');save_json(directory/'dictionary.json',DICTIONARY);protocol=json.loads((Path(__file__).parent/'protocol.json').read_text(encoding='utf-8'));save_json(directory/'protocol.json',protocol)
    save_json(directory/'preprocessing.json',{'namespace':protocol['namespace'],'input':protocol['input'],'coordinates':protocol['coordinates'],'dictionary':protocol['dictionary'],'acceptance':protocol['acceptance'],'outputs':{'probabilities':'float32[batch,timeSteps,97]','rejectProbability':'float32[batch], uncalibrated sigmoid score'},'limits':'Not interchangeable with Paddle BGR48. Blank/UNK are not fabricated source glyphs. Reject and source-support proof must be enforced before adoption; no Unicode/damage identity guarantee from crop alone.'})
    shutil.copyfile(Path(__file__).parent/'model.py',directory/'model_reference.py');shutil.copyfile(Path(__file__).parent/'data.py',directory/'preprocessing_reference.py');shutil.copyfile(ROOT/'assets/font-receipt.json',directory/'font-receipt.json');shutil.copyfile(ROOT/'pre-training-freeze.json',directory/'pre-training-freeze.json');shutil.copyfile(ROOT/'selection.json',directory/'selection.json')
    graph=onnx.load(directory/'model.onnx');domains=sorted({node.domain for node in graph.graph.node});assert domains==['']
    coreml={'status':'not attempted','deviceInference':'not tested; iOS repository untouched'}
    try:
        import coremltools as ct
        trace=torch.jit.trace(model.eval(),torch.zeros(1,3,64,320),check_trace=False)
        converted=ct.convert(trace,convert_to='mlprogram',inputs=[ct.TensorType(name='x',shape=(1,3,64,ct.RangeDim(lower_bound=320,upper_bound=960,default=320)),dtype=np.float32)],outputs=[ct.TensorType(name='probabilities',dtype=np.float32),ct.TensorType(name='rejectProbability',dtype=np.float32)],minimum_deployment_target=ct.target.iOS16,compute_precision=ct.precision.FLOAT32,skip_model_load=True)
        converted.short_description='Research-only baseline-preserving ASCII line CTC; no runtime qualification';converted.save(str(directory/'model.mlpackage'));coreml.update({'status':'converted-unqualified','minimumTarget':'iOS16','floatPrecision':'float32','input':'x [1,3,64,320..960]','outputs':['probabilities','rejectProbability']})
    except Exception as error:coreml.update({'status':'conversion-failed; standard ONNX retained','error':str(error),'type':type(error).__name__})
    save_json(RESULTS/'coreml.json',coreml)
    receipt={'namespace':protocol['namespace'],'status':'research-only; unqualified/notactivated','selected':chosen,'onnxDomains':domains,'parameters':selection['parameters'],'coreml':coreml,'weights':'New from-scratch synthetic-only model; no pretrained weight dependency. Public-font metadata/licenses retained externally. No real school data.','files':{str(path.relative_to(directory)):{'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()} for path in sorted(directory.rglob('*')) if path.is_file()}}
    save_json(directory/'provenance.json',receipt);save_json(RESULTS/'portable-receipt.json',receipt);print(json.dumps({'portable':str(directory),'files':len(receipt['files']),'coreml':coreml}),flush=True)


if __name__=='__main__':main()
