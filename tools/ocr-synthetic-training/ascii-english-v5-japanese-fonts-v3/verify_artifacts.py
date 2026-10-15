"""Read-only artifact/registration/format verification, no model calls."""
import ast,hashlib,json,shutil
from pathlib import Path
import numpy as np
from data import ROOT,PARENT,RESULTS,save

def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def main():
 source=Path(__file__).parent;freeze=json.loads((ROOT/'freeze.json').read_text(encoding='utf-8'));selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));assert selection['selected']['epoch']==0
 core=['train.py','data.py','model.py','adapter.py','fresh.py','download_fonts.py'];assert all(sha(source/n)==freeze['sources'][n] for n in core);assert sha(source/'protocol.json')==freeze['protocolSha256'];assert sha('/workspace/scratch/ascii-english-v5-font-balanced-20261014-v1/adaptation-v1/epoch-4.safetensors')==freeze['initialStateSha256'];assert sha('/workspace/scratch/ocr-frozen-20261013/v6-medium.onnx')==freeze['japanesePrimarySha256'];assert not (ROOT/'fresh').exists() and not (ROOT/'fresh-cache').exists() and not (RESULTS/'fresh-freeze.json').exists();assert sha('/workspace/scratch/ocr-production-development-20261015-v1/models/ascii-rec.onnx')=='9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703'
 source_hashes={p.name:sha(p) for p in source.glob('*.py')};encoding_issues=[]
 for p in source.glob('*.py'):
  tree=ast.parse(p.read_text(encoding='utf-8'))
  for node in ast.walk(tree):
   if isinstance(node,ast.Call) and isinstance(node.func,ast.Attribute) and node.func.attr in ['read_text','write_text']:
    if not any(k.arg=='encoding' for k in node.keywords):encoding_issues.append([p.name,node.lineno])
 assert not encoding_issues;jsonfiles=0;jsonlrows=0
 for path in RESULTS.rglob('*.json'):
  json.loads(path.read_text(encoding='utf-8'));jsonfiles+=1
 for path in RESULTS.rglob('*.jsonl'):
  for line in path.read_text(encoding='utf-8').splitlines():
   if line:json.loads(line);jsonlrows+=1
 cachefiles=0
 for split in ['train','development','distortion']:
  receipt=json.loads((ROOT/(split+'-cache')/'cache.json').read_text(encoding='utf-8'));manifest=json.loads((ROOT/split/'manifest.json').read_text(encoding='utf-8'));assert len(receipt['rows'])==len(manifest['rows'])
  for row,item in zip(manifest['rows'],receipt['rows']):
   assert sha(ROOT/split/row['file'])==row['pngSha256'];p=ROOT/(split+'-cache')/item['file'];assert sha(p)==item['npzSha256']
   with np.load(p,allow_pickle=False) as cache:
    for key,h in [('input','inputSha256'),('teacher','teacherSha256'),('probability','probabilitySha256')]:assert cache[key].dtype==np.float32 and hashlib.sha256(cache[key].tobytes()).hexdigest()==item[h]
   cachefiles+=1
 package=json.loads((RESULTS/'exploratory-epoch1-v3/package-receipt.json').read_text(encoding='utf-8'));assert all(sha(Path(package['root'])/rel)==item['sha256'] for rel,item in package['files'].items());assert (Path(package['root'])/'dictionary.json').read_bytes()==Path('/workspace/scratch/ocr-production-development-20261015-v1/models/ascii-dictionary.json').read_bytes()
 changed_initial={n:{'initial':h,'current':source_hashes.get(n)} for n,h in freeze['sources'].items() if source_hashes.get(n)!=h};assert set(changed_initial)=={'portable.py'}
 result={'originalSelection':'active-baseline','originalTrialFailed':True,'originalFreshGeneratedOrInferred':False,'trainingInputsAndProtocolUnchanged':True,'initialSourceDraftChanges':changed_initial,'changeExplanation':'Export-only portable.py metadata changed from inherited finalOpenedfalse to freshOpenedtrue/fullDocumentQualifiedfalse for a future successful stage; never run for this failed trial. Later exploratory/diagnostic scripts have independent receipts.','coreSources':core,'currentSourceHashes':source_hashes,'allSourceAstAndExplicitTextEncoding':True,'researchJsonFiles':jsonfiles,'researchJsonlRows':jsonlrows,'allRegisteredPngNpzShaAndExactFp32':cachefiles,'portablePackageFilesVerified':package['fileCount'],'portableModelSha256':sha(Path(package['root'])/'model.onnx'),'oldInitializerActiveOnnxAndJapanesePrimaryUnchanged':True,'ownScratchBytes':sum(p.stat().st_size for p in PARENT.rglob('*') if p.is_file()),'freeDiskBytes':shutil.disk_usage(PARENT).free,'iosEdited':False,'childCommitOrPush':False,'modelActivated':False,'nextOptimizerHeldByParent':True};save(RESULTS/'final-verification.json',result);print(json.dumps({k:v for k,v in result.items() if k not in ['currentSourceHashes','initialSourceDraftChanges','coreSources']}))
if __name__=='__main__':main()
