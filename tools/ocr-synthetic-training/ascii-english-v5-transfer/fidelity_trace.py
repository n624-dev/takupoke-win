"""Read-only worst numeric-case decomposition, no quality/oracle/selection."""
import hashlib,json,sys
from pathlib import Path
import onnx,numpy as np,torch
from onnx import helper,TensorProto,numpy_helper
from PIL import Image
from native_math import load
from native_fused import CNN_OUTPUT
from reproduce_math import ROOT,RESULTS,BASE,INPUTS,session,save
sys.path.insert(0,str(Path(__file__).parents[1]));from image_tensor import tensor
OUT=RESULTS.parent/'numerical-trace'
def main():
 OUT.mkdir(exist_ok=False);torch.set_num_threads(2);torch.set_num_interop_threads(1);model=load();graph=onnx.load(str(ROOT/'math-native-feature-logit-observer.onnx'));graph.graph.output.append(helper.make_tensor_value_info(CNN_OUTPUT,TensorProto.FLOAT,['batch',480,1,'time']));path=ROOT/'numeric-cnn-observer.onnx';onnx.save(graph,str(path));native=session(path);constants={n.output[0]:numpy_helper.to_array(helper.get_attribute_value(n.attribute[0])) for n in graph.graph.node if n.op_type=='Constant'};parameter_diff={}
 for i,conv in enumerate(model.encoder.conv_block):
  parameter_diff[f'conv{i}']=float(np.max(np.abs(conv.convolution.weight.detach().numpy()-constants[f'conv2d_{131+i}.w_0'])))
  for field,suffix in [('weight','w_0'),('bias','b_0'),('running_mean','w_1'),('running_var','w_2')]:parameter_diff[f'bn{i}.{field}']=float(np.max(np.abs(getattr(conv.normalization,field).detach().numpy()-constants[f'batch_norm2d_{146+i}.{suffix}'])))
 parameter_diff['headWeight']=float(np.max(np.abs(model.head.weight.detach().numpy()-constants['linear_8.w_0'].T)));parameter_diff['headBias']=float(np.max(np.abs(model.head.bias.detach().numpy()-constants['linear_8.b_0'])))
 raw=[json.loads(line) for line in (RESULTS/'unjudged.jsonl').read_text(encoding='utf-8').splitlines()];ids={max(raw,key=lambda r:r['differences'][key])['id'] for key in ['feature','logit','probability']};inputs={r['id']:r for r in json.loads((INPUTS/'inputs.json').read_text(encoding='utf-8'))};rows=[]
 with torch.inference_mode():
  for rid in sorted(ids):
   r=inputs[rid];p=INPUTS/r['file'];assert hashlib.sha256(p.read_bytes()).hexdigest()==r['sha256'];x,valid=tensor(Image.open(p));prob,feat,logits,cnn=native.run(None,{'x':x[None]});tc=model.cnn(torch.from_numpy(x[None]));head_nativecnn=model.encoder(torch.from_numpy(cnn)).last_hidden_state;head_torchcnn=model.encoder(tc).last_hidden_state;rows.append({'id':rid,'cnnMaxAbsolute':float(np.max(np.abs(cnn-tc.numpy()))),'encoderOnNativeCnnFeatureMaxAbsolute':float(np.max(np.abs(feat-head_nativecnn.numpy()))),'encoderOnTorchCnnFeatureMaxAbsolute':float(np.max(np.abs(feat-head_torchcnn.numpy()))),'nativeCnnMagnitude':float(np.max(np.abs(cnn))),'nativeFeatureMagnitude':float(np.max(np.abs(feat)))})
 save(OUT/'trace.json',{'scope':'Largest numerical difference case diagnosis only, derived from unjudged512 numeric differences; no oracle/quality/modelselection/newfinal','parameterMaximumDifferences':parameter_diff,'rows':rows,'frozenGateChanged':False,'trainingStarted':False});print(json.dumps(rows))
if __name__=='__main__':main()
