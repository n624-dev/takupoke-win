"""Fresh-process CPU/ONNX memory profile. Never reads literal gold fields."""
import argparse
import hashlib
import json
import platform
import resource
import statistics
import time
from pathlib import Path

import onnxruntime as ort
from PIL import Image

from image_tensor import tensor
from image_tensor import greedy


def main():
    p=argparse.ArgumentParser();p.add_argument('--model',required=True,type=Path);p.add_argument('--pixels',required=True,type=Path);p.add_argument('--output',required=True,type=Path);p.add_argument('--dictionary',type=Path);p.add_argument('--reference-outputs',type=Path);a=p.parse_args()
    if a.output.exists():raise ValueError('Profile receipt exists')
    options=ort.SessionOptions();options.intra_op_num_threads=2;options.inter_op_num_threads=1
    start=time.monotonic();session=ort.InferenceSession(str(a.model),sess_options=options,providers=['CPUExecutionProvider']);load=time.monotonic()-start
    manifest=json.loads((a.pixels/'manifest.json').read_text(encoding='utf-8'));records=[]
    dictionary=json.loads(a.dictionary.read_text(encoding='utf-8')) if a.dictionary else None
    references={r['id']:r for r in json.loads(a.reference_outputs.read_text(encoding='utf-8'))['records']} if a.reference_outputs else None
    if references and not dictionary:raise ValueError('Reference comparison needs dictionary')
    for row in manifest['rows']:
        path=a.pixels/row['file']
        if hashlib.sha256(path.read_bytes()).hexdigest()!=row['sha256']:raise ValueError('Pixel identity mismatch')
        array,valid=tensor(Image.open(path));start=time.monotonic();result=session.run(None,{'x':array[None]})[0];elapsed=time.monotonic()-start
        record={'id':row['id'],'width':array.shape[-1],'validWidth':valid,'outputShape':list(result.shape),'inferenceMilliseconds':elapsed*1000}
        if references:
            pieces=greedy(result[0],dictionary);reference=references[row['id']]['pieces'];fields=lambda ps:[{k:piece[k] for k in ['Text','Start','End']} for piece in ps]
            minimum=min((piece['Confidence'] for piece in pieces),default=None);reference_minimum=min((piece['Confidence'] for piece in reference),default=None)
            above=lambda value:value is not None and value>=.8
            record.update({'exactTextAndIntervals':fields(pieces)==fields(reference),'minimumConfidence':minimum,'referenceMinimumConfidence':reference_minimum,'confidenceFloor08Agreement':above(minimum)==above(reference_minimum),'maximumPieceConfidenceDifference':max((abs(x['Confidence']-y['Confidence']) for x,y in zip(pieces,reference)),default=0),'unsupportedPadding':any(piece['End']*array.shape[-1]>valid*result.shape[1] for piece in pieces)})
        records.append(record)
    times=[r['inferenceMilliseconds'] for r in records]
    receipt={'platform':platform.platform(),'architecture':platform.machine(),'onnxruntime':ort.__version__,'provider':session.get_providers(),'threads':2,'modelBytes':a.model.stat().st_size,'modelSha256':hashlib.sha256(a.model.read_bytes()).hexdigest(),'rows':len(records),'loadSeconds':load,'medianInferenceMilliseconds':statistics.median(times),'maximumInferenceMilliseconds':max(times),'maxRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'timingLimit':'Concurrent CPU work; Linux measurement; no isolated-speed or native-Windows memory claim','records':records}
    if references:receipt.update({'exactTextAndIntervals':sum(r['exactTextAndIntervals'] for r in records),'confidenceFloor08Agreement':sum(r['confidenceFloor08Agreement'] for r in records),'maximumPieceConfidenceDifference':max(r['maximumPieceConfidenceDifference'] for r in records),'selectedRowsTouchingPadding':sum(r['unsupportedPadding'] for r in records)})
    a.output.write_text(json.dumps(receipt,indent=2)+'\n', encoding='utf-8',newline='\n');print(json.dumps({k:v for k,v in receipt.items() if k!='records'}),flush=True)


if __name__=='__main__':main()
