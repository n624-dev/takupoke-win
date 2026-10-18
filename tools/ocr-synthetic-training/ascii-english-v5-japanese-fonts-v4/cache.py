"""Lossless FP32 frozen-CNN cache; no fabricated image or temporal padding."""
import hashlib,json,resource,shutil,time
from pathlib import Path
import numpy as np,torch
from PIL import Image
from data import ROOT,PARENT,save,tensor,sha

def budget(spec):
 if shutil.disk_usage(PARENT).free<spec['minimumFreeMiB']*1024**2:raise OSError('Free disk reserve crossed')
 if resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024>spec['maximumRssMiB']:raise MemoryError('Own RSS limit crossed')
 if sum(p.stat().st_size for p in PARENT.rglob('*') if p.is_file())>spec['maximumOwnScratchGiB']*1024**3:raise OSError('Own scratch budget crossed')
def build(manifest,teacher,folder,spec):
 folder.mkdir();rows=[];start=time.monotonic();keep_input=manifest['split']!='train'
 with torch.inference_mode():
  for i,row in enumerate(manifest['rows']):
   if time.monotonic()-start>spec['maximumCacheSeconds']:raise TimeoutError('Cache watchdog')
   image_path=ROOT/manifest['split']/row['file'];assert sha(image_path)==row['pngSha256'];x,valid=tensor(Image.open(image_path));assert hashlib.sha256(x.tobytes()).hexdigest()==row['inputSha256'];tx=torch.from_numpy(x[None]);cnn=teacher.model(tx).last_hidden_state;feature=teacher.head.encoder(cnn).last_hidden_state;prob=teacher.head.head(feature).softmax(-1)
   if manifest['split']=='development':assert torch.equal(prob,teacher(pixel_values=tx).last_hidden_state)
   values={'cnn':cnn[0].numpy(),'teacher':feature[0].numpy(),'probability':prob[0].numpy()}
   if keep_input:values['input']=x
   path=folder/(row['id']+'.npz');np.savez_compressed(path,**values)
   with np.load(path,allow_pickle=False) as restored:
    for key,val in values.items():assert restored[key].dtype==np.float32 and restored[key].tobytes()==val.tobytes()
   frames=feature.shape[1];valid_frames=int(np.sum((np.arange(frames)+.5)*x.shape[-1]/frames<valid));assert valid_frames>0
   rows.append({'id':row['id'],'file':path.name,'sourcePng':str(image_path),'inputStored':keep_input,'inputWidth':x.shape[-1],'frames':frames,'validFrames':valid_frames,'validWidth':valid,'inputSha256':hashlib.sha256(x.tobytes()).hexdigest(),'cnnSha256':hashlib.sha256(values['cnn'].tobytes()).hexdigest(),'teacherSha256':hashlib.sha256(values['teacher'].tobytes()).hexdigest(),'probabilitySha256':hashlib.sha256(values['probability'].tobytes()).hexdigest(),'npzSha256':sha(path),'bytes':path.stat().st_size,'shape':{k:list(v.shape) for k,v in values.items()}})
   if i%128==0:budget(spec);print(json.dumps({'stage':'cache','split':manifest['split'],'rows':i+1,'total':len(manifest['rows']),'seconds':time.monotonic()-start,'bytes':sum(r['bytes'] for r in rows),'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024}),flush=True)
 result={'rows':rows,'seconds':time.monotonic()-start,'totalBytes':sum(r['bytes'] for r in rows),'precision':'All exact FP32 CNN/features/full438 probabilities; dev/fresh inputFP32; train input regenerated only for pixel assessment from hashed PNG'};save(folder/'cache.json',result);budget(spec);return result
def fetch(indices,receipt,folder,need_input=False):
 items=[receipt['rows'][i] for i in indices];assert len({r['inputWidth'] for r in items})==len({r['frames'] for r in items})==1;values={k:[] for k in ['cnn','teacher','probability']};inputs=[]
 for item in items:
  with np.load(folder/item['file'],allow_pickle=False) as data:
   for key in values:assert data[key].dtype==np.float32;values[key].append(np.array(data[key],copy=True))
   if need_input:
    if item['inputStored']:x=np.array(data['input'],copy=True)
    else:x,valid=tensor(Image.open(item['sourcePng']));assert valid==item['validWidth']
    assert x.dtype==np.float32 and hashlib.sha256(x.tobytes()).hexdigest()==item['inputSha256'];inputs.append(x)
 mask=torch.arange(items[0]['frames'])[None]<torch.tensor([r['validFrames'] for r in items])[:,None]
 return torch.from_numpy(np.stack(inputs)) if need_input else None,*(torch.from_numpy(np.stack(values[k])) for k in ['cnn','teacher','probability']),mask
