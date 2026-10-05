import io,json,tempfile,sys,unittest
from pathlib import Path
from unittest.mock import patch
from types import SimpleNamespace
import run_once as r
class RunnerControls(unittest.TestCase):
 def test_prewrite_exact_boundary(self):
  f=io.BytesIO();self.assertEqual(r.write_capped(f,b'1234',0,4),4);self.assertEqual(f.getvalue(),b'1234')
 def test_prewrite_overflow_never_written(self):
  f=io.BytesIO(b'12');f.seek(2)
  with self.assertRaises(ValueError):r.write_capped(f,b'345',2,4)
  self.assertEqual(f.getvalue(),b'12')
 def test_incomplete_inventory_refused(self):
  with tempfile.TemporaryDirectory() as td:
   p=Path(td)/'x';p.write_text(json.dumps({'type':'result','actualDetectorCalls':1})+'\n')
   with self.assertRaises(ValueError):r.assess(p)
 def test_exactly_once_no_implicit_registration_native(self):
  with patch.dict(r.os.environ,{'GITHUB_EVENT_NAME':'push'},clear=True):
   with self.assertRaises(ValueError):r.event_gate('not-a-packet')
 def test_model_free_fast_exit_drains_closed_pipes(self):
  with tempfile.TemporaryDirectory() as td,patch.object(r,'memory_bytes',return_value=100),patch.object(r.shutil,'disk_usage',return_value=SimpleNamespace(free=3*1024**3)):
   root=Path(td);receipt=r.capture([sys.executable,'-c','print("fictional-control")'],root/'out',root/'err',dict(r.os.environ))
   self.assertEqual(receipt['exitCode'],0);self.assertFalse(receipt['errors']);self.assertIsNone(receipt['watchdogReason']);self.assertEqual((root/'out').read_bytes().rstrip(b'\r\n'),b'fictional-control')
 def test_pipe_cap_stops_and_does_not_append_overflow(self):
  with tempfile.TemporaryDirectory() as td,patch.object(r,'memory_bytes',return_value=100),patch.object(r.shutil,'disk_usage',return_value=SimpleNamespace(free=3*1024**3)):
   root=Path(td);receipt=r.capture([sys.executable,'-c','import sys;sys.stderr.buffer.write(b"x"*300000);sys.stderr.flush()'],root/'out',root/'err',dict(r.os.environ))
   self.assertTrue(receipt['errors']);self.assertLessEqual((root/'err').stat().st_size,256*1024)

class ClosedCaptureControls(unittest.TestCase):
 def rows(self):
  import hashlib,base64
  b=b'fictional';h=hashlib.sha256(b).hexdigest();empty=hashlib.sha256(b'').hexdigest()
  return [json.dumps(v) for v in [{'closedCapture':'native.jsonl','bytes':len(b),'sha256':h,'parts':1,'sourceCommit':'a'*40,'runID':'7','runAttempt':'1'},{'closedCapturePart':'native.jsonl','index':0,'base64':base64.b64encode(b).decode()},{'closedCaptureEnd':'native.jsonl','sha256':h},{'closedCapture':'native.stderr','bytes':0,'sha256':empty,'parts':0,'sourceCommit':'a'*40,'runID':'7','runAttempt':'1'},{'closedCaptureEnd':'native.stderr','sha256':empty},{'executionReceipt':{'exitCode':0,'watchdogReason':None,'errors':[],'stdoutBytes':len(b),'stderrBytes':0},'sourceCommit':'a'*40,'ownedScratchRemoved':True,'downloadsNewModelIdentities':0,'activation':False}]]
 def test_complete_parts_exact(self):
  from read_closed_capture import decode
  self.assertEqual(decode(self.rows(),'a'*40,'7')['native.jsonl'],b'fictional')
 def test_missing_end_refuses(self):
  from read_closed_capture import decode
  with self.assertRaises(ValueError):decode(self.rows()[:-1],'a'*40,'7')
 def test_duplicate_part_refuses(self):
  from read_closed_capture import decode
  rows=self.rows()
  with self.assertRaises(ValueError):decode(rows[:2]+rows[1:],'a'*40,'7')
 def test_wrong_source_refuses(self):
  from read_closed_capture import decode
  with self.assertRaises(ValueError):decode(self.rows(),'b'*40,'7')
 def test_payload_hash_drift_refuses(self):
  from read_closed_capture import decode
  rows=self.rows();p=json.loads(rows[1]);p['base64']='YnJva2Vu';rows[1]=json.dumps(p)
  with self.assertRaises(ValueError):decode(rows,'a'*40,'7')

if __name__=='__main__':unittest.main()
