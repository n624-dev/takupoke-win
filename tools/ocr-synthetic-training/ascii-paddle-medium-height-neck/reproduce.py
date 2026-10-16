"""Native pool/local-neck reconstruction and lossless FP32 cache feasibility."""
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
from safetensors.torch import save_file
import torch
import torch.nn.functional as F

from neck import HeightNeck
sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-paddle-medium-svtr'))
from reproduce_v2 import MODEL,MODEL_SHA,DICT,PIXELS,PIXELS_SHA,ORDINALS,FEATURE,LOGITS,framed,session,decoded,save

ROOT=Path('/workspace/scratch/ascii-paddle-medium-height-neck-20261013-v1');REPO=Path('/workspace/scratch/takupoke-win');RESULTS=REPO/'research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-height-neck-v1'
PREPOOL='p2o.pd_op.add.69.0';POOLED='p2o.pd_op.pool2d.1.0';CONTEXT='p2o.pd_op.add.70.0';SHORTCUT='p2o.pd_op.swish.0.0'


def ancestor(original,outputs):
 graph=copy.deepcopy(original);needed=set(outputs);kept=[]
 for node in reversed(graph.graph.node):
  if needed.intersection(node.output):kept.append(node);needed.update(node.input)
 del graph.graph.node[:];graph.graph.node.extend(reversed(kept));initializers=[v for v in graph.graph.initializer if v.name in needed];del graph.graph.initializer[:];graph.graph.initializer.extend(initializers);del graph.graph.output[:]
 for output in outputs:graph.graph.output.append(info(output))
 onnx.checker.check_model(graph);return graph


def info(name):
 shape=['batch',768,3,'localWidth'] if name==PREPOOL else ['batch',768,1,'time'] if name==POOLED else ['batch','time',192] if name==FEATURE else ['batch','time',18710] if name==LOGITS else ['batch',192,1,'time']
 return helper.make_tensor_value_info(name,TensorProto.FLOAT,shape)


def main():
 if (RESULTS/'freeze.json').exists():raise ValueError('Fixed reproduction cannot repeat')
 assert hashlib.sha256(MODEL.read_bytes()).hexdigest()==MODEL_SHA and hashlib.sha256(PIXELS.read_bytes()).hexdigest()==PIXELS_SHA;torch.set_num_threads(2);torch.set_num_interop_threads(1);original=onnx.load(str(MODEL));initializers={v.name:torch.from_numpy(numpy_helper.to_array(v).copy()) for v in original.graph.initializer};model=HeightNeck(initializers).eval();head_weight=initializers['linear_8.w_0'].T.contiguous();head_bias=initializers['linear_8.b_0'];dictionary=json.loads(DICT.read_text(encoding='utf-8'));assert len(dictionary)==18710;state=ROOT/'original-height-neck.safetensors';save_file(model.state_dict(),str(state));prefix=ROOT/'medium-frozen-prepool.onnx';onnx.save(ancestor(original,[PREPOOL]),str(prefix));observer=copy.deepcopy(original);observer.graph.output.extend([info(name) for name in [PREPOOL,POOLED,CONTEXT,SHORTCUT,FEATURE,LOGITS]]);onnx.checker.check_model(observer);observer_path=ROOT/'medium-height-neck-observer.onnx';onnx.save(observer,str(observer_path))
 tolerance={'featureMaxAbsolute':1e-4,'logitMaxAbsolute':1e-3,'probabilityMaxAbsolute':1e-4};source=Path(__file__).parent
 freeze={'namespace':'research-ascii-paddle-medium-height-neck-v1','scope':'Initialization/native reconstruction and lossless-cache size only; consumed48pixels plus numeric dynamicshapes; no oracle/quality/training/final/activation','modelSha256':MODEL_SHA,'dictionarySha256':hashlib.sha256(DICT.read_bytes()).hexdigest(),'pixelsSha256':PIXELS_SHA,'ordinals':ORDINALS,'condition':'Whole originalRGBink bbox+2pixels/nativeFP32BGR48 width320..960, validWidth preserved; parentmin160 condition not used','prepoolTensor':PREPOOL,'cnnMapShape':'batch768x3xlocalWidth; original trailing odd local column remains in input, native stride3x2 floored average/correction outputT','originalCopy':'Original localprojection weights, running mean/variance and affineBNbuffers eps1e-5, two unchanged copiedSVTRblocks with original1e-5/1e-6norms, originalfull18710head','trainablePrototype':'Zero-initialized depthwise768x(3,2) pool correction plus three original neck convweights; SVTR/BNstats+affine/head frozen','trainableParameters':sum(p.numel() for p in model.parameters() if p.requires_grad),'sources':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [source/'neck.py',source/'reproduce.py']},'sharedSvtrSourceSha256':hashlib.sha256((source.parent/'ascii-paddle-medium-svtr/svtr_exact.py').read_bytes()).hexdigest(),'sharedPreprocessSha256':hashlib.sha256((source.parent/'image_tensor.py').read_bytes()).hexdigest(),'prefixSha256':hashlib.sha256(prefix.read_bytes()).hexdigest(),'observerSha256':hashlib.sha256(observer_path.read_bytes()).hexdigest(),'stateSha256':hashlib.sha256(state.read_bytes()).hexdigest(),'tolerances':tolerance,'gate':'Everyfixed natural/dynamic text+CTCinterval/floor.8+validWidthdecision identical and within registered feature/logit/prob tolerances; floatingbyteequality not assumed','cacheTest':'Natural providedFP32prepool+native finalteacherfeature storedlossless NPZ; decompresseddtype/shape/exactbytes SHA verified. No float16 or quantization.'};save(RESULTS/'freeze.json',freeze);reference=session(MODEL);observing=session(observer_path);trunk=session(prefix);packet=json.loads(PIXELS.read_text(encoding='utf-8'));lookup={r['id']:r for r in packet['rows']};raw=RESULTS/'unjudged.jsonl';maxima={name:0. for name in ['observerProbability','prepool','pooled','context','shortcut','feature','logits','probability','emittedConfidence']};matches={'textIntervals':0,'floor08Decisions':0,'allSourceInkRetained':0};compression=[];started=time.monotonic()
 def compare(value,valid,compress=False):
  baseline=reference.run(None,{'x':value})[0];prob_ref,prepool,pooled_ref,context_ref,shortcut_ref,feature_ref,logit_ref=observing.run(None,{'x':value});only_pre=trunk.run(None,{'x':value})[0];x=torch.from_numpy(only_pre);pooled,context,shortcut=model.maps(x);feature=model.svtr(context,shortcut);logits=F.linear(feature,head_weight,head_bias);probability=logits.softmax(-1).numpy();differences={'observerProbability':float(np.max(np.abs(baseline-prob_ref))),'prepool':float(np.max(np.abs(prepool-only_pre))),'pooled':float(np.max(np.abs(pooled.numpy()-pooled_ref))),'context':float(np.max(np.abs(context.numpy()-context_ref))),'shortcut':float(np.max(np.abs(shortcut.numpy()-shortcut_ref))),'feature':float(np.max(np.abs(feature.numpy()-feature_ref))),'logits':float(np.max(np.abs(logits.numpy()-logit_ref))),'probability':float(np.max(np.abs(probability-baseline)))};a=[decoded(baseline[i],dictionary) for i in range(len(value))];b=[decoded(probability[i],dictionary) for i in range(len(value))];supported=lambda pieces:all(p['End']*value.shape[-1]<=valid*baseline.shape[1] for p in pieces);accepted=lambda d:bool(d['text']) and min((p['Confidence'] for p in d['pieces']),default=0)>=.8 and supported(d['pieces']);same=all(l['intervals']==r['intervals'] for l,r in zip(a,b));floor=all(accepted(l)==accepted(r) for l,r in zip(a,b));differences['emittedConfidence']=max((abs(p['Confidence']-q['Confidence']) for l,r in zip(a,b) if l['intervals']==r['intervals'] for p,q in zip(l['pieces'],r['pieces'])),default=0);stored=None
  if compress:
   payload=io.BytesIO();np.savez_compressed(payload,cnn=only_pre[0],teacher=feature_ref[0]);encoded=payload.getvalue()
   with np.load(io.BytesIO(encoded),allow_pickle=False) as restored:
    assert set(restored.files)=={'cnn','teacher'} and restored['cnn'].dtype==restored['teacher'].dtype==np.float32;assert restored['cnn'].tobytes()==only_pre[0].tobytes() and restored['teacher'].tobytes()==feature_ref[0].tobytes()
   total=only_pre[0].nbytes+feature_ref[0].nbytes;stored={'rawFp32Bytes':total,'losslessNpzBytes':len(encoded),'ratio':len(encoded)/total,'decompressedBytesIdentical':True,'prepoolShape':list(only_pre.shape)}
  return a,b,differences,same,floor,list(only_pre.shape),stored
 with raw.open('x',encoding='utf-8',newline='\n') as stream,torch.inference_mode():
  for ordinal in ORDINALS:
   value,valid,source_record=framed(lookup[ordinal]);a,b,diff,same,floor,shape,stored=compare(value,valid,True)
   for key in maxima:maxima[key]=max(maxima[key],diff[key])
   matches['textIntervals']+=same;matches['floor08Decisions']+=floor;matches['allSourceInkRetained']+=source_record['allNonwhiteRetained'];compression.append({'id':ordinal,**stored});stream.write(json.dumps({'id':ordinal,'source':source_record,'inputWidth':value.shape[-1],'validWidth':valid,'prepoolShape':shape,'original':a[0],'torchHeightNeck':b[0],'differences':diff,'sameTextIntervals':same,'sameFloor08Decision':floor},ensure_ascii=False)+'\n')
  dynamic=[]
  for batch in [1,2]:
   for width in [320,481,500,640,960]:
    value=np.zeros((batch,3,48,width),dtype=np.float32);value[:,:,:,width//4:width//2]=1;a,b,diff,same,floor,shape,_=compare(value,width);dynamic.append({'batch':batch,'inputWidth':width,'prepoolShape':shape,'differences':diff,'sameTextIntervals':same,'sameFloor08Decision':floor})
 numeric=lambda d:d['feature']<=tolerance['featureMaxAbsolute'] and d['logits']<=tolerance['logitMaxAbsolute'] and d['probability']<=tolerance['probabilityMaxAbsolute'];gate=matches['textIntervals']==len(ORDINALS) and matches['floor08Decisions']==len(ORDINALS) and numeric(maxima) and all(row['sameTextIntervals'] and row['sameFloor08Decision'] and numeric(row['differences']) for row in dynamic);result={'scope':freeze['scope'],'matches':matches,'maximumDifferences':maxima,'dynamicShapes':dynamic,'fidelityGatePassed':gate,'compression':{'rows':compression,'totalRawBytes':sum(r['rawFp32Bytes'] for r in compression),'totalLosslessBytes':sum(r['losslessNpzBytes'] for r in compression),'weightedRatio':sum(r['losslessNpzBytes'] for r in compression)/sum(r['rawFp32Bytes'] for r in compression)},'rawSha256':hashlib.sha256(raw.read_bytes()).hexdigest(),'seconds':time.monotonic()-started,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'oracleOpened':False,'trainingStarted':False,'finalOpened':False,'portability':'Original prefix isstandardONNXopset11; reconstructedheight/localneck andSVTR standardTorch primitives. Trainedexport/nativeWindows/CoreML/iPhone not validated.'};save(RESULTS/'fidelity.json',result);print(json.dumps(result))


if __name__=='__main__':main()
