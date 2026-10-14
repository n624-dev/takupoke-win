"""Freeze then verify original medium SVTR Torch reconstruction, oracle-free."""
import copy
import hashlib
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

from svtr import MediumSvtr
sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-paddle-medium-features'))
from fidelity import MODEL,MODEL_SHA,DICT,PIXELS,PIXELS_SHA,ORDINALS,FEATURE,LOGITS,framed,session,decoded,save

REPO=Path('/workspace/scratch/takupoke-win');ROOT=Path('/workspace/scratch/ascii-paddle-medium-svtr-20261013-v1');RESULTS=REPO/'research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-svtr-v1'
CONTEXT='p2o.pd_op.add.70.0';SHORTCUT='p2o.pd_op.swish.0.0'


def ancestor_graph(original,outputs):
 model=copy.deepcopy(original);needed=set(outputs);kept=[]
 for node in reversed(model.graph.node):
  if needed.intersection(node.output):kept.append(node);needed.update(node.input)
 del model.graph.node[:];model.graph.node.extend(reversed(kept));initializers=[v for v in model.graph.initializer if v.name in needed];del model.graph.initializer[:];model.graph.initializer.extend(initializers);del model.graph.output[:]
 for output in outputs:model.graph.output.append(helper.make_tensor_value_info(output,TensorProto.FLOAT,['batch',192,1,'time']))
 onnx.checker.check_model(model);return model


def main():
 if (RESULTS/'freeze.json').exists():raise ValueError('Fixed fidelity run cannot be overwritten')
 assert hashlib.sha256(MODEL.read_bytes()).hexdigest()==MODEL_SHA and hashlib.sha256(PIXELS.read_bytes()).hexdigest()==PIXELS_SHA
 torch.set_num_threads(2);torch.set_num_interop_threads(1);original_graph=onnx.load(str(MODEL));initializers={v.name:torch.from_numpy(numpy_helper.to_array(v).copy()) for v in original_graph.graph.initializer};model=MediumSvtr(initializers).eval();head_weight=initializers['linear_8.w_0'].T.contiguous();head_bias=initializers['linear_8.b_0'];dictionary=json.loads(DICT.read_text(encoding='utf-8'));assert len(dictionary)==18710
 ROOT.mkdir(exist_ok=True);RESULTS.mkdir(exist_ok=True);cnn=ROOT/'medium-frozen-local-neck.onnx';onnx.save(ancestor_graph(original_graph,[CONTEXT,SHORTCUT]),str(cnn));state=ROOT/'original-svtr.safetensors';save_file(model.state_dict(),str(state));observer=copy.deepcopy(original_graph)
 observer.graph.output.extend([helper.make_tensor_value_info(CONTEXT,TensorProto.FLOAT,['batch',192,1,'time']),helper.make_tensor_value_info(SHORTCUT,TensorProto.FLOAT,['batch',192,1,'time']),helper.make_tensor_value_info(FEATURE,TensorProto.FLOAT,['batch','time',192]),helper.make_tensor_value_info(LOGITS,TensorProto.FLOAT,['batch','time',18710])]);onnx.checker.check_model(observer);observed=ROOT/'medium-svtr-observer.onnx';onnx.save(observer,str(observed))
 tolerances={'featureMaxAbsolute':1e-4,'logitMaxAbsolute':1e-3,'probabilityMaxAbsolute':1e-4}
 freeze={'namespace':'research-ascii-paddle-medium-svtr-v1','scope':'Initialization/graph reconstruction only on consumed48pixels and numeric dynamic shapes; no oracle, training, independent-final or production credit','condition':'Same all-ink original bbox+2px/native BGR48 FP32 width320..960, full18710 CTC; no padding condition adjustment','ordinals':ORDINALS,'modelSha256':MODEL_SHA,'dictionarySha256':hashlib.sha256(DICT.read_bytes()).hexdigest(),'pixelsSha256':PIXELS_SHA,'sources':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in Path(__file__).parent.glob('*.py')},'sharedPreprocessSha256':hashlib.sha256((Path(__file__).parents[1]/'image_tensor.py').read_bytes()).hexdigest(),'sharedFidelitySourceSha256':hashlib.sha256((Path(__file__).parents[1]/'ascii-paddle-medium-features/fidelity.py').read_bytes()).hexdigest(),'contextTensor':CONTEXT,'shortcutTensor':SHORTCUT,'frozenPrefix':'Original CNN, 1x1 projection+BN+SiLU, local depthwise7+BN+SiLU+residual; split immediately before global SVTR flatten/norm','torchReconstruction':'2 pre-norm attention blocks, hidden192/8heads/24perhead, SiLU FFN768,5 original LayerNorm matrices/bias/eps1e-5, original final residual; copied tensor-only ONNX values; no dropout','parameters':sum(p.numel() for p in model.parameters()),'tolerances':tolerances,'gate':'All fixed text/CTC intervals/minconfidence floor.8+validWidth decisions identical, natural and dynamic outputs within registered numeric tolerances. Floating equality not asserted. No quality qualification from fidelity.','cnnSha256':hashlib.sha256(cnn.read_bytes()).hexdigest(),'svtrStateSha256':hashlib.sha256(state.read_bytes()).hexdigest(),'observerSha256':hashlib.sha256(observed.read_bytes()).hexdigest(),'finalOpened':False,'trainingStarted':False}
 save(RESULTS/'freeze.json',freeze);reference=session(MODEL);observing=session(observed);prefix=session(cnn);packet=json.loads(PIXELS.read_text(encoding='utf-8'));lookup={row['id']:row for row in packet['rows']};raw=RESULTS/'unjudged.jsonl';started=time.monotonic();maxima={key:0. for key in ['observerProbability','context','shortcut','feature','logits','probability','emittedConfidence']};matches={'textIntervals':0,'floor08Decisions':0,'allSourceInkRetained':0}
 def compare(value,valid):
  original=reference.run(None,{'x':value})[0];prob_ref,context,shortcut,feature_ref,logit_ref=observing.run(None,{'x':value});context_cnn,shortcut_cnn=prefix.run(None,{'x':value});features=model(torch.from_numpy(context_cnn),torch.from_numpy(shortcut_cnn));logits=F.linear(features,head_weight,head_bias);probability=logits.softmax(-1).numpy();deltas={'observerProbability':float(np.max(np.abs(original-prob_ref))),'context':float(np.max(np.abs(context-context_cnn))),'shortcut':float(np.max(np.abs(shortcut-shortcut_cnn))),'feature':float(np.max(np.abs(features.numpy()-feature_ref))),'logits':float(np.max(np.abs(logits.numpy()-logit_ref))),'probability':float(np.max(np.abs(probability-original)))};a=[decoded(original[i],dictionary) for i in range(len(value))];b=[decoded(probability[i],dictionary) for i in range(len(value))]
  supported=lambda pieces:all(p['End']*value.shape[-1]<=valid*original.shape[1] for p in pieces);decision=lambda d:bool(d['text']) and min((p['Confidence'] for p in d['pieces']),default=0)>=.8 and supported(d['pieces'])
  same=all(l['intervals']==r['intervals'] for l,r in zip(a,b));floor=all(decision(l)==decision(r) for l,r in zip(a,b));deltas['emittedConfidence']=max((abs(p['Confidence']-q['Confidence']) for l,r in zip(a,b) if l['intervals']==r['intervals'] for p,q in zip(l['pieces'],r['pieces'])),default=0)
  return a,b,deltas,same,floor,list(context_cnn.shape)
 with raw.open('x',encoding='utf-8',newline='\n') as stream,torch.inference_mode():
  for ordinal in ORDINALS:
   value,valid,source=framed(lookup[ordinal]);a,b,delta,same,floor,shape=compare(value,valid)
   for key in maxima:maxima[key]=max(maxima[key],delta[key])
   matches['textIntervals']+=same;matches['floor08Decisions']+=floor;matches['allSourceInkRetained']+=source['allNonwhiteRetained'];stream.write(json.dumps({'id':ordinal,'source':source,'inputWidth':value.shape[-1],'validWidth':valid,'contextShape':shape,'original':a[0],'torchSvtr':b[0],'differences':delta,'sameTextIntervals':same,'sameFloor08Decision':floor},ensure_ascii=False)+'\n')
  dynamic=[]
  for batch in [1,2]:
   for width in [320,481,640,960]:
    value=np.zeros((batch,3,48,width),dtype=np.float32);value[:,:,:,width//4:width//2]=1;a,b,delta,same,floor,shape=compare(value,width);dynamic.append({'batch':batch,'inputWidth':width,'contextShape':shape,'differences':delta,'sameTextIntervals':same,'sameFloor08Decision':floor})
 numeric=lambda d:d['feature']<=tolerances['featureMaxAbsolute'] and d['logits']<=tolerances['logitMaxAbsolute'] and d['probability']<=tolerances['probabilityMaxAbsolute'];gate=matches['textIntervals']==len(ORDINALS) and matches['floor08Decisions']==len(ORDINALS) and numeric(maxima) and all(r['sameTextIntervals'] and r['sameFloor08Decision'] and numeric(r['differences']) for r in dynamic)
 result={'scope':freeze['scope'],'matches':matches,'maximumDifferences':maxima,'dynamicShapes':dynamic,'fidelityGatePassed':gate,'rawSha256':hashlib.sha256(raw.read_bytes()).hexdigest(),'seconds':time.monotonic()-started,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'oracleOpened':False,'trainingStarted':False,'finalOpened':False,'portability':'Original prefix standard ONNX opset11; reconstructed Torch uses standard primitives. Trained ONNX/CoreML export and Windows/iPhone execution not attempted.'};save(RESULTS/'fidelity.json',result);print(json.dumps(result))


if __name__=='__main__':main()
