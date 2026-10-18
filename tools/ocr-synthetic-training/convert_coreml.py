"""Optional Linux CoreML packaging; conversion is not device qualification."""
import argparse
import hashlib
import json
import traceback
from pathlib import Path

import numpy as np
import torch
from transformers import PPOCRV6SmallRecForTextRecognition

from final_evaluate import Export, outcome
from corpus import load_arrays
from image_tensor import greedy
import yaml


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',required=True,type=Path);p.add_argument('--experiment',required=True,type=Path);p.add_argument('--results',required=True,type=Path);a=p.parse_args()
    if not (a.results/'final-summary.json').exists():raise ValueError('Final required')
    receipt_path=a.results/'coreml-conversion.json'
    if receipt_path.exists():raise ValueError('Conversion receipt exists')
    import coremltools as ct
    torch.set_num_threads(2);torch.set_num_interop_threads(1)
    selected=json.loads((a.experiment/'selection.json').read_text(encoding='utf-8'))['selected'];checkpoint=Path(selected['path'])
    if hashlib.sha256((checkpoint/'model.safetensors').read_bytes()).hexdigest()!=selected['sha256']:raise ValueError('Checkpoint hash changed')
    manifest=json.loads((a.experiment/'final'/'manifest.json').read_text(encoding='utf-8'));arrays=load_arrays(manifest,a.experiment/'final')
    dictionary=['']+yaml.safe_load((a.assets/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' ']
    model=PPOCRV6SmallRecForTextRecognition.from_pretrained(checkpoint,local_files_only=True,attn_implementation='eager').eval()
    traced=torch.jit.trace(Export(model).eval(),torch.from_numpy(arrays[0][None]),check_trace=False)
    # Verify dynamic traced Torch before handing it to another converter.
    parity=[]
    with torch.inference_mode():
        reference=PPOCRV6SmallRecForTextRecognition.from_pretrained(checkpoint,local_files_only=True).eval()
        for width in [320,481,640,960]:
            array=torch.zeros(1,3,48,width);array[:,:,:,:min(width,arrays[0].shape[-1])]=torch.from_numpy(arrays[0][None,:,:,:min(width,arrays[0].shape[-1])])
            expected=reference(pixel_values=array).last_hidden_state[0].numpy();actual=traced(array)[0].numpy()
            parity.append({'width':width,'exactTextAndIntervals':outcome(greedy(expected,dictionary))==outcome(greedy(actual,dictionary)),'maximumProbabilityDifference':float(np.max(np.abs(actual-expected)))})
    receipt={'coremltools':ct.__version__,'torch':torch.__version__,'checkpointSha256':selected['sha256'],'torchTracingParity':parity,'deviceValidation':'not performed; Linux has no CoreML runtime/iPhone; no speed, memory or prediction fidelity claim','status':'attempting'}
    if not all(r['exactTextAndIntervals'] for r in parity):raise ValueError('Traced eager Torch changed decoder intervals')
    try:
        converted=ct.convert(traced,convert_to='mlprogram',inputs=[ct.TensorType(name='x',shape=(1,3,48,ct.RangeDim(lower_bound=320,upper_bound=960,default=320)),dtype=np.float32)],outputs=[ct.TensorType(name='probabilities',dtype=np.float32)],minimum_deployment_target=ct.target.iOS16,compute_precision=ct.precision.FLOAT32,skip_model_load=True)
        converted.short_description='Research-only fictional-synthetic PP-OCRv6 small recognizer; no device qualification'
        converted.author='PP-OCR authors; local synthetic adaptation experiment'
        converted.license='Apache-2.0 (base model); see provenance package'
        converted.input_description['x']='BGR normalized float32 [1,3,48,width], width320..960; original-pixel bilinear+uint8 rounding and zero-normalized padding outside model'
        converted.output_description['probabilities']='Full 18710-class CTC probabilities; external greedy CTC and source-coordinate mapping required'
        path=a.experiment/'selected.mlpackage';converted.save(str(path))
        files={str(f.relative_to(path)):{'bytes':f.stat().st_size,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()} for f in sorted(path.rglob('*')) if f.is_file()}
        receipt.update({'status':'converted-unqualified','format':'CoreML ML Program','minimumDeploymentTarget':'iOS16','computePrecision':'float32','inputShape':'[1,3,48,320..960]','files':files,'totalBytes':sum(v['bytes'] for v in files.values())})
    except Exception as error:
        receipt.update({'status':'conversion-failed; standard ONNX remains portable artifact','error':str(error),'exceptionType':type(error).__name__});traceback.print_exc()
    receipt_path.write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n');print(json.dumps(receipt,ensure_ascii=False),flush=True)


if __name__=='__main__':main()
