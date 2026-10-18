"""Close one completed trial, artifact SHA handoff; no model calls or activation."""
import datetime,hashlib,json,shutil
from pathlib import Path
from data import ROOT,PARENT,REPO,RESULTS,save

def sha(p):
 h=hashlib.sha256()
 with Path(p).open('rb') as f:
  for b in iter(lambda:f.read(1048576),b''):h.update(b)
 return h.hexdigest()
def main():
 selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'));diagnosis=json.loads((RESULTS/'development-diagnosis.json').read_text(encoding='utf-8'));assert (RESULTS/'verification.json').exists();registration=RESULTS.parent/'pre-execution-registration.json';reg=json.loads(registration.read_text(encoding='utf-8'));source=Path(__file__).parent
 for name,h in reg['sources'].items():assert sha(source/name)==h
 freeze=json.loads((ROOT/'freeze.json').read_text(encoding='utf-8'));assert sha(source.parent/'image_tensor.py')==freeze['preprocessSha256']
 from model import ASSETS
 from data import FONTS
 for name,item in freeze['assetReceipt']['files'].items():assert sha(ASSETS/name)==item['sha256']
 for name,h in freeze['fontHashes'].items():assert sha(FONTS[name])==h
 for split in ['train','development']:
  for tail,label in [('manifest.json','manifest'),('../'+split+'-cache/cache.json','cache-receipt')]:
   p=ROOT/split/tail;save(RESULTS/(split+'-'+label+'.json'),json.loads(p.read_text(encoding='utf-8')))
 for name in ['preflight','training']:
  p=Path('/workspace/scratch')/('english-v5-font-balanced-'+name+'-20261014.log');(RESULTS.parent/(name+'-execution.log')).write_bytes(p.read_bytes())
 scratch={str(p.relative_to(PARENT)):{'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(PARENT.rglob('*')) if p.is_file()};save(RESULTS/'external-scratch-file-manifest.json',{'scope':'External PNGs/fonts/checkpoints/NPZ/receipts and preparation scripts; never committed payloads','files':scratch,'bytes':sum(r['bytes'] for r in scratch.values())})
 old=REPO/'research-results/ocr-synthetic-training-20261013/ascii-english-v5-official-transfer-v1/handoff-manifest.json';prior=json.loads(old.read_text(encoding='utf-8'))
 for rel,item in prior['files'].items():assert sha(REPO/rel)==item['sha256'] and (REPO/rel).stat().st_size==item['bytes']
 selected=selection['selected'];closure={'status':'Closed bounded development trial; no automatic final/export/activation. Originalbaseline fallback unless research eligibility met.','selected':selected['name'],'researchUsefulnessGatePassed':diagnosis['researchUsefulnessGatePassed'],'zeroWrongAcceptRequired':False,'oldFrozenZeroWrongRecordsUnchanged':True,'officialBaselineMetrics':selection['officialBaseline']['metrics'],'selectedMetrics':selected['metrics'],'trainingSeconds':selection['trainingSeconds'],'cacheSeconds':selection['cacheSeconds'],'peakRssMiB':selection['peakRssMiB'],'budgetStopped':selection['budgetStopped'],'ownScratchBytes':sum(r['bytes'] for r in scratch.values()),'freeDiskBytes':shutil.disk_usage(PARENT).free,'finalGeneratedOrOpened':False,'portableExportStarted':False,'productActivated':False,'japanesePrimaryChanged':False,'iosEdited':False,'modelPublished':False,'childCommitOrPush':False,'prior46HandoffAllBytesShaUnchanged':True,'registeredProtocolAndInitialSourcesUnchanged':True,'closedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'limits':'ASCII whole-crop specialist research; not CJK whole-field/document qualification or calibrated confidence. Newdev is consumed after this trial; Sawarabi remains reserved.'};save(RESULTS/'closure.json',closure)
 policy=Path('/workspace/internal/takupoke-win-ocr-training-policy.md')
 with policy.open('a',encoding='utf-8',newline='\n') as f:f.write('\n## English V5 font-balanced-v1 closed\n\n'+json.dumps(closure,ensure_ascii=False)+'\n\nAllold46handoff SHA unchanged, allnewFP32 cache/input/prob/feature restore and checkpoint/BN/full438/source audits pass. No further optimization/weightdownload/final/ONNX/CoreML/productactivation. Parent integrates source/docs/JSON only; weights/PNGs/cache/internalMD remain outsideGit. Fontserialization initialmetadata mistakenly said4.61.0; installed4.61.1 was corrected in addendum without changing initial audit/bytes or rerunning models. Newdev now consumed, never called final. InternalMDpush forbidden, childcommit/push and iOS edits none.\n')
 print(json.dumps(closure))
if __name__=='__main__':main()
