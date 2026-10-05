"""Read complete hosted capture frames; missing/repeated/truncated data fails closed."""
import argparse,base64,hashlib,json,tempfile
from pathlib import Path
import run_once as runner
def decode(lines,source,run):
 inventories={};chunks={};ends={};footers=[]
 for line in lines:
  footer=line.find('{"executionReceipt')
  if footer>=0:footers.append(json.loads(line[footer:]))
  start=line.find('{"closedCapture')
  if start<0:continue
  row=json.loads(line[start:])
  if 'closedCapture' in row:
   name=row['closedCapture'];runner.check(name in ['native.jsonl','native.stderr'] and name not in inventories,'duplicate/unknown inventory');runner.check(row['sourceCommit']==source and row['runID']==str(run) and row['runAttempt']=='1','host source/run/attempt');runner.check(0<=row['bytes']<=(4*1024**2 if name=='native.jsonl' else 256*1024) and 0<=row['parts']<=1400,'capture cap');inventories[name]=row;chunks[name]=[]
  elif 'closedCapturePart' in row:
   name=row['closedCapturePart'];runner.check(name in inventories and name not in ends and row['index']==len(chunks[name]),'part inventory/order');data=base64.b64decode(row['base64'],validate=True);runner.check(0<len(data)<=3072,'part bound');chunks[name].append(data);runner.check(len(chunks[name])<=inventories[name]['parts'],'part count cap')
  elif 'closedCaptureEnd' in row:
   name=row['closedCaptureEnd'];runner.check(name in inventories and name not in ends,'end inventory');ends[name]=row['sha256']
 runner.check(set(inventories)==set(ends)=={'native.jsonl','native.stderr'},'missing complete native captures')
 result={}
 for name,pin in inventories.items():
  data=b''.join(chunks[name]);digest=hashlib.sha256(data).hexdigest();runner.check(len(chunks[name])==pin['parts'] and len(data)==pin['bytes'] and digest==pin['sha256']==ends[name],'closed capture bytes/hash');result[name]=data
 runner.check(len(footers)==1,'missing/duplicate cleanup receipt');footer=footers[0];receipt=footer['executionReceipt']
 runner.check(footer['sourceCommit']==source and footer['ownedScratchRemoved'] is True and footer['downloadsNewModelIdentities']==0 and footer['activation'] is False,'cleanup/source scope')
 runner.check(receipt['exitCode']==0 and receipt['watchdogReason'] is None and not receipt['errors'] and receipt['stdoutBytes']==len(result['native.jsonl']) and receipt['stderrBytes']==len(result['native.stderr']),'native operational receipt')
 result['receipt']=footer
 return result
def main():
 p=argparse.ArgumentParser();p.add_argument('log');p.add_argument('--source',required=True);p.add_argument('--run',required=True);a=p.parse_args();log=Path(a.log);runner.check(log.stat().st_size<=12*1024**2,'bounded full host log');captured=decode(log.read_text().splitlines(),a.source,a.run)
 with tempfile.TemporaryDirectory(prefix='takupoke-roi-readback-') as td:
  out=Path(td)/'native.jsonl';out.write_bytes(captured['native.jsonl']);report=runner.assess(out)
 report['nativeCaptureSHA256']=hashlib.sha256(captured['native.jsonl']).hexdigest();report['stderrSHA256']=hashlib.sha256(captured['native.stderr']).hexdigest();runner.check(captured['receipt']['packetSHA256']==runner.sha(runner.D/'packet-freeze.json'),'host packet receipt pin');report['executionReceipt']=captured['receipt']['executionReceipt'];report['sourceCommit']=a.source;report['runID']=a.run;print(json.dumps(report,indent=2))
if __name__=='__main__':main()
