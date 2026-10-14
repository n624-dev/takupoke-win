"""Adapt early height/localneck with full original classifier and frozen SVTR."""
import torch
import torch.nn.functional as F
from neck import HeightNeck


class Adapter(torch.nn.Module):
 def __init__(self,initializers,dictionary):
  super().__init__();self.neck=HeightNeck(initializers);self.register_buffer('originalWeight',initializers['linear_8.w_0'].T.contiguous());self.register_buffer('originalBias',initializers['linear_8.b_0']);self.register_buffer('asciiIndices',torch.tensor([dictionary.index(chr(c)) for c in range(32,127)]));self.deltaWeight=torch.nn.Parameter(torch.zeros(95,192));self.deltaBias=torch.nn.Parameter(torch.zeros(95));self.reject=torch.nn.Linear(192,1);torch.nn.init.zeros_(self.reject.weight);torch.nn.init.zeros_(self.reject.bias)
 def logits(self,feature):return F.linear(feature,self.originalWeight,self.originalBias).index_add(-1,self.asciiIndices,F.linear(feature,self.deltaWeight,self.deltaBias))
 def rejection(self,feature,valid):return self.reject((feature*valid[...,None]).sum(1)/valid.sum(1,keepdim=True).clamp_min(1)).squeeze(-1)
 def forward(self,cnn,valid):
  feature=self.neck(cnn);return self.logits(feature),feature,self.rejection(feature,valid)
 def checkpoint(self):
  result={f'neck.{k}':v.detach().contiguous() for k,v in self.neck.state_dict().items()};result.update({'deltaWeight':self.deltaWeight.detach().contiguous(),'deltaBias':self.deltaBias.detach().contiguous(),'rejectWeight':self.reject.weight.detach().contiguous(),'rejectBias':self.reject.bias.detach().contiguous()});return result
 def load_checkpoint(self,state):
  self.neck.load_state_dict({k[5:]:v for k,v in state.items() if k.startswith('neck.')},strict=True)
  with torch.no_grad():self.deltaWeight.copy_(state['deltaWeight']);self.deltaBias.copy_(state['deltaBias']);self.reject.weight.copy_(state['rejectWeight']);self.reject.bias.copy_(state['rejectBias'])
