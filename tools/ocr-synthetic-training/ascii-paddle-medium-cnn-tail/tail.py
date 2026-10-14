"""Original final CNN residual block before height pooling; original downstream frozen."""
from pathlib import Path
import importlib.util
import torch
from torch.nn import functional as F
_spec=importlib.util.spec_from_file_location('cnn_tail_height_reference',Path(__file__).parents[1]/'ascii-paddle-medium-height-neck/neck.py')
_ref=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(_ref)
HeightNeck=_ref.HeightNeck
class CnnTail(torch.nn.Module):
 def __init__(self,initializers):
  super().__init__()
  self.downstream=HeightNeck(initializers)
  for p in self.downstream.parameters():p.requires_grad_(False)
  for name,number,biasnumber,groups,padding in [('spatial',128,56,768,1),('expand',129,57,1,0),('project',130,58,1,0)]:
   w=initializers[f'conv2d_{number}.w_0'];layer=torch.nn.Conv2d(w.shape[1]*groups,w.shape[0],tuple(w.shape[2:]),padding=padding,groups=groups,bias=True)
   with torch.no_grad():layer.weight.copy_(w);layer.bias.copy_(initializers[f'p2o.pd_op.reshape.{biasnumber}.0'].reshape(-1))
   for p in layer.parameters():p.requires_grad_(name=='spatial')
   setattr(self,name,layer)
  for name,number in [('geluSqrt2',39),('geluHalf',40),('geluOne',41)]:self.register_buffer(name,initializers[f'helper.constant.{number}'].clone())
 def prepool(self,x):
  residual=self.spatial(x);raw=self.expand(residual);gelu=raw*(torch.erf(raw/self.geluSqrt2)+self.geluOne)*self.geluHalf
  return residual+self.project(gelu)
 def maps(self,x):return self.downstream.maps(self.prepool(x))
 def forward(self,x):return self.downstream(self.prepool(x))
