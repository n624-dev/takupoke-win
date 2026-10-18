"""Independent unknown-character probe; no checkpoint selection or repair."""
import argparse
import hashlib
import json
import random
from pathlib import Path

from fontTools.ttLib import TTFont
from transformers import PPOCRV6SmallRecForTextRecognition
import torch
import yaml

from corpus import FONTS, render, load_arrays
from train import evaluate
from image_tensor import tensor


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',required=True,type=Path);p.add_argument('--experiment',required=True,type=Path);p.add_argument('--results',required=True,type=Path);a=p.parse_args()
    if not (a.results/'final-summary.json').exists():raise ValueError('Main final evaluation required first')
    if (a.results/'ood-summary.json').exists():raise ValueError('OOD already opened')
    torch.set_num_threads(2);torch.set_num_interop_threads(1)
    spec=json.loads((Path(__file__).parent/'ood-protocol.json').read_text(encoding='utf-8'));selection=json.loads((a.experiment/'selection.json').read_text(encoding='utf-8'));chosen=Path(selection['selected']['path'])
    if hashlib.sha256((chosen/'model.safetensors').read_bytes()).hexdigest()!=selection['selected']['sha256']:raise ValueError('Selection hash changed')
    dictionary=['']+yaml.safe_load((a.assets/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' ']
    seen=set()
    for split in ['train','development']:
        for row in json.loads((a.experiment/split/'manifest.json').read_text(encoding='utf-8'))['rows']:seen.update(row['text'])
    cmaps=[set(TTFont(FONTS[font]).getBestCmap()) for font in spec['fonts']]
    pool=sorted(c for c in set(dictionary) if len(c)==1 and 0x4e00<=ord(c)<=0x9faf and c not in seen and all(ord(c) in cmap for cmap in cmaps))
    rng=random.Random(spec['seed']);directory=a.experiment/'ood';directory.mkdir(exist_ok=False);rows=[]
    for i in range(spec['rows']):
        label=''.join(rng.choice(pool) for _ in range(rng.randint(*spec['length'])));font=spec['fonts'][i%2]
        image=render(label,rng,FONTS[font]);path=directory/f'{i:04d}.png';image.save(path)
        rows.append({'id':f'ood-{i:04d}','category':'untrained-cjk','font':font,'text':label,'file':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
    manifest={'rows':rows,'protocolSha256':hashlib.sha256(json.dumps(spec,sort_keys=True).encode()).hexdigest(),'poolCharacters':len(pool),'poolSha256':hashlib.sha256(''.join(pool).encode()).hexdigest()};(directory/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n');arrays=load_arrays(manifest,directory)
    results={}
    for name,path in [('baseline',a.assets),('selected',chosen)]:
        model=PPOCRV6SmallRecForTextRecognition.from_pretrained(path,local_files_only=True).eval();result=evaluate(model,arrays,rows,dictionary);results[name]=result
        (a.results/f'ood-{name}-records.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n')
    summary={'status':'research-only; no selection or repair; not qualified','protocolSha256':manifest['protocolSha256'],'poolCharacters':len(pool),'groups':{name:result['groups'] for name,result in results.items()},'improvements':sum(not x['exact'] and y['exact'] for x,y in zip(results['baseline']['records'],results['selected']['records'])),'regressions':sum(x['exact'] and not y['exact'] for x,y in zip(results['baseline']['records'],results['selected']['records']))}
    (a.results/'ood-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n');print(json.dumps(summary,ensure_ascii=False),flush=True)


if __name__=='__main__':main()
