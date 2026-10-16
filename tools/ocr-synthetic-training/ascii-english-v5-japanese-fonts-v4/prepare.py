"""Register protocol/source/asset/exclusion/corpora before any model call."""
import hashlib,json,shutil
from pathlib import Path
from data import ROOT,PARENT,RESULTS,FONTS,save,sha,exclusion,generate,PRINTABLE,digest

def main():
 ROOT.mkdir(parents=True,exist_ok=True);RESULTS.mkdir(parents=True,exist_ok=True)
 if (ROOT/'registration-freeze.json').exists():raise RuntimeError('Registration cannot repeat')
 source=Path(__file__).parent;protocol=json.loads((source/'protocol.json').read_text(encoding='utf-8'));assets=json.loads((PARENT/'assets/readiness.json').read_text(encoding='utf-8'));assert assets['all28FontsVerified'];assert shutil.disk_usage(PARENT).free>=1024**3
 for name,item in assets['fonts'].items():assert sha(FONTS[name])==item['sha256'] and sha(item['license'])==item['licenseSha256']
 checkpoint=Path('/workspace/scratch/ascii-english-v5-font-balanced-20261014-v1/adaptation-v1/epoch-4.safetensors');assert sha(checkpoint)==protocol['initializerAndTeacher']['checkpointSha256'];assert not Path('/workspace/scratch/ascii-english-v5-japanese-fonts-v3/adaptation-v3/fresh').exists();assert not set(protocol['train']['jpFonts'])&set(protocol['development']['jpFonts']);assert not (set(protocol['train']['jpFonts'])|set(protocol['development']['jpFonts']))&set(protocol['fresh']['jpFonts'])
 exclusion_values=exclusion();registration={'protocol':protocol,'protocolSha256':sha(source/'protocol.json'),'sources':{p.name:sha(p) for p in source.glob('*.py')},'fontHashes':{n:item['sha256'] for n,item in assets['fonts'].items()},'licenseHashes':{n:item['licenseSha256'] for n,item in assets['fonts'].items()},'preprocessSha256':sha(source.parent/'image_tensor.py'),'initialStateSha256':sha(checkpoint),'activeOnnxSha256':sha('/workspace/scratch/ocr-production-development-20261015-v1/models/ascii-rec.onnx'),'japanesePrimarySha256':sha('/workspace/scratch/ocr-frozen-20261013/v6-medium.onnx'),'exclusionReceiptSha256':sha(ROOT/'exclusion-receipt.json'),'freshOpened':False,'modelCalled':False,'optimizerStarted':False};save(ROOT/'registration-freeze.json',registration);save(RESULTS/'registration-freeze.json',registration);save(RESULTS/'exclusion-receipt.json',json.loads((ROOT/'exclusion-receipt.json').read_text(encoding='utf-8')))
 train=generate(protocol,'train',exclusion_values);used={r[k] for r in train['rows'] for k in ['renderedLiteral','pngSha256','inputSha256']};dev=generate(protocol,'development',exclusion_values|used)
 for m in [train,dev]:
  for kind,count in protocol[m['split']]['positiveCounts'].items():
   rr=[r for r in m['rows'] if r['kind']==kind];assert len(rr)==count;names=protocol[m['split']]['jpFonts'] if kind in ['code','lookalike'] else protocol[m['split']]['latinFonts'];assert len({sum(r['font']==name for r in rr) for name in names})==1
 assert not {r['renderedLiteral'] for r in train['rows']}&{r['renderedLiteral'] for r in dev['rows']};assert not {r['inputSha256'] for r in train['rows'] if r['kind']!='blank'}&{r['inputSha256'] for r in dev['rows'] if r['kind']!='blank'}
 coverage={ch:sum((r['text'] or '').count(ch) for r in train['rows']) for ch in PRINTABLE};assert min(coverage.values())>0
 fixed={**registration,'registrationSha256':sha(ROOT/'registration-freeze.json'),'manifestSha256':{m['split']:sha(ROOT/m['split']/'manifest.json') for m in [train,dev]},'all95PrintableTrainCoverage':coverage,'rows':{m['split']:len(m['rows']) for m in [train,dev]},'allTrainDevLiteralAndNonblankTensorDisjoint':True,'devAmbiguityNeverFiltered':True};save(ROOT/'freeze.json',fixed);save(RESULTS/'freeze.json',fixed);print(json.dumps({'stage':'corpora-frozen','rows':fixed['rows'],'manifestSha256':fixed['manifestSha256'],'freshOpened':False,'modelCalled':False}),flush=True)
if __name__=='__main__':main()
