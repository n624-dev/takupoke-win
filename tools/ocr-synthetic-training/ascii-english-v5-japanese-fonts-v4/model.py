"""Unmodified official HF English V5 as an independent model, full438."""
from pathlib import Path
import torch
from transformers import PPOCRV5MobileRecForTextRecognition
ASSETS=Path('/workspace/scratch/ascii-english-v5-transfer-20261014-v1/assets')
def load():return PPOCRV5MobileRecForTextRecognition.from_pretrained(ASSETS,local_files_only=True).eval()
def feature_logits(model,x):
 cnn=model.model(x).last_hidden_state;feature=model.head.encoder(cnn).last_hidden_state
 return feature,model.head.head(feature)
