"""Oracle-free medium feature/head extraction with full-vocabulary fidelity."""
import base64
import copy
import hashlib
import io
import json
from pathlib import Path
import resource
import sys
import time

import numpy as np
import onnx
from onnx import helper,numpy_helper,TensorProto
import onnxruntime as ort
from PIL import Image
from safetensors.torch import save_file
import torch
import torch.nn.functional as F

sys.path.insert(0,str(Path(__file__).parents[1]))
from image_tensor import tensor,greedy

REPO=Path('/workspace/scratch/takupoke-win')
ROOT=Path('/workspace/scratch/ascii-paddle-medium-features-20261013-v1')
RESULTS=REPO/'research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-features-v1'
MODEL=Path('/workspace/scratch/ocr-frozen-20261013/v6-medium.onnx')
MODEL_SHA='9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba'
DICT=Path('/workspace/scratch/ocr-frozen-20261013/v6-medium-dictionary.json')
PIXELS=REPO/'tools/vision-ocr-comparison/fictional-pixels.json'
PIXELS_SHA='6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff'
ORDINALS=[f'n{i:03d}' for i in [*range(72,96),*range(168,192)]]
FEATURE='p2o.pd_op.transpose.8.0'
LOGITS='p2o.pd_op.add.84.0'


def save(path,value):
    Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


def framed(row):
    payload=base64.b64decode(row['pngBase64'],validate=True)
    assert hashlib.sha256(payload).hexdigest()==row['pngSha256']
    image=Image.open(io.BytesIO(payload)).convert('RGB');rgb=np.asarray(image)
    assert (image.width,image.height)==(row['width'],row['height'])
    bgra=np.concatenate([rgb[:,:,::-1],np.full((*rgb.shape[:2],1),255,dtype=np.uint8)],axis=2)
    assert hashlib.sha256(bgra.tobytes()).hexdigest()==row['bgraSha256']
    foreground=np.any(rgb!=255,axis=2);ys,xs=np.where(foreground)
    if len(xs):box=(max(0,int(xs.min())-2),max(0,int(ys.min())-2),min(image.width,int(xs.max())+3),min(image.height,int(ys.max())+3))
    else:box=(0,0,image.width,image.height)
    crop=image.crop(box);assert int(foreground.sum())==int(np.any(np.asarray(crop)!=255,axis=2).sum())
    value,valid=tensor(crop)
    return value[None],valid,{'crop':list(box),'originalWidth':image.width,'originalHeight':image.height,'nonwhiteSamples':int(foreground.sum()),'allNonwhiteRetained':True,'pngSha256':row['pngSha256'],'framedRgbSha256':hashlib.sha256(np.asarray(crop).tobytes()).hexdigest()}


def session(path):
    options=ort.SessionOptions();options.intra_op_num_threads=2;options.inter_op_num_threads=1
    return ort.InferenceSession(str(path),sess_options=options,providers=['CPUExecutionProvider'])


def decoded(probability,dictionary):
    pieces=greedy(probability,dictionary)
    return {'text':''.join(piece['Text'] for piece in pieces),'intervals':[[piece['Text'],piece['Start'],piece['End']] for piece in pieces],'pieces':pieces}


def main():
    ROOT.mkdir(parents=True,exist_ok=True);RESULTS.mkdir(parents=True,exist_ok=True)
    if (RESULTS/'freeze.json').exists():raise ValueError('Fixed fidelity check already started; no overwrite')
    assert hashlib.sha256(MODEL.read_bytes()).hexdigest()==MODEL_SHA
    assert hashlib.sha256(PIXELS.read_bytes()).hexdigest()==PIXELS_SHA
    dictionary=json.loads(DICT.read_text(encoding='utf-8'));assert len(dictionary)==len(set(dictionary))==18710 and dictionary[0]==''
    torch.set_num_threads(2);torch.set_num_interop_threads(1)
    model=onnx.load(str(MODEL));assert all(node.domain=='' for node in model.graph.node)
    initializers={value.name:value for value in model.graph.initializer}
    tail=model.graph.node[-4:]
    assert [node.op_type for node in tail]==['MatMul','Add','Identity','Softmax']
    assert list(tail[0].input)==[FEATURE,'linear_8.w_0']
    assert list(tail[1].input)==['p2o.pd_op.matmul.12.0','linear_8.b_0']
    assert list(tail[-1].input)==[LOGITS] and list(tail[-1].output)==['fetch_name_0']
    weight=numpy_helper.to_array(initializers['linear_8.w_0']).copy();bias=numpy_helper.to_array(initializers['linear_8.b_0']).copy()
    assert weight.shape==(192,18710) and bias.shape==(18710,)
    head_weight=torch.from_numpy(weight.T.copy());head_bias=torch.from_numpy(bias)
    save_file({'weight':head_weight,'bias':head_bias},str(ROOT/'original-full-ctc-head.safetensors'))
    # Standard graph output additions expose exactly the original tensors.
    model.graph.output.extend([helper.make_tensor_value_info(FEATURE,TensorProto.FLOAT,['batch','time',192]),helper.make_tensor_value_info(LOGITS,TensorProto.FLOAT,['batch','time',18710])])
    onnx.checker.check_model(model)
    augmented=ROOT/'medium-feature-observer.onnx';onnx.save(model,str(augmented))
    # Keep the same frozen CNN/SVTR subgraph; remove the terminal classifier only.
    features=copy.deepcopy(model);del features.graph.node[-4:];del features.graph.output[:]
    features.graph.output.append(helper.make_tensor_value_info(FEATURE,TensorProto.FLOAT,['batch','time',192]))
    kept=[value for value in features.graph.initializer if value.name not in ['linear_8.w_0','linear_8.b_0']]
    del features.graph.initializer[:];features.graph.initializer.extend(kept)
    onnx.checker.check_model(features)
    trunk=ROOT/'medium-frozen-features.onnx';onnx.save(features,str(trunk))
    packet=json.loads(PIXELS.read_text(encoding='utf-8'));lookup={row['id']:row for row in packet['rows']}
    freeze={'namespace':'research-ascii-paddle-medium-features-v1','scope':'Initialization/feature extraction fidelity only on fixed consumed ordinals; no oracle access, quality score, training, final or activation','ordinals':ORDINALS,'condition':'All-original-RGB-nonwhite bbox+2originalpx, source preserved; native BGR48 center-bilinear/uint8round/norm0pad width320..960, original18710 dictionary/greedy','modelSha256':MODEL_SHA,'modelBytes':MODEL.stat().st_size,'dictionarySha256':hashlib.sha256(DICT.read_bytes()).hexdigest(),'pixelsSha256':PIXELS_SHA,'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'preprocessingSha256':hashlib.sha256((Path(__file__).parents[1]/'image_tensor.py').read_bytes()).hexdigest(),'featureName':FEATURE,'featureChannels':192,'logitsName':LOGITS,'originalInput':'x','originalProbabilities':'fetch_name_0','opsets':[(item.domain,item.version) for item in model.opset_import],'domains':sorted({node.domain for node in model.graph.node}),'observerSha256':hashlib.sha256(augmented.read_bytes()).hexdigest(),'trunkSha256':hashlib.sha256(trunk.read_bytes()).hexdigest(),'headSha256':hashlib.sha256((ROOT/'original-full-ctc-head.safetensors').read_bytes()).hexdigest(),'headCopy':'Original MatMul W[192,18710] transposed into torchLinear W[18710,192], original bias exactly copied; no row removal/renormalization','thresholds':'Record exact decoded text/interval/floor.8 parity; floating differences retained, not rounded or repaired'}
    save(RESULTS/'freeze.json',freeze)
    original=session(MODEL);observer=session(augmented);feature_session=session(trunk)
    maxima={key:0.0 for key in ['observerVsOriginalProb','torchVsOriginalLogits','torchVsOriginalProb','featureTrunkVsObserver','maxEmittedConfidenceDifference']};same={'observerTextIntervals':0,'torchTextIntervals':0,'floor08Decisions':0,'allSourceNonwhiteRetained':0};started=time.monotonic();raw=RESULTS/'unjudged.jsonl'
    with raw.open('x',encoding='utf-8',newline='\n') as stream,torch.inference_mode():
        for ordinal in ORDINALS:
            value,valid,source=framed(lookup[ordinal]);reference=original.run(None,{'x':value})[0];observed,feature,logits=observer.run(None,{'x':value});only_feature=feature_session.run(None,{'x':value})[0]
            recomputed=F.linear(torch.from_numpy(only_feature),head_weight,head_bias);probability=recomputed.softmax(-1).numpy()
            assert feature.shape==only_feature.shape and feature.shape[-1]==192
            maxima['observerVsOriginalProb']=max(maxima['observerVsOriginalProb'],float(np.max(np.abs(observed-reference))))
            maxima['torchVsOriginalLogits']=max(maxima['torchVsOriginalLogits'],float(np.max(np.abs(recomputed.numpy()-logits))))
            maxima['torchVsOriginalProb']=max(maxima['torchVsOriginalProb'],float(np.max(np.abs(probability-reference))))
            maxima['featureTrunkVsObserver']=max(maxima['featureTrunkVsObserver'],float(np.max(np.abs(only_feature-feature))))
            a=decoded(reference[0],dictionary);b=decoded(observed[0],dictionary);c=decoded(probability[0],dictionary)
            same['observerTextIntervals']+=a['intervals']==b['intervals'];same['torchTextIntervals']+=a['intervals']==c['intervals']
            supported=lambda pieces:all(piece['End']*value.shape[-1]<=valid*reference.shape[1] for piece in pieces)
            minimum=lambda pieces:min((piece['Confidence'] for piece in pieces),default=0)
            decision=lambda d:bool(d['text']) and minimum(d['pieces'])>=.8 and supported(d['pieces'])
            same['floor08Decisions']+=decision(a)==decision(c);same['allSourceNonwhiteRetained']+=source['allNonwhiteRetained']
            if a['intervals']==c['intervals']:
                maxima['maxEmittedConfidenceDifference']=max(maxima['maxEmittedConfidenceDifference'],max((abs(left['Confidence']-right['Confidence']) for left,right in zip(a['pieces'],c['pieces'])),default=0))
            stream.write(json.dumps({'id':ordinal,'source':source,'inputWidth':value.shape[-1],'validWidth':valid,'timeSteps':reference.shape[1],'featureShape':list(feature.shape),'original':a,'observer':b,'torchHead':c,'originalSupported':supported(a['pieces']),'torchSupported':supported(c['pieces'])},ensure_ascii=False)+'\n')
    dynamic=[]
    with torch.inference_mode():
        for batch in [1,2]:
            for width in [320,481,640,960]:
                value=np.zeros((batch,3,48,width),dtype=np.float32);value[:,:,:,width//4:width//2]=1
                reference=original.run(None,{'x':value})[0];observed,feature,logits=observer.run(None,{'x':value});only_feature=feature_session.run(None,{'x':value})[0];recomputed=F.linear(torch.from_numpy(only_feature),head_weight,head_bias);probability=recomputed.softmax(-1).numpy()
                dynamic.append({'batch':batch,'inputWidth':width,'featureShape':list(feature.shape),'featureMaxDifference':float(np.max(np.abs(only_feature-feature))),'probabilityMaxDifference':float(np.max(np.abs(probability-reference))),'logitMaxDifference':float(np.max(np.abs(recomputed.numpy()-logits))),'textIntervalsIdentical':all(decoded(reference[i],dictionary)['intervals']==decoded(probability[i],dictionary)['intervals'] for i in range(batch)),'scope':'Synthetic numeric input shape fidelity only, no recognition quality claim'})
    result={'scope':freeze['scope'],'rows':len(ORDINALS),'matches':same,'maximumDifferences':maxima,'dynamicShapes':dynamic,'rawSha256':hashlib.sha256(raw.read_bytes()).hexdigest(),'elapsedSeconds':time.monotonic()-started,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'oracleOpened':False,'trainingStarted':False,'finalOpened':False,'portability':'Original and extracted graphs standard domain only/opset11. Full-network PyTorch/CoreML conversion not attempted. Cached-feature head training may be feasible without a transformers medium class; not yet validated by training or independent new development.'}
    save(RESULTS/'fidelity.json',result);print(json.dumps(result))


if __name__=='__main__':main()
