"""Original fullhead plus lowLR SVTR, ASCII deltas and independent reject."""
import torch
import torch.nn.functional as F

from svtr_exact import MediumSvtr


class Adapter(torch.nn.Module):
 def __init__(self,initializers,dictionary):
  super().__init__();self.svtr=MediumSvtr(initializers);self.register_buffer('originalWeight',initializers['linear_8.w_0'].T.contiguous());self.register_buffer('originalBias',initializers['linear_8.b_0']);self.register_buffer('asciiIndices',torch.tensor([dictionary.index(chr(c)) for c in range(32,127)]));self.deltaWeight=torch.nn.Parameter(torch.zeros(95,192));self.deltaBias=torch.nn.Parameter(torch.zeros(95));self.reject=torch.nn.Linear(192,1);torch.nn.init.zeros_(self.reject.weight);torch.nn.init.zeros_(self.reject.bias)
 def logits(self,features):return F.linear(features,self.originalWeight,self.originalBias).index_add(-1,self.asciiIndices,F.linear(features,self.deltaWeight,self.deltaBias))
 def rejection(self,features,valid):return self.reject((features*valid[...,None]).sum(1)/valid.sum(1,keepdim=True).clamp_min(1)).squeeze(-1)
 def forward(self,context,shortcut,valid):
  features=self.svtr(context,shortcut);return self.logits(features),features,self.rejection(features,valid)
 def checkpoint(self):
  result={f'svtr.{k}':v.detach().contiguous() for k,v in self.svtr.state_dict().items()};result.update({'deltaWeight':self.deltaWeight.detach().contiguous(),'deltaBias':self.deltaBias.detach().contiguous(),'rejectWeight':self.reject.weight.detach().contiguous(),'rejectBias':self.reject.bias.detach().contiguous()});return result
 def load_checkpoint(self,state):
  self.svtr.load_state_dict({k[5:]:v for k,v in state.items() if k.startswith('svtr.')},strict=True)
  with torch.no_grad():self.deltaWeight.copy_(state['deltaWeight']);self.deltaBias.copy_(state['deltaBias']);self.reject.weight.copy_(state['rejectWeight']);self.reject.bias.copy_(state['rejectBias'])
