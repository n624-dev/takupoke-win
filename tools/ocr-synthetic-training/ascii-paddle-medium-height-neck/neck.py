"""Original height pool/local neck; zero-init height correction, frozen SVTR."""
from pathlib import Path
import sys

import torch
import torch.nn.functional as F

sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-paddle-medium-svtr'))
from svtr_exact import MediumSvtr


class FrozenBatchNorm(torch.nn.Module):
 def __init__(self,initializers,number):
  super().__init__()
  for field,key in [('scale','w_0'),('bias','b_0'),('mean','w_1'),('variance','w_2')]:self.register_buffer(field,initializers[f'batch_norm2d_{number}.{key}'].clone())
 def forward(self,x):return F.batch_norm(x,self.mean,self.variance,self.scale,self.bias,False,0.1,9.999999747378752e-06)


class HeightNeck(torch.nn.Module):
 def __init__(self,initializers):
  super().__init__();self.poolDelta=torch.nn.Parameter(torch.zeros(768,1,3,2));self.shortcut=self.conv(initializers,72);self.shortcutNorm=FrozenBatchNorm(initializers,60);self.projection=self.conv(initializers,70);self.projectionNorm=FrozenBatchNorm(initializers,58);self.depthwise=self.conv(initializers,71,groups=192,padding=(0,3));self.depthwiseNorm=FrozenBatchNorm(initializers,59);self.svtr=MediumSvtr(initializers)
  for parameter in self.svtr.parameters():parameter.requires_grad_(False)
 @staticmethod
 def conv(initializers,number,groups=1,padding=0):
  weight=initializers[f'conv2d_{number}.w_0'];layer=torch.nn.Conv2d(weight.shape[1]*groups,weight.shape[0],tuple(weight.shape[2:]),groups=groups,padding=padding,bias=False)
  with torch.no_grad():layer.weight.copy_(weight)
  return layer
 def maps(self,x):
  pooled=F.avg_pool2d(x,(3,2),(3,2))+F.conv2d(x,self.poolDelta,stride=(3,2),groups=768);raw=self.shortcutNorm(self.shortcut(pooled));shortcut=raw*raw.sigmoid();raw=self.projectionNorm(self.projection(pooled));projected=raw*raw.sigmoid();raw=self.depthwiseNorm(self.depthwise(projected));context=projected+raw*raw.sigmoid();return pooled,context,shortcut
 def forward(self,x):
  pooled,context,shortcut=self.maps(x);return self.svtr(context,shortcut)
