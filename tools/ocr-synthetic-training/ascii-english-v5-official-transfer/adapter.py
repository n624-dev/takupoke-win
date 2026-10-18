"""All official CNN/encoder/full438 parameters adapt; independent reject121."""
import torch
from model import load,feature_logits
class Adapter(torch.nn.Module):
 def __init__(self):
  super().__init__();self.official=load();self.reject=torch.nn.Linear(120,1);torch.nn.init.zeros_(self.reject.weight);torch.nn.init.zeros_(self.reject.bias)
 def forward(self,x,valid):
  feature,logits=feature_logits(self.official,x);reject=self.reject((feature*valid[...,None]).sum(1)/valid.sum(1,keepdim=True).clamp_min(1)).squeeze(-1)
  return logits,feature,reject
