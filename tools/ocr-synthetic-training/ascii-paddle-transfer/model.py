"""Frozen official CNN with copied ASCII CTC rows and an independent reject head."""
import hashlib
from pathlib import Path
import torch
from transformers import PPOCRV6SmallRecForTextRecognition
import yaml

from data import DICTIONARY

ASSETS=Path('/workspace/scratch/ocr-training-assets')
BASE_SHA='f65a332afe5aa663f0b9d5706f4ae8457b5b4058a842d5c1eb22df505c27d642'


def load_base():
    assert hashlib.sha256((ASSETS/'model.safetensors').read_bytes()).hexdigest()==BASE_SHA
    model=PPOCRV6SmallRecForTextRecognition.from_pretrained(ASSETS,local_files_only=True).eval()
    dictionary=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' ']
    assert len(dictionary)==18710 and len(set(dictionary))==18710
    return model,dictionary


class Transfer(torch.nn.Module):
    def __init__(self,base,official_dictionary):
        super().__init__()
        self.recognizer=base
        source=base.head.head
        copied=[official_dictionary.index(char) for char in DICTIONARY[:-1]]
        self.register_buffer('copiedOfficialIndices',torch.tensor(copied,dtype=torch.long))
        replacement=torch.nn.Linear(source.in_features,len(DICTIONARY))
        excluded=torch.ones(len(official_dictionary),dtype=torch.bool);excluded[copied]=False
        with torch.no_grad():
            replacement.weight[:-1].copy_(source.weight[copied]);replacement.bias[:-1].copy_(source.bias[copied])
            # Deterministic mean-row initializer only; NOT an exact aggregation
            # of excluded logits or a claim of preserved confidence.
            replacement.weight[-1].copy_(source.weight[excluded].mean(0));replacement.bias[-1].copy_(source.bias[excluded].mean())
        assert torch.equal(replacement.weight[:-1],source.weight[copied])
        assert torch.equal(replacement.bias[:-1],source.bias[copied])
        base.head.head=replacement;base.config.head_out_channels=len(DICTIONARY)
        self.reject=torch.nn.Linear(source.in_features,1)
        torch.nn.init.zeros_(self.reject.weight);torch.nn.init.zeros_(self.reject.bias)
        for parameter in base.model.parameters():parameter.requires_grad=False
        self.eval()

    def encoded(self,features):
        return self.recognizer.head.encoder(features).last_hidden_state

    def from_features(self,features):
        encoded=self.encoded(features)
        return self.recognizer.head.head(encoded),self.reject(encoded.mean(1)).squeeze(-1)

    def forward(self,x):
        features=self.recognizer.model(x).last_hidden_state
        logits,reject_logit=self.from_features(features)
        return logits.softmax(-1),reject_logit.sigmoid()
