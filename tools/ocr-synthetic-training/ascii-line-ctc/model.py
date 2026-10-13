"""Small baseline-preserving line CTC network; no lexicon or role prior."""
import torch
from torch import nn


class LineCTC(nn.Module):
    def __init__(self):
        super().__init__();layers=[];previous=3
        for channels,stride in [(16,(2,2)),(32,(2,1)),(64,(1,2))]:
            layers.extend([nn.Conv2d(previous,channels,3,stride=stride,padding=1,bias=False),nn.BatchNorm2d(channels),nn.SiLU()]);previous=channels
        self.cnn=nn.Sequential(*layers);self.project=nn.Linear(64*16,96);self.sequence=nn.LSTM(96,64,bidirectional=True,batch_first=True);self.ctc=nn.Linear(128,97);self.reject=nn.Linear(128,1)

    def logits(self,x):
        value=self.cnn(x).permute(0,3,1,2).flatten(2);value=self.project(value);value,_=self.sequence(value);return self.ctc(value),self.reject(value.mean(dim=1)).squeeze(-1)

    def forward(self,x):
        logits,reject=self.logits(x);return logits.softmax(-1),reject.sigmoid()
