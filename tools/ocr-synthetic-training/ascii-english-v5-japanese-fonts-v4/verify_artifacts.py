"""Final byte/hash integrity assessment, with no model execution or selection."""
import ast,hashlib,json,shutil
from pathlib import Path
import numpy as np
from PIL import Image
from data import ROOT,PARENT,RESULTS,FONTS,sha,save,tensor
from assets import LICENSES

def main():
 source=Path(__file__).parent;freeze=json.loads((ROOT/'freeze.json').read_text(encoding='utf-8'));selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));assert sha(source/'protocol.json')==freeze['protocolSha256'];assert all(sha(source/n)==h for n,h in freeze['sources'].items());assert all(sha(c['path'])==c['sha256'] for c in [selection['activeBaseline']]+selection['candidates']);assert sha('/workspace/scratch/ocr-frozen-20261013/v6-medium.onnx')==freeze['japanesePrimarySha256'];assert sha('/workspace/scratch/ocr-production-development-20261015-v1/models/ascii-rec.onnx')==json.loads((source/'protocol.json').read_text())['initializerAndTeacher']['activeOnnxSha256'];assert not set(FONTS)&{'BIZUDPGothic','ZenMaruGothic','BIZUDPMincho','MPLUS2'}
 for path in source.glob('*.py'):ast.parse(path.read_text(encoding='utf-8'),filename=str(path))
 ready=json.loads((PARENT/'assets/readiness.json').read_text(encoding='utf-8'))
 for name,item in ready['fonts'].items():assert sha(FONTS[name])==item['sha256'] and sha(LICENSES[name])==item['licenseSha256']
 counts={'json':0,'jsonlRows':0,'sourcePng':0,'cacheNpz':0,'fp32Arrays':0};split_receipts={};splits=['train','development']+(['fresh'] if (ROOT/'fresh').exists() else [])
 for split in splits:
  manifest=json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'));cache=json.loads((ROOT/(split+'-cache')/'cache.json').read_text(encoding='utf-8'));assert len(manifest['rows'])==len(cache['rows']);assert all(sha(FONTS[n])==h for n,h in manifest['fonts'].items())
  for row,item in zip(manifest['rows'],cache['rows']):
   png=ROOT/split/row['file'];npz=ROOT/(split+'-cache')/item['file'];assert row['id']==item['id'];assert sha(png)==row['pngSha256'];assert sha(npz)==item['npzSha256'];x,valid=tensor(Image.open(png));assert x.dtype==np.float32 and valid==row['validWidth']==item['validWidth'];assert hashlib.sha256(x.tobytes()).hexdigest()==row['inputSha256']==item['inputSha256'];counts['sourcePng']+=1;counts['cacheNpz']+=1
   with np.load(npz,allow_pickle=False) as values:
    for key in values.files:
     array=values[key];assert array.dtype==np.float32 and list(array.shape)==item['shape'][key] and np.isfinite(array).all();assert hashlib.sha256(array.tobytes()).hexdigest()==item[key+'Sha256'];counts['fp32Arrays']+=1
    if item['inputStored']:assert values['input'].tobytes()==x.tobytes()
  split_receipts[split]={'rows':len(manifest['rows']),'manifestSha256':sha(ROOT/split/'manifest.json'),'cacheReceiptSha256':sha(ROOT/(split+'-cache')/'cache.json')}
 for folder in [ROOT,RESULTS]:
  for p in folder.rglob('*.json'):json.loads(p.read_text(encoding='utf-8'));counts['json']+=1
  for p in folder.rglob('*.jsonl'):
   for line in p.read_text(encoding='utf-8').splitlines():json.loads(line);counts['jsonlRows']+=1
 portable=None;package=RESULTS/'portable-v4/package-receipt.json'
 if package.exists():
  receipt=json.loads(package.read_text(encoding='utf-8'));bundle=Path(receipt['root'])
  for n,item in receipt['files'].items():assert sha(bundle/n)==item['sha256'] and (bundle/n).stat().st_size==item['bytes']
  portable={'files':len(receipt['files']),'bytes':receipt['totalBytes'],'allHashesVerified':True}
 scratch_bytes=sum(p.stat().st_size for p in PARENT.rglob('*') if p.is_file());assert scratch_bytes<=1.3*1024**3 and shutil.disk_usage(PARENT).free>=1024**3
 result={'status':'All current hashes, losslessFP32 arrays, PNG-derived inputs and syntax verified; no quality reselection','counts':counts,'splits':split_receipts,'all28FontAndLicenseHashesUnchanged':True,'frozenTrainingSourcesAndProtocolUnchanged':True,'allEpochCheckpointsVerified':True,'activeAndJapanesePrimaryUnchanged':True,'portable':portable,'ownScratchBytes':scratch_bytes,'freeBytes':shutil.disk_usage(PARENT).free,'freshOpened':(ROOT/'fresh').exists(),'selected':selection['selected']['name'],'iosEdited':False,'defaultSwitched':False,'commitOrPushByThisAgent':False};save(RESULTS/'final-verification.json',result)
 files={str(p.relative_to(RESULTS)):{'bytes':p.stat().st_size,'sha256':sha(p)} for p in RESULTS.rglob('*') if p.is_file() and p.name!='artifact-index.json'};save(RESULTS/'artifact-index.json',{'researchFiles':files,'sourceFiles':{p.name:sha(p) for p in source.glob('*') if p.is_file()},'indexSelfExcluded':True});print(json.dumps(result),flush=True)

if __name__=='__main__':main()
