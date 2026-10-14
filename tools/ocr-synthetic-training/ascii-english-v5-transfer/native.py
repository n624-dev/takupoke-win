"""Pinned English V5 native-equivalent Torch class, full438 head retained."""
from pathlib import Path
import torch
from transformers import PPOCRV5MobileRecForTextRecognition
ASSETS=Path('/workspace/scratch/ascii-english-v5-transfer-20261014-v1/assets')
def load():
 model=PPOCRV5MobileRecForTextRecognition.from_pretrained(ASSETS,local_files_only=True).eval()
 # Original ONNX explicit normalization scalars: first4 1e-5, terminal1e-6.
 for block in model.head.encoder.svtr_block:
  block.layer_norm1.eps=9.999999747378752e-06;block.layer_norm2.eps=9.999999747378752e-06
 model.head.encoder.norm.eps=9.999999974752427e-07
 return model
def feature_logits(model,value):
 cnn=model.model(value).last_hidden_state;feature=model.head.encoder(cnn).last_hidden_state
 return feature,model.head.head(feature)
