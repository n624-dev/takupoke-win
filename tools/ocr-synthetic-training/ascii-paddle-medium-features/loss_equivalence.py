"""Validate full-objective loss compression when only ASCII rows can change."""
import hashlib
import json
from pathlib import Path
import torch
import torch.nn.functional as F
from safetensors.torch import load_file

ROOT=Path('/workspace/scratch/ascii-paddle-medium-features-20261013-v1')
RESULTS=Path('/workspace/scratch/takupoke-win/research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-features-v1')


def main():
    torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.manual_seed(9921741)
    dictionary=json.loads(Path('/workspace/scratch/ocr-frozen-20261013/v6-medium-dictionary.json').read_text(encoding='utf-8'))
    index={char:i for i,char in enumerate(dictionary)};ascii_indices=torch.tensor([index[chr(c)] for c in range(32,127)])
    head=load_file(str(ROOT/'original-full-ctc-head.safetensors'))
    feature=torch.randn(2,16,192);base=F.linear(feature,head['weight'],head['bias'])
    labels=['pR7','星雲8'];pool=sorted({0,*ascii_indices.tolist(),*(index[c] for value in labels for c in value)})
    pool_index=torch.tensor(pool);mapping={full:i for i,full in enumerate(pool)}
    ascii_in_pool=torch.tensor([mapping[i] for i in ascii_indices.tolist()]);remaining=torch.ones(len(dictionary),dtype=torch.bool);remaining[pool_index]=False
    rest=base[:,:,remaining].logsumexp(-1,keepdim=True);compressed_base=torch.cat([base.index_select(-1,pool_index),rest],-1)
    dw=(torch.randn(95,192)*.03).requires_grad_();db=(torch.randn(95)*.03).requires_grad_();cw=dw.detach().clone().requires_grad_();cb=db.detach().clone().requires_grad_()
    full=base.index_add(-1,ascii_indices,F.linear(feature,dw,db));compressed=compressed_base.index_add(-1,ascii_in_pool,F.linear(feature,cw,cb))
    targets=torch.tensor([index[c] for value in labels for c in value]);target_lengths=torch.tensor([len(value) for value in labels]);input_lengths=torch.tensor([16,16]);compressed_targets=torch.tensor([mapping[i] for i in targets.tolist()])
    loss_fn=torch.nn.CTCLoss(blank=0,zero_infinity=False)
    a_ctc=loss_fn(full.log_softmax(-1).transpose(0,1),targets,input_lengths,target_lengths)
    b_ctc=loss_fn(compressed.log_softmax(-1).transpose(0,1),compressed_targets,input_lengths,target_lengths)
    a_kl=F.kl_div(full.log_softmax(-1),base.softmax(-1),reduction='sum')/32
    b_kl=F.kl_div(compressed.log_softmax(-1),compressed_base.softmax(-1),reduction='sum')/32
    a=(a_ctc+.1*a_kl);b=(b_ctc+.1*b_kl);a.backward();b.backward()
    difference={'ctc':abs(float(a_ctc.detach()-b_ctc.detach())),'fullTeacherKl':abs(float(a_kl.detach()-b_kl.detach())),'total':abs(float(a.detach()-b.detach())),'weightGradientMax':float((dw.grad-cw.grad).abs().max()),'biasGradientMax':float((db.grad-cb.grad).abs().max())}
    assert difference['ctc']<1e-5 and difference['fullTeacherKl']<1e-5 and difference['weightGradientMax']<1e-5 and difference['biasGradientMax']<1e-5
    result={'scope':'Synthetic numeric feature/objective equivalence only; no image, oracle, optimizer step, training or quality claim','originalClasses':len(dictionary),'lossClasses':len(pool)+1,'exactPrinciple':'All trainable ASCII rows and every CTC target class are retained. Remaining unmodified rows enter one exact logsumexp OTHER scalar. OTHER is never a target. CTC paths and denominator are unchanged. Every omitted teacher/student logit is identical, so their teacher-KL probability ratios share the same normalization shift and aggregate exactly. Inference still uses full18710 rows/dictionary.','differences':difference,'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'threshold':1e-5,'floatType':'float32'}
    (RESULTS/'loss-equivalence.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8',newline='\n');print(json.dumps(result))


if __name__=='__main__':main()
