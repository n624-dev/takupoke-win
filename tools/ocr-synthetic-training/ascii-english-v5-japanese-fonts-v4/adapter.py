"""V1 CNN fixed byte-for-byte, official encoder/full438 head/reject adapt."""
import torch
from model import load
class Adapter(torch.nn.Module):
 def __init__(self):
  super().__init__();self.official=load();self.reject=torch.nn.Linear(120,1);torch.nn.init.zeros_(self.reject.weight);torch.nn.init.zeros_(self.reject.bias)
  for p in self.official.model.parameters():p.requires_grad_(False)
 def from_cnn(self,cnn,valid):
  feature=self.official.head.encoder(cnn).last_hidden_state;logits=self.official.head.head(feature);rejected=self.reject((feature*valid[...,None]).sum(1)/valid.sum(1,keepdim=True).clamp_min(1)).squeeze(-1);return logits,feature,rejected
 def forward(self,x,valid):return self.from_cnn(self.official.model(x).last_hidden_state,valid)
