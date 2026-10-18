"""Post-selection train resubstitution; never used for selection or fresh tuning."""
import json
from pathlib import Path
import torch,yaml
from safetensors.torch import load_file
from data import ROOT,RESULTS,save,sha
from model import ASSETS
from adapter import Adapter
from metrics import evaluate

def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));c=selection['selected'];eligible=c['epoch']>0
 if eligible:assert (RESULTS/'fresh-comparison.json').exists()
 else:
  assert not (ROOT/'fresh').exists();c=next(v for v in selection['candidates'] if v['epoch']==4)
 save(RESULTS/'postfit-freeze.json',{'sourceSha256':sha(__file__),'selectionSha256':sha(ROOT/'selection.json'),'evaluatedCheckpointSha256':c['sha256'],'checkpointRule':'Selected candidate after fresh, otherwise fixed final epoch4','trainResubstitutionOnly':True,'noFurtherOptimizationOrSelection':True})
 torch.set_num_threads(2);torch.set_num_interop_threads(1);torch.manual_seed(108601);manifest=json.loads((ROOT/'train/manifest.json').read_text(encoding='utf-8'));receipt=json.loads((ROOT/'train-cache/cache.json').read_text(encoding='utf-8'));dictionary=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' '];summaries={}
 for name,path in [('trainfit-active',selection['activeBaseline']['path']),('trainfit-candidate',c['path'])]:
  model=Adapter().eval();model.load_state_dict(load_file(path),strict=True);metrics,_=evaluate(name,model,receipt,ROOT/'train-cache',manifest,dictionary,'trained');summaries[name]=metrics;del model
 save(RESULTS/'trainfit-comparison.json',{'evaluatedCheckpointSha256':c['sha256'],'selectedForDevelopment':eligible,'actualSelectionUnchanged':True,'active':summaries['trainfit-active'],'candidate':summaries['trainfit-candidate'],'scope':'TRAIN resubstitution diagnostic, not generalization or full-document credit','noSelectionOrOptimization':True})

if __name__=='__main__':main()
