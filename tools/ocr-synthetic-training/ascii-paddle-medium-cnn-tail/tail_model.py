"""Adapt only original lastCNN depthwise spatial weights/bias plus diagnostic reject."""
import torch
import torch.nn.functional as F
from tail import CnnTail
class Adapter(torch.nn.Module):
 def __init__(self,initializers,dictionary):
  super().__init__();self.neck=CnnTail(initializers);self.register_buffer('originalWeight',initializers['linear_8.w_0'].T.contiguous());self.register_buffer('originalBias',initializers['linear_8.b_0']);self.reject=torch.nn.Linear(192,1);torch.nn.init.zeros_(self.reject.weight);torch.nn.init.zeros_(self.reject.bias)
 def logits(self,feature):return F.linear(feature,self.originalWeight,self.originalBias)
 def rejection(self,feature,valid):return self.reject((feature*valid[...,None]).sum(1)/valid.sum(1,keepdim=True).clamp_min(1)).squeeze(-1)
 def forward(self,cnn,valid):
  feature=self.neck(cnn);return self.logits(feature),feature,self.rejection(feature,valid)
 def checkpoint(self):
  result={f'neck.{k}':v.detach().contiguous() for k,v in self.neck.state_dict().items()};result.update({'rejectWeight':self.reject.weight.detach().contiguous(),'rejectBias':self.reject.bias.detach().contiguous()});return result
 def load_checkpoint(self,state):
  self.neck.load_state_dict({k[5:]:v for k,v in state.items() if k.startswith('neck.')},strict=True)
  with torch.no_grad():self.reject.weight.copy_(state['rejectWeight']);self.reject.bias.copy_(state['rejectBias'])
