"""Package research artifacts with external preprocessing and provenance."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

import onnx
import yaml


def main():
    p=argparse.ArgumentParser();p.add_argument('--assets',required=True,type=Path);p.add_argument('--experiment',required=True,type=Path);p.add_argument('--results',required=True,type=Path);a=p.parse_args()
    final=json.loads((a.results/'final-summary.json').read_text(encoding='utf-8'));selection=json.loads((a.experiment/'selection.json').read_text(encoding='utf-8'));chosen=Path(selection['selected']['path'])
    if hashlib.sha256((chosen/'model.safetensors').read_bytes()).hexdigest()!=selection['selected']['sha256']:raise ValueError('Selection changed')
    root=a.experiment/'portable-package';root.mkdir(exist_ok=False);torch_dir=root/'torch';torch_dir.mkdir()
    for name in ['model.safetensors','config.json','inference.yml']:shutil.copyfile(chosen/name,torch_dir/name)
    shutil.copyfile(a.experiment/'selected.onnx',root/'recognizer.onnx')
    model=onnx.load(root/'recognizer.onnx');domains=sorted({node.domain for node in model.graph.node})
    if any(domain not in ['', 'ai.onnx'] for domain in domains):raise ValueError(f'Custom domain: {domains}')
    dictionary=['']+yaml.safe_load((a.assets/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' ']
    (root/'dictionary.json').write_text(json.dumps(dictionary,ensure_ascii=False,separators=(',',':'))+'\n', encoding='utf-8',newline='\n')
    shutil.copyfile(Path(__file__).parent/'image_tensor.py',root/'reference_preprocessing_ctc.py')
    preprocessing={'inputName':'x','dtype':'float32','layout':'NCHW','batch':1,'channels':'BGR','height':48,'minimumWidth':320,'maximumWidth':960,'inputWidth':'floor(48 * max(320/48, originalWidth/originalHeight)); reject >960','validWidth':'min(inputWidth, ceil(48*originalWidth/originalHeight))','resampling':'original-pixel-center bilinear: (dst+0.5)*sourceExtent/destinationExtent-0.5, clamp to source bounds; floor(channel+0.5) to uint8','normalization':'(uint8/255 - 0.5)/0.5','padding':'float32 0 in normalized domain, after validWidth','ctc':{'blankIndex':0,'classes':18710,'spaceIndex':18709,'selection':'argmax each time step, first index on tie; merge consecutive same nonblank, blank resets repetition','intervals':'[Start,End) time steps retained; repeated piece confidence=max; never silently crop out padding predictions','coordinates':'Reject if End*inputWidth > validWidth*outputTimeSteps. sourceLeft=Crop.X+Start*inputWidth/(outputTimeSteps*validWidth)*Crop.Width; sourceRight analogously End; y/height=Crop.Y/Crop.Height. Reject box outside Crop; never clamp an unsupported interval. Native production SourceBox remains authoritative.'},'confidence':'uncalibrated; minimum piece confidence does not independently prove correct/undamaged text','outputCorrection':'none; no lexicon substitution or oracle candidate selection'}
    preprocessing['outputName']='probabilities'
    preprocessing['formatContracts']={
        'onnx':{'input':'float32 [batch,3,48,width]','output':'float32 [batch,timeSteps,18710]','consumerBounds':'Use batch1 and width320..960; ONNX symbolic dimensions do not enforce these application bounds.'},
        'coreml':{'input':'float32 [1,3,48,width], RangeDim320..960, default320','output':'probabilities FLOAT32; outer feature description does not declare output shape','consumerValidation':'Read runtime output dimensions and require [1,timeSteps,18710]; CoreML predictions remain untested on Apple runtime.'},
    }
    (root/'preprocessing.json').write_text(json.dumps(preprocessing,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n')
    shutil.copytree(a.experiment/'licenses',root/'source-licenses')
    coreml=a.experiment/'selected.mlpackage'
    if coreml.exists():shutil.copytree(coreml,root/'recognizer.mlpackage')
    evaluation=root/'evaluation';evaluation.mkdir()
    shutil.copyfile(a.results/'README.md',root/'README.md')
    for name in ['protocol.json','ood-protocol.json']:
        shutil.copyfile(Path(__file__).parent/name,evaluation/name)
    corpus_provenance={}
    for split in ['train','development','final','ood']:
        manifest=json.loads((a.experiment/split/'manifest.json').read_text(encoding='utf-8'))
        corpus_provenance[split]={key:manifest[key] for key in ['protocolSha256','fontSha256','poolCharacters','poolSha256'] if key in manifest}
        corpus_provenance[split]['rows']=len(manifest['rows'])
        corpus_provenance[split]['manifestSha256']=hashlib.sha256((a.experiment/split/'manifest.json').read_bytes()).hexdigest()
    (evaluation/'corpus-provenance.json').write_text(json.dumps(corpus_provenance,indent=2)+'\n', encoding='utf-8',newline='\n')
    for name in ['final-summary.json','ood-summary.json','onnx-profile-selected.json','onnx-profile-baseline.json','coreml-conversion.json','reproduction-receipt.json']:
        source=a.results/name
        if source.exists():shutil.copyfile(source,evaluation/name)
    shutil.copyfile(a.experiment/'selection.json',evaluation/'development-selection.json')
    receipt={'status':'research-only; unqualified, not activated, iOS code untouched','selected':selection['selected'],'base':json.loads((a.assets/'download-receipt.json').read_text(encoding='utf-8')),'onnxDomains':domains,'onnxOpsets':[{'domain':entry.domain,'version':entry.version} for entry in model.opset_import],'preprocessingSha256':hashlib.sha256((root/'preprocessing.json').read_bytes()).hexdigest(),'dictionarySha256':hashlib.sha256((root/'dictionary.json').read_bytes()).hexdigest(),'platformNotes':'ONNX uses standard operators and can be evaluated with ONNX Runtime Windows/mobile. CoreML if present is conversion-only on Linux; iPhone prediction fidelity, dynamic shapes, deployment, speed, memory and document safety remain untested. No iOS repository write.','finalSummary':final,'files':{str(f.relative_to(root)):{'bytes':f.stat().st_size,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()} for f in sorted(root.rglob('*')) if f.is_file()}}
    (root/'provenance.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n');(a.results/'portable-package-receipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n', encoding='utf-8',newline='\n');print(json.dumps({'package':str(root),'onnxDomains':domains,'dictionarySha256':receipt['dictionarySha256'],'files':len(receipt['files'])}),flush=True)


if __name__=='__main__':main()
