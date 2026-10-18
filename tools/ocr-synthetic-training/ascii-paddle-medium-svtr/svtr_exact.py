"""Original medium final two SVTR blocks, copied from standard ONNX tensors."""
import torch
import torch.nn.functional as F


class ExactNorm(torch.nn.Module):
 def __init__(self,weight,bias,epsilon):
  super().__init__();self.weight=torch.nn.Parameter(weight.clone());self.bias=torch.nn.Parameter(bias.clone());self.register_buffer('epsilon',epsilon.clone())
 def forward(self,x):
  centered=x-x.mean(-1,keepdim=True)
  return centered/(centered.pow(2).mean(-1,keepdim=True)+self.epsilon).sqrt()*self.weight+self.bias


class Block(torch.nn.Module):
 def __init__(self,initializers,number):
  super().__init__();offset=number*4
  self.norm1=ExactNorm(initializers[f'helper.reshape.{offset}'],initializers[f'helper.reshape.{offset+1}'],initializers['helper.constant.48' if number==0 else 'helper.constant.68']);self.norm2=ExactNorm(initializers[f'helper.reshape.{offset+2}'],initializers[f'helper.reshape.{offset+3}'],initializers['helper.constant.61' if number==0 else 'helper.constant.81'])
  self.qkv=self.linear(initializers,offset);self.projection=self.linear(initializers,offset+1);self.ff1=self.linear(initializers,offset+2);self.ff2=self.linear(initializers,offset+3)
 @staticmethod
 def linear(initializers,index):
  weight=initializers[f'linear_{index}.w_0'];bias=initializers[f'linear_{index}.b_0'];layer=torch.nn.Linear(weight.shape[0],weight.shape[1])
  with torch.no_grad():layer.weight.copy_(weight.T);layer.bias.copy_(bias)
  return layer
 def forward(self,x):
  batch,time,channels=x.shape;qkv=self.qkv(self.norm1(x)).reshape(batch,time,3,8,24).permute(2,0,3,1,4);q,k,v=qkv.unbind(0);attention=((q*0.20412415266036987)@k.transpose(-1,-2)).softmax(-1)
  x=x+self.projection((attention@v).transpose(1,2).reshape(batch,time,channels));x=x+self.ff2(F.silu(self.ff1(self.norm2(x))))
  return x


class MediumSvtr(torch.nn.Module):
 def __init__(self,initializers):
  super().__init__();self.blocks=torch.nn.ModuleList([Block(initializers,i) for i in range(2)]);self.finalNorm=ExactNorm(initializers['helper.reshape.8'],initializers['helper.reshape.9'],initializers['helper.constant.88'])
 def forward(self,context,shortcut):
  # Native neck output has height1. Its local context and residual shortcut
  # are distinct original tensors; neither is re-rendered or normalized here.
  x=context.flatten(2).transpose(1,2)
  for block in self.blocks:x=block(x)
  x=self.finalNorm(x).transpose(1,2).reshape_as(context)+shortcut
  return x.squeeze(2).transpose(1,2)
