"""Bounded diagnostic of unchanged native logits/argmax; never decides adoption."""
import hashlib,numpy as np
def describe(v,chars,cap=128):
 finite=bool(np.isfinite(v).all());tokens=[];eos=None
 contract=v.ndim==3 and v.shape[0]==1 and v.shape[2]==len(chars)+1
 if contract and finite:
  ids=np.argmax(v[0],axis=1);found=np.where(ids==0)[0];eos=int(found[0]) if len(found) else None
  for i,row in enumerate(v[0][:cap]):
   selected=int(ids[i]);x=row.astype(np.float64);maximum=float(x.max());weights=np.exp(x-maximum);denom=float(weights.sum());second=float(np.partition(x,-2)[-2]);top=float(x[selected])
   tokens.append({'timeIndex':i,'actualArgmaxID':selected,'actualArgmaxText':None if selected==0 else chars[selected-1],'argmaxLogit':top,'runnerUpLogit':second,'logitMargin':top-second,'derivedArgmaxSoftmax':float(weights[selected]/denom),'beforeFirstEOS':eos is None or i<eos})
 return {'tensorShape':list(v.shape),'dtype':str(v.dtype),'tensorSHA256':hashlib.sha256(np.ascontiguousarray(v).tobytes()).hexdigest(),'allFinite':finite,'expectedClassContract':contract,'firstActualEOSIndex':eos,'tokens':tokens,'tokenCaptureComplete':bool(contract and finite and v.shape[1]<=cap),'probabilityCalibration':'unproved; diagnostic only; no guard/selection changes','extraModelCalls':0}
