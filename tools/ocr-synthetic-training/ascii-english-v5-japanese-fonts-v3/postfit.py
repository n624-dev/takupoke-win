"""Post-frozen-selection train fit diagnosis, not independent quality credit or reselection."""
import importlib.util,json
from pathlib import Path
import torch,yaml
from safetensors.torch import load_file
from data import ROOT,RESULTS,save
from model import ASSETS
from adapter import Adapter
_s=importlib.util.spec_from_file_location('v3_postfit_training',Path(__file__).with_name('train.py'));_m=importlib.util.module_from_spec(_s);_s.loader.exec_module(_m)
def main():
 selected=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));c=selected['selected'];selected_for_development=c['epoch']>0
 if selected_for_development:assert (RESULTS/'fresh-comparison.json').exists()
 else:
  assert not (ROOT/'fresh').exists();c=next(x for x in selected['candidates'] if x['epoch']==3)
 torch.manual_seed(107641);torch.set_num_threads(2);torch.set_num_interop_threads(1);manifest=json.loads((ROOT/'train/manifest.json').read_text(encoding='utf-8'));receipt=json.loads((ROOT/'train-cache/cache.json').read_text(encoding='utf-8'));dictionary=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' '];summaries={}
 for name,path in [('trainfit-active',selected['activeBaseline']['path']),('trainfit-candidate',c['path'])]:
  model=Adapter().eval();model.load_state_dict(load_file(path),strict=True);metrics,_=_m.evaluate(name,model,receipt,ROOT/'train-cache',manifest,dictionary,'trained');summaries[name]=metrics;del model
 save(RESULTS/'trainfit-comparison.json',{'evaluatedCheckpointSha256':c['sha256'],'selectedForDevelopment':selected_for_development,'actualSelectionUnchanged':True,'active':summaries['trainfit-active'],'candidate':summaries['trainfit-candidate'],'scope':'TRAIN resubstitution diagnostic; not generalization/fresh/full-document credit','noSelectionOrOptimization':True,'sourceSha256':_m.sha(__file__)})
if __name__=='__main__':main()
