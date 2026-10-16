"""Failure diagnosis only: explicit vs fused operations, no training/inference repair."""
import copy
import hashlib
import json
from pathlib import Path
import sys

import numpy as np
import onnx
from onnx import helper,TensorProto,numpy_helper
import onnxruntime as ort
import torch
import torch.nn.functional as F

from svtr import MediumSvtr
from reproduce import ROOT,RESULTS,MODEL,PIXELS,CONTEXT,SHORTCUT,framed,session,save


def main():
 torch.set_num_threads(2);torch.set_num_interop_threads(1);raw=[json.loads(line) for line in (RESULTS/'unjudged.jsonl').read_text(encoding='utf-8').splitlines()];worst=max(raw,key=lambda r:r['differences']['feature'])['id'];packet=json.loads(PIXELS.read_text(encoding='utf-8'));row=next(r for r in packet['rows'] if r['id']==worst);value,valid,_=framed(row);g=onnx.load(str(MODEL));ini={i.name:torch.from_numpy(numpy_helper.to_array(i).copy()) for i in g.graph.initializer};model=MediumSvtr(ini).eval();names=[CONTEXT,SHORTCUT,'p2o.pd_op.transpose.0.0','p2o.pd_op.layer_norm.0.0','p2o.pd_op.add.71.0','p2o.pd_op.add.73.0','p2o.pd_op.layer_norm.1.0','p2o.pd_op.add.76.0','p2o.pd_op.layer_norm.2.0','p2o.pd_op.add.79.0','p2o.pd_op.layer_norm.3.0','p2o.pd_op.add.82.0','p2o.pd_op.layer_norm.4.0','p2o.pd_op.transpose.8.0']
 for name in names:g.graph.output.append(helper.make_tensor_value_info(name,TensorProto.FLOAT,None))
 path=ROOT/'trace-numeric-observer.onnx';onnx.save(g,str(path));out=session(path).run(None,{'x':value});actual={k:torch.from_numpy(v) for k,v in zip(names,out[1:])};x=actual['p2o.pd_op.transpose.0.0'];diff=lambda a,b:float((a-b).abs().max());norms=[]
 for i,(input_name,output_name) in enumerate([('p2o.pd_op.transpose.0.0','p2o.pd_op.layer_norm.0.0'),('p2o.pd_op.add.73.0','p2o.pd_op.layer_norm.1.0'),('p2o.pd_op.add.76.0','p2o.pd_op.layer_norm.2.0'),('p2o.pd_op.add.79.0','p2o.pd_op.layer_norm.3.0'),('p2o.pd_op.add.82.0','p2o.pd_op.layer_norm.4.0')]):
  v=actual[input_name];w=ini[f'helper.reshape.{i*2}'];b=ini[f'helper.reshape.{i*2+1}'];c=v-v.mean(-1,keepdim=True);explicit=c/(c.pow(2).mean(-1,keepdim=True)+1e-5).sqrt()*w+b;fused=F.layer_norm(v,(192,),w,b,1e-5);double=F.layer_norm(v.double(),(192,),w.double(),b.double(),1e-5).float();norms.append({'norm':i,'explicitMax':diff(explicit,actual[output_name]),'torchFusedMax':diff(fused,actual[output_name]),'doubleMax':diff(double,actual[output_name])})
 q=actual['p2o.pd_op.layer_norm.0.0'];w=ini['linear_0.w_0'];b=ini['linear_0.b_0'];result={'scope':'Read-only numeric failure diagnosis, consumed reconstruction inputs only; no training/selection/final','worstConsumedOrdinal':worst,'normDifferences':norms,'qkvFusedLinearMax':diff(F.linear(q,w.T,b),actual['p2o.pd_op.add.71.0']),'qkvMatmulThenAddMax':diff(q@w+b,actual['p2o.pd_op.add.71.0']),'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()};save(RESULTS/'numeric-failure-diagnosis.json',result);print(json.dumps(result))


if __name__=='__main__':main()
