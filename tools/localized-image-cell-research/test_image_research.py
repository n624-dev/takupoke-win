import json
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch
from comparison import validate_recipe
from contract import decode
from guard import verify_packet
from hosted_support import verify_source
from image_protocol import (BLIND_INSTRUCTION,BLIND_SCHEMA,decode_blind,validate_image,comparison_spec,execute_image_first)
from protocol import digest
from report_protocol import report
import bootstrap_ci
import run_once
ROOT=Path(__file__).resolve().parent

class ImageProtocolTests(unittest.TestCase):
    def setUp(self):
        self.recipe=json.loads((ROOT/'recipe.json').read_text())
        self.task=json.loads((ROOT/'inputs.json').read_text())['tasks'][0]
        self.meta=json.loads((ROOT/'image-input.json').read_text())

    def test_exact_existing_crop_and_scope(self):
        validate_recipe(self.recipe)
        self.assertEqual(validate_image(ROOT,self.meta,self.recipe).stat().st_size,3394)
        for key,value in [('maximumCalls',3),('maximumCropPixels',9025),('visionSupported',False),('productionAdoption',True),('qualifiedGenAIModels',['gemma']),('callDeadlineSeconds',61)]:
            with self.subTest(key=key),self.assertRaises(RuntimeError):validate_recipe({**self.recipe,key:value})

    def test_blind_prompt_has_no_retained_ocr_or_field_role_hint(self):
        for s in self.task['sources']:
            if s['owner']=='cell':self.assertNotIn(s['id'],BLIND_INSTRUCTION);self.assertNotIn(s['text'],BLIND_INSTRUCTION)
        self.assertNotIn('幻学03',json.dumps(BLIND_SCHEMA,ensure_ascii=False))
        self.assertNotIn('requestedField',BLIND_INSTRUCTION)
        self.assertNotIn('37383946385',BLIND_INSTRUCTION)

    def test_blind_raw_unicode_preserved_and_abstention(self):
        self.assertEqual(decode_blind('{"state":"TRANSCRIBED","lines":["甲  Ω"]}')['lines'],['甲  Ω'])
        for state in ('UNKNOWN','NONE'):self.assertEqual(decode_blind(json.dumps({'state':state,'lines':[]}))['state'],state)
        for bad in ('{"state":"TRANSCRIBED","lines":[]}', '{"state":"UNKNOWN","lines":["甲"]}',
                    '{"state":"UNKNOWN","state":"NONE","lines":[]}', '{"state":"UNKNOWN","lines":[],"ids":["p1s70"]}'):
            with self.subTest(raw=bad),self.assertRaises(ValueError):decode_blind(bad)

    def test_ocr_open_order_bound_to_persisted_blind(self):
        events=[]
        def blind():events.append('blind');return {'candidate':{'state':'TRANSCRIBED','lines':['架空Ω']}}
        def frozen(row):events.append('persist-and-freeze');return 'a'*64
        def load():events.append('open-ocr');return self.task
        def compare(spec,sha):events.append('compare');self.assertEqual(sha,'a'*64);return {'candidate':None}
        result=execute_image_first(blind,frozen,load,compare)
        self.assertEqual(events,['blind','persist-and-freeze','open-ocr','compare']);self.assertEqual(result['calls'],2)

    def test_unknown_none_malformed_and_failure_never_open_ocr_or_fallback(self):
        for candidate in (None,{'state':'UNKNOWN','lines':[]},{'state':'NONE','lines':[]}):
            opened=[]
            result=execute_image_first(lambda:{'candidate':candidate},lambda row:'b'*64,
                lambda:opened.append('ocr'),lambda *a:opened.append('compare'))
            self.assertEqual(opened,[]);self.assertEqual(result['calls'],1)
            self.assertEqual(result['comparison'],'UNASSESSED_NO_USABLE_IMAGE')

    def test_freeze_failure_cannot_open_ocr(self):
        opened=[]
        def failure(row):raise OSError('cannot persist')
        with self.assertRaises(OSError):execute_image_first(lambda:{'candidate':{'state':'TRANSCRIBED','lines':['甲']}},failure,
            lambda:opened.append('ocr'),lambda *a:None)
        self.assertEqual(opened,[])

    def test_image_cannot_create_ids_or_empty_proof(self):
        spec=comparison_spec(self.task,{'state':'TRANSCRIBED','lines':['架空別転記Ω']})
        with self.assertRaises(ValueError):decode('{"state":"PRESENT","ids":["IMAGE-ID"]}',self.task,spec['binding'])
        with self.assertRaises(ValueError):decode('{"state":"EMPTY","ids":[]}',self.task,spec['binding'])
        sid=spec['binding']['selectableFocalIds'][0]
        value=decode(json.dumps({'state':'PRESENT','ids':[sid]}),self.task,spec['binding'])
        self.assertNotEqual(value['value'],'架空別転記Ω');self.assertFalse(value['productionAdoption'])
        self.assertEqual(value['assignmentCertificate'],'ABSENT')

    def test_resources_fail_before_any_launch(self):
        launched=[]
        with patch.object(run_once,'resources',side_effect=RuntimeError('no memory')):
            with self.assertRaises(RuntimeError):run_once.guarded_start(self.recipe,lambda:launched.append(1))
        self.assertEqual(launched,[])

    def test_no_root_approval_cannot_verify_or_import_engine(self):
        with self.assertRaises(FileNotFoundError):verify_packet(ROOT,self.recipe)
        self.assertFalse((ROOT/'probe-started.json').exists())

    def test_unassessed_report_reuses_baseline_without_truth(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)
            for name in ('retained-text-baseline.jsonl','inputs.json'):shutil.copyfile(ROOT/name,p/name)
            result=report(p)
            self.assertEqual(result['completedCalls'],0);self.assertEqual(result['missingCallsUNASSESSED'],2)
            self.assertEqual(result['literalAgreementWithRetainedOCR'],'UNASSESSED');self.assertEqual(result['newTextBaselineCalls'],0)
            self.assertEqual(result['correctness'],'UNASSESSED_NO_ORACLE_READ');self.assertFalse(result['productionAdoption'])

    def test_worker_source_has_one_image_and_explicit_cpu_backend(self):
        source=(ROOT/'worker.py').read_text()
        self.assertIn('vision_backend=Backend.CPU(thread_count=2)',source)
        self.assertIn('max_num_images=1',source)
        self.assertIn('os.fsync(s.fileno())',source)
        self.assertIn('IMAGE_UNSUPPORTED_OR_PINNED_VISION_BUDGET_MISMATCH_NO_FALLBACK',source)
        self.assertNotIn('retained-text-baseline',source)
        self.assertNotIn('oracle',source)
        self.assertEqual(source.count('engine.create_conversation('),1)

    def test_exact_source_freeze_and_pin_tamper_rejected(self):
        freeze=verify_source(ROOT,digest(ROOT/'packet-freeze.json'),digest(ROOT/'recipe.json'),bootstrap_ci.WORKFLOW)
        self.assertNotIn('inputs.json',freeze['workerPinPaths'])
        self.assertNotIn('retained-text-baseline.jsonl',freeze['workerPinPaths'])
        with self.assertRaises(RuntimeError):verify_source(ROOT,'0'*64,digest(ROOT/'recipe.json'),bootstrap_ci.WORKFLOW)

    def test_dispatch_cannot_run_locally_or_on_rerun(self):
        with patch.dict('os.environ',{'GITHUB_RUN_ATTEMPT':'1','GITHUB_ACTIONS':'false'},clear=False):
            with self.assertRaises(RuntimeError):bootstrap_ci.dispatch_authority('a'*64,'b'*64)
        with patch.dict('os.environ',{'GITHUB_RUN_ATTEMPT':'2'},clear=False):
            with self.assertRaises(RuntimeError):bootstrap_ci.dispatch_authority('a'*64,'b'*64)

if __name__=='__main__':unittest.main()
