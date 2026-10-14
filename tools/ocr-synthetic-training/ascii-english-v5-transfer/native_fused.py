"""Original ONNX fused CNN as a differentiable fixed-whitelist Torch DAG."""
from collections import Counter
from pathlib import Path
import onnx,torch
from onnx import helper,numpy_helper
from torch.nn import functional as F
from native import load as load_hf
MODEL=Path('/workspace/scratch/paddle-english-v5-pinned/inference.onnx')
CNN_OUTPUT='p2o.pd_op.pool2d.2.0'
class NativeCnn(torch.nn.Module):
 def __init__(self):
  super().__init__();graph=onnx.load(str(MODEL));needed={CNN_OUTPUT};selected=[]
  for node in reversed(graph.graph.node):
   if needed.intersection(node.output):selected.append(node);needed.update(node.input)
  selected=list(reversed(selected));constant={};operations=[]
  for node in selected:
   assert node.domain in ['', 'ai.onnx'] and len(node.output)==1
   attrs={a.name:helper.get_attribute_value(a) for a in node.attribute}
   if node.op_type=='Constant':constant[node.output[0]]=torch.from_numpy(numpy_helper.to_array(attrs['value']).copy())
   else:
    assert node.op_type in ['Conv','BatchNormalization','Identity','Reshape','Add','Mul','HardSigmoid','GlobalAveragePool','Relu','AveragePool']
    operations.append((node.op_type,list(node.input),node.output[0],attrs))
  conv_names={name for op,inputs,out,attrs in operations if op=='Conv' for name in inputs[1:]};self.keys={};self.origin={};self.trainableNames=[]
  for i,(name,value) in enumerate(constant.items()):
   key=f'tensor_{i}';self.keys[name]=key;self.origin[key]=name
   trainable=value.is_floating_point() and (name in conv_names or name.startswith('learnable_affine_block_'))
   if trainable:self.register_parameter(key,torch.nn.Parameter(value));self.trainableNames.append(name)
   else:self.register_buffer(key,value)
  self.operations=operations;self.usecount=Counter(name for op,inputs,out,attrs in operations for name in inputs)
 def forward(self,x):
  values={name:getattr(self,key) for name,key in self.keys.items()};values['x']=x;remaining=self.usecount.copy()
  for op,inputs,out,a in self.operations:
   args=[values[name] for name in inputs]
   if op=='Conv':
    pad=a.get('pads',[0,0,0,0]);assert pad[:2]==pad[2:]
    y=F.conv2d(args[0],args[1],args[2] if len(args)==3 else None,stride=tuple(a.get('strides',[1,1])),padding=tuple(pad[:2]),dilation=tuple(a.get('dilations',[1,1])),groups=a.get('group',1))
   elif op=='BatchNormalization':y=F.batch_norm(args[0],args[3],args[4],args[1],args[2],False,.1,a.get('epsilon',1e-5))
   elif op=='Identity':y=args[0]
   elif op=='Reshape':y=args[0].reshape(tuple(int(v) for v in args[1]))
   elif op=='Add':y=args[0]+args[1]
   elif op=='Mul':y=args[0]*args[1]
   elif op=='HardSigmoid':y=(args[0]*a.get('alpha',.2)+a.get('beta',.5)).clamp(0,1)
   elif op=='GlobalAveragePool':y=args[0].mean(dim=(2,3),keepdim=True)
   elif op=='Relu':y=args[0].relu()
   elif op=='AveragePool':
    assert a.get('pads',[0,0,0,0])==[0,0,0,0] and not a.get('ceil_mode',0)
    y=F.avg_pool2d(args[0],tuple(a['kernel_shape']),tuple(a['strides']),count_include_pad=bool(a.get('count_include_pad',0)))
   else:raise AssertionError(op)
   values[out]=y
   for name in inputs:
    remaining[name]-=1
    if remaining[name]==0 and name!=CNN_OUTPUT:values.pop(name,None)
  return values[CNN_OUTPUT]
class EnglishModel(torch.nn.Module):
 def __init__(self):
  super().__init__();original=load_hf();self.cnn=NativeCnn();self.encoder=original.head.encoder;self.head=original.head.head
 def forward(self,value):
  feature=self.encoder(self.cnn(value)).last_hidden_state;return feature,self.head(feature)
def load():return EnglishModel().eval()
def feature_logits(model,value):return model(value)
