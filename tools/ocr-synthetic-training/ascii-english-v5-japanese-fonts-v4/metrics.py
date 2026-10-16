"""All-row assessment; alias labels never filter dev/fresh denominator or rank."""
import json,hashlib,sys
from pathlib import Path
import numpy as np,torch
from data import ROOT,RESULTS,save,sha
from cache import fetch
sys.path.insert(0,str(Path(__file__).parents[1]));from image_tensor import greedy
sys.path.insert(0,str(Path(__file__).parents[1]/'ascii-foundation-ctc'));from diagnose import edits
def evaluate(name,adapter,receipt,folder,manifest,dictionary,mode):
 path=RESULTS/(name+'-unjudged.jsonl')
 if path.exists():raise ValueError('Fixed inference cannot repeat')
 adapter.eval();records=[];maximum={'feature':0.,'probability':0.};byteequal=True
 with path.open('x',encoding='utf-8',newline='\n') as output,torch.inference_mode():
  for i,row in enumerate(manifest['rows']):
   x,cnn,teacher,baseline,valid=fetch([i],receipt,folder,need_input=True)
   if mode=='baseline':prob=baseline[0].numpy();reject=None
   else:
    logits,feature,rejected=adapter.from_cnn(cnn,valid) if mode=='cached' else adapter(x,valid);p=logits.softmax(-1);prob=p[0].numpy();reject=float(rejected.sigmoid()[0]) if mode in ['trained','cached'] else None
    if mode=='cached':byteequal=byteequal and torch.equal(p,baseline) and torch.equal(feature,teacher);maximum['feature']=max(maximum['feature'],float((feature-teacher).abs().max()));maximum['probability']=max(maximum['probability'],float((p-baseline).abs().max()))
   pieces=greedy(prob,dictionary);text=''.join(p['Text'] for p in pieces);minimum=min((p['Confidence'] for p in pieces),default=0);supported=all(p['End']*row['inputWidth']<=row['validWidth']*len(prob) for p in pieces);ascii_domain=all(32<=ord(c)<=126 for c in text);accepted=bool(text) and minimum>=.8 and supported and ascii_domain and (mode not in ['trained','cached'] or reject<=.05);r={'id':row['id'],'prediction':text,'pieces':pieces,'minimumConfidence':minimum,'rejectProbability':reject,'accepted':accepted,'supported':supported,'printableAscii':ascii_domain,'inputWidth':row['inputWidth'],'validWidth':row['validWidth'],'timeSteps':len(prob),'source':row['source']};records.append(r);output.write(json.dumps(r,ensure_ascii=False)+'\n')
 lookup={r['id']:r for r in manifest['rows']};positive=[r for r in records if lookup[r['id']]['selectionPositive']];negative=[r for r in records if lookup[r['id']]['reject']];exact=lambda r:r['prediction']==lookup[r['id']]['text'];groups={}
 for kind in sorted({r['kind'] for r in manifest['rows']}):
  g=[r for r in records if lookup[r['id']]['kind']==kind];groups[kind]={'rows':len(g),'exact':sum(not lookup[r['id']]['reject'] and exact(r) for r in g),'correctAccepted':sum(not lookup[r['id']]['reject'] and exact(r) and r['accepted'] for r in g),'incorrectAccepted':sum(not lookup[r['id']]['reject'] and not exact(r) and r['accepted'] for r in g),'accepted':sum(r['accepted'] for r in g),'nonempty':sum(bool(r['prediction']) for r in g)}
 result={'name':name,'positiveRows':len(positive),'exact':sum(exact(r) for r in positive),'correctAccepted':sum(exact(r) and r['accepted'] for r in positive),'incorrectAccepted':sum(not exact(r) and r['accepted'] for r in positive),'negativeRows':len(negative),'negativeAccepted':sum(r['accepted'] for r in negative),'paddingRows':sum(not r['supported'] for r in records),'cer':sum(len(edits(lookup[r['id']]['text'],r['prediction'])) for r in positive)/sum(len(lookup[r['id']]['text']) for r in positive),'groups':groups,'rawSha256':sha(path),'scope':'Fresh synthetic development unless separately labeled fresh; independent ASCII branch; no document qualification'}
 def characterize(rows):
  rows=[r for r in rows if lookup[r['id']]['selectionPositive']];characters=sum(len(lookup[r['id']]['text']) for r in rows);errors=sum(len(edits(lookup[r['id']]['text'],r['prediction'])) for r in rows)
  return {'rows':len(rows),'characters':characters,'editErrors':errors,'cer':errors/characters if characters else None,'exact':sum(exact(r) for r in rows),'correctLowConfidence':sum(exact(r) and r['minimumConfidence']<.8 for r in rows),'wrongHighConfidence':sum(not exact(r) and r['minimumConfidence']>=.8 for r in rows),'accepted':sum(r['accepted'] for r in rows)}
 for kind in groups:
  if any(lookup[r['id']]['selectionPositive'] for r in records if lookup[r['id']]['kind']==kind):groups[kind].update({k:v for k,v in characterize([r for r in records if lookup[r['id']]['kind']==kind]).items() if k!='rows'})
 result['fontMetrics']={f:characterize([r for r in positive if lookup[r['id']]['font']==f]) for f in manifest['fonts'] if any(lookup[r['id']]['font']==f for r in positive)}
 result['codeMetrics']=characterize([r for r in positive if lookup[r['id']]['kind'] in ['code','lookalike']])
 result['monoCodeMetrics']=characterize([r for r in positive if lookup[r['id']]['kind'] in ['code','lookalike'] and lookup[r['id']]['font'] in ['RedHatMono','OverpassMono']])
 result['fontCodeMetrics']={f:characterize([r for r in positive if lookup[r['id']]['font']==f and lookup[r['id']]['kind'] in ['code','lookalike']]) for f in ['RedHatMono','OverpassMono']}
 result['reviewMetrics']=characterize(positive)
 result['allUnsupportedPaddingRefused']=all(not r['accepted'] for r in records if not r['supported'])
 if mode=='cached':result.update({'exactFp32SelfConsistency':byteequal,'maximumDifferences':maximum})
 save(RESULTS/(name+'-summary.json'),result);print(json.dumps({'stage':'evaluation','metrics':result}),flush=True);return result,records

