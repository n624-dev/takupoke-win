"""Native fused CNN plus exact ONNX norm/query-scale order, copied weights."""
import torch
from native_fused import EnglishModel
class NativeNorm(torch.nn.Module):
 def __init__(self,original):
  super().__init__();self.weight=original.weight;self.bias=original.bias;self.register_buffer('epsilon',torch.tensor(original.eps,dtype=torch.float32))
 def forward(self,x):
  centered=x-x.mean(-1,keepdim=True)
  return centered/(centered.pow(2).mean(-1,keepdim=True)+self.epsilon).sqrt()*self.weight+self.bias
class NativeAttention(torch.nn.Module):
 def __init__(self,original):
  super().__init__();self.qkv=original.qkv;self.projection=original.projection;self.num_heads=original.num_heads;self.register_buffer('queryScale',torch.tensor(original.scale,dtype=torch.float32))
 def forward(self,hidden_states,attention_mask=None,**kwargs):
  assert attention_mask is None
  batch,time,channels=hidden_states.shape;qkv=self.qkv(hidden_states).reshape(batch,time,3,self.num_heads,channels//self.num_heads).permute(2,0,3,1,4);q,k,v=qkv.unbind(0);attention=((q*self.queryScale)@k.transpose(-1,-2)).softmax(-1);context=(attention@v).transpose(1,2).reshape(batch,time,channels)
  return self.projection(context),attention
class NativeEnglishMath(EnglishModel):
 def __init__(self):
  super().__init__()
  for block in self.encoder.svtr_block:
   block.layer_norm1=NativeNorm(block.layer_norm1);block.layer_norm2=NativeNorm(block.layer_norm2);block.self_attn=NativeAttention(block.self_attn)
  self.encoder.norm=NativeNorm(self.encoder.norm)
def load():return NativeEnglishMath().eval()
def feature_logits(model,value):return model(value)
