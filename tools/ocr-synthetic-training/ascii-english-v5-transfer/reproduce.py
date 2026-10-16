"""Unjudged pinned512-input + dynamicshape originalONNX fidelity; no oracle."""
import copy,hashlib,json,resource,sys,time
from pathlib import Path
import numpy as np,onnx,torch,yaml
from PIL import Image
from onnx import helper,TensorProto,numpy_helper
from safetensors.torch import save_file
from transformers import PPOCRV5MobileRecForTextRecognition
from native import ASSETS,load,feature_logits
sys.path.insert(0,str(Path(__file__).parents[1]));from image_tensor import tensor,greedy
ROOT=ASSETS.parent;REPO=Path('/workspace/scratch/takupoke-win');RESULTS=REPO/'research-results/ocr-synthetic-training-20261013/ascii-english-v5-transfer-v1'
BASE=Path('/workspace/scratch/paddle-english-v5-pinned');INPUTS=Path('/workspace/scratch/english-v5-ascii-development-20261014');MODEL_SHA='b5f833dfc5d0eb71da397b4efa06ebeee9b431b690a47d6af40d77d8eabc557f';DICT_SHA='ef84489f927fb92b64714c7f110404e5bfb83c4067bd609fe4c41d5f23e25659'
FEATURE='p2o.pd_op.transpose.8.0';LOGITS='p2o.pd_op.add.100.0'
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def save(p,v):Path(p).write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
def session(p):
 import onnxruntime as ort
 opt=ort.SessionOptions();opt.intra_op_num_threads=2;opt.inter_op_num_threads=1;opt.log_severity_level=3
 return ort.InferenceSession(str(p),sess_options=opt,providers=['CPUExecutionProvider'])
def decode(prob,width,valid,dictionary):
 pieces=greedy(prob,dictionary);text=''.join(p['Text'] for p in pieces);supported=all(p['End']*width<=valid*len(prob) for p in pieces);accepted=bool(text) and min((p['Confidence'] for p in pieces),default=0)>=.8 and supported and all(32<=ord(c)<=126 for c in text)
 return {'text':text,'pieces':pieces,'intervals':[(p['Text'],p['Start'],p['End']) for p in pieces],'accepted':accepted,'supported':supported}
def main():
 RESULTS.mkdir(parents=True,exist_ok=True)
 if (RESULTS/'freeze.json').exists():raise ValueError('Fixed reproduction cannot repeat')
 assert sha(BASE/'inference.onnx')==MODEL_SHA and sha(BASE/'dictionary.json')==DICT_SHA
 dictionary=json.loads((BASE/'dictionary.json').read_text(encoding='utf-8'));official=['']+yaml.safe_load((ASSETS/'inference.yml').read_text(encoding='utf-8'))['PostProcess']['character_dict']+[' '];assert dictionary==official and len(dictionary)==438
 original=onnx.load(str(BASE/'inference.onnx'));nodes={out:n for n in original.graph.node for out in n.output};eps=[]
 for n in original.graph.node:
  if n.op_type=='Sqrt':
   constant=nodes[nodes[n.input[0]].input[1]];eps.append(float(numpy_helper.to_array(helper.get_attribute_value(constant.attribute[0]))))
 assert eps==[9.999999747378752e-06]*4+[9.999999974752427e-07]
 observer=copy.deepcopy(original)
 for name,size in [(FEATURE,120),(LOGITS,438)]:observer.graph.output.append(helper.make_tensor_value_info(name,TensorProto.FLOAT,['batch','time',size]))
 onnx.checker.check_model(observer);observer_path=ROOT/'native-feature-logit-observer.onnx';onnx.save(observer,str(observer_path));torch.set_num_threads(2);torch.set_num_interop_threads(1);model=load();save_file(model.state_dict(),str(ROOT/'original-native-english-v5.safetensors'));inputs=json.loads((INPUTS/'inputs.json').read_text(encoding='utf-8'));assert len(inputs)==512
 freeze={'scope':'Consumed parent512 providedPNG/nativeinitialization and10numericshape checks only; no oracle/quality/training/final/activation','originalModelSha256':MODEL_SHA,'dictionarySha256':DICT_SHA,'checkpointSha256':sha(ASSETS/'model.safetensors'),'inputsSha256':sha(INPUTS/'inputs.json'),'sources':{p.name:sha(p) for p in [Path(__file__),Path(__file__).with_name('native.py')]},'headClasses':438,'normalizationAudit':{'hfClassDefaultAllFive':1e-6,'originalOnnxScalars':eps,'nativeTorchSetFromOriginalScalars':True,'weightsUnmodified':True},'registeredTolerances':{'feature':1e-4,'logit':1e-3,'probability':1e-4},'parameters':sum(p.numel() for p in model.parameters()),'fp32Inputs':'Whole original ink+2 supplied by parent, BGR48 centerbilinear/uint8round/norm0rightpad width320..960; parentvalidWidth/sourceRect unchanged'};save(RESULTS/'freeze.json',freeze)
 native=session(BASE/'inference.onnx');observe=session(observer_path);maxima={'observerProbability':0.,'feature':0.,'logit':0.,'probability':0.};count={'textIntervals':0,'floor08AsciiValidDecisions':0,'sourceRectPreserved':0};started=time.monotonic()
 def compare(x,valid):
  reference=native.run(None,{'x':x})[0];prob,feat,logit=observe.run(None,{'x':x});actual_feat,actual_logits=feature_logits(model,torch.from_numpy(x));actual_prob=actual_logits.softmax(-1).numpy();diff={'observerProbability':float(np.max(np.abs(reference-prob))),'feature':float(np.max(np.abs(feat-actual_feat.numpy()))),'logit':float(np.max(np.abs(logit-actual_logits.numpy()))),'probability':float(np.max(np.abs(reference-actual_prob)))};a=[decode(p,x.shape[-1],valid,dictionary) for p in reference];b=[decode(p,x.shape[-1],valid,dictionary) for p in actual_prob];same=all(l['intervals']==r['intervals'] for l,r in zip(a,b));decisions=all(l['accepted']==r['accepted'] and l['supported']==r['supported'] for l,r in zip(a,b));return a,b,diff,same,decisions
 with (RESULTS/'unjudged.jsonl').open('x',encoding='utf-8',newline='\n') as out,torch.inference_mode():
  for i,row in enumerate(inputs):
   p=INPUTS/row['file'];assert sha(p)==row['sha256'];x,valid=tensor(Image.open(p));a,b,diff,same,decisions=compare(x[None],valid)
   for key in maxima:maxima[key]=max(maxima[key],diff[key])
   count['textIntervals']+=same;count['floor08AsciiValidDecisions']+=decisions;count['sourceRectPreserved']+=1;out.write(json.dumps({'id':row['id'],'sourceRect':row['sourceRect'],'inputWidth':x.shape[-1],'validWidth':valid,'inputTensorSha256':hashlib.sha256(x.tobytes()).hexdigest(),'native':a[0],'torch':b[0],'differences':diff,'sameIntervals':same,'sameDecisions':decisions},ensure_ascii=False)+'\n')
   if i%128==0:print(json.dumps({'completed':i+1,'planned':512}),flush=True)
  dynamic=[]
  for batch in [1,2]:
   for width in [320,481,500,640,960]:
    x=np.zeros((batch,3,48,width),dtype=np.float32);x[:,:,:,width//4:width//2]=1;a,b,diff,same,decisions=compare(x,width);dynamic.append({'batch':batch,'width':width,'differences':diff,'sameIntervals':same,'sameDecisions':decisions})
 numeric=lambda d:all(d[k]<=v for k,v in freeze['registeredTolerances'].items());gate=count['textIntervals']==count['floor08AsciiValidDecisions']==512 and numeric(maxima) and all(d['sameIntervals'] and d['sameDecisions'] and numeric(d['differences']) for d in dynamic);result={'scope':freeze['scope'],'counts':count,'maximumDifferences':maxima,'dynamic':dynamic,'fidelityGatePassed':gate,'seconds':time.monotonic()-started,'peakRssMiB':resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,'rawSha256':sha(RESULTS/'unjudged.jsonl'),'oracleOpened':False,'trainingStarted':False,'finalOpened':False};save(RESULTS/'fidelity.json',result);print(json.dumps(result))
if __name__=='__main__':main()
