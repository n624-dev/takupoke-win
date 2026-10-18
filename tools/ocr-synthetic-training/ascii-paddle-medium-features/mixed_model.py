"""Full original18710 head with ASCII-only deltas and valid-frame rejection."""
import torch
import torch.nn.functional as F


class DeltaHead(torch.nn.Module):
 def __init__(self,weight,bias,dictionary,pool):
  super().__init__();index={c:i for i,c in enumerate(dictionary)};ascii_indices=[index[chr(c)] for c in range(32,127)];mapping={full:i for i,full in enumerate(pool)}
  self.register_buffer('originalWeight',weight);self.register_buffer('originalBias',bias);self.register_buffer('asciiIndices',torch.tensor(ascii_indices));self.register_buffer('asciiPoolIndices',torch.tensor([mapping[i] for i in ascii_indices]))
  self.deltaWeight=torch.nn.Parameter(torch.zeros(95,192));self.deltaBias=torch.nn.Parameter(torch.zeros(95));self.reject=torch.nn.Linear(192,1);torch.nn.init.zeros_(self.reject.weight);torch.nn.init.zeros_(self.reject.bias)
 def loss_logits(self,features,original_pool):
  return original_pool.index_add(-1,self.asciiPoolIndices,F.linear(features,self.deltaWeight,self.deltaBias))
 def full_logits(self,features):
  return F.linear(features,self.originalWeight,self.originalBias).index_add(-1,self.asciiIndices,F.linear(features,self.deltaWeight,self.deltaBias))
 def rejection(self,features,valid_mask):
  mean=(features*valid_mask[...,None]).sum(1)/valid_mask.sum(1,keepdim=True).clamp_min(1)
  return self.reject(mean).squeeze(-1)
 def checkpoint(self):
  return {'deltaWeight':self.deltaWeight.detach().contiguous(),'deltaBias':self.deltaBias.detach().contiguous(),'rejectWeight':self.reject.weight.detach().contiguous(),'rejectBias':self.reject.bias.detach().contiguous()}
 def load_checkpoint(self,state):
  assert set(state)=={'deltaWeight','deltaBias','rejectWeight','rejectBias'}
  with torch.no_grad():
   self.deltaWeight.copy_(state['deltaWeight']);self.deltaBias.copy_(state['deltaBias']);self.reject.weight.copy_(state['rejectWeight']);self.reject.bias.copy_(state['rejectBias'])
