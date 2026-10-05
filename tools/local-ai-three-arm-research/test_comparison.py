"""Matched source-only condition controls; all model/backend/process objects fake."""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock,patch
import comparison as c
import guard
import protocol
import score_outputs

ROOT=Path(__file__).resolve().parent

class Comparison(unittest.TestCase):
    def setUp(self):
        self.base=json.loads((ROOT/'recipe.json').read_text());self.config=json.loads((ROOT/'comparison-config.json').read_text());self.inputs=json.loads((ROOT/'inputs.json').read_text());self.oracle=json.loads((ROOT/'oracle-evaluation-only.json').read_text())
    def recipe(self,name):return c.select_recipe(self.base,name,self.config)

    def test_original_prompt_is_byte_exact_every_task_and_blind_candidate(self):
        r=self.recipe('gemma-original')
        for t in self.inputs['tasks']:
            for blind in (None,{'state':'UNKNOWN','lines':[]},{'state':'CANDIDATE','lines':['架空確認']}):
                self.assertEqual(c.prompt(t,r,blind).encode(),protocol.text_prompt(t,blind).encode())

    def test_gemma_conditions_only_prompt_label_diff_all_other_model_controls_match(self):
        a=self.recipe('gemma-original');b=self.recipe('gemma-clarified')
        self.assertEqual({k:v for k,v in a.items() if k!='comparisonCondition'},{k:v for k,v in b.items() if k!='comparisonCondition'})
        self.assertEqual(c.validate_recipe(a),c.validate_recipe(b))
        self.assertEqual(a['contextTokens'],8192);self.assertEqual(a['sampler']['maximumOutputTokens'],768)

    def test_demo_ids_absent_actual_inputs_and_same_actual_payload_suffix(self):
        r=self.recipe('gemma-clarified');text=c.CLARIFIED_INSTRUCTION
        self.assertIn('見出しだけ',text);self.assertIn('lessonsは[]',text);self.assertIn('原文ではありません',text);self.assertIn('例のIDは出力禁止',text)
        allids={s['id'] for t in self.inputs['tasks'] for s in t['sources']}
        self.assertFalse(allids.intersection({'EX_H1','EX_H2','EX_H3','EX_B1','EX_B2','EX_B3'}))
        for t in self.inputs['tasks']:
            suffix='\n原文データ:\n'+json.dumps(t['promptInput'],ensure_ascii=False,separators=(',',':'))
            self.assertTrue(c.prompt(t,r).endswith(suffix));self.assertEqual(c.prompt(t,r),text+suffix)

    def test_schema_and_original_protocol_are_unchanged_not_oracle_filtered(self):
        for t in self.inputs['tasks']:
            schema=protocol.id_schema(t);ids=[s['id'] for s in t['sources']] or ['__NO_SOURCE_ID_ALLOWED__']
            for group in (schema['properties']['headers']['properties'],schema['properties']['lessons']['items']['properties']):
                for field in group.values():self.assertEqual(field['items']['enum'],ids)
        self.assertEqual(protocol.digest(ROOT/'protocol.py'),'e27ec1322faf2486e3d990ac088f01dfe84155f19285030702c062b36e21b408')

    def test_header_candidate_no_lessons_valid_body_missing_field_refuses(self):
        t={'sources':[{'id':'z1','owner':'cell'},{'id':'z2','owner':'cell'},{'id':'z3','owner':'cell'}]}
        envelope={'state':'CANDIDATE','headers':{k:[] for k in protocol.HEADERS},'lessons':[],'comparison':'NOT_APPLICABLE'};envelope['headers']['day']=['z1','z2','z3']
        self.assertEqual(protocol.decode_candidate(json.dumps(envelope),t)['lessons'],[])
        envelope['headers']['day']=[];envelope['lessons']=[{'subject':['z1'],'teacher':['z2'],'room':['z3']}]
        protocol.decode_candidate(json.dumps(envelope),t)
        envelope['lessons'][0]['teacher']=[]
        with self.assertRaisesRegex(ValueError,'missing field'):protocol.decode_candidate(json.dumps(envelope),t)

    def test_empty_candidate_remains_semantic_rejection_not_json_syntax_failure(self):
        t=self.inputs['tasks'][0];x={'state':'CANDIDATE','headers':{k:[] for k in protocol.HEADERS},'lessons':[{'subject':[],'teacher':[],'room':[]}],'comparison':'NOT_APPLICABLE'}
        self.assertEqual(protocol.strict_json(json.dumps(x)),x)
        with self.assertRaisesRegex(ValueError,'empty candidate'):protocol.decode_candidate(json.dumps(x),t)
        for state in ['NONE','UNKNOWN']:
            x['state']=state;x['lessons']=[];protocol.decode_candidate(json.dumps(x),t)

    def test_qwen_text_scope_twelve_and_vision_options_absent(self):
        r=self.recipe('qwen-text-clarified');self.assertEqual(c.validate_recipe(r),['arm2_text']);self.assertEqual(r['maximumCalls'],12)
        cpu=Mock(return_value='fakeCPU2');options=c.engine_options(r,cpu,ROOT)
        cpu.assert_called_once_with(thread_count=2);self.assertEqual(options['max_num_images'],0);self.assertNotIn('vision_backend',options)
        self.assertEqual(r['modelBytes'],344671744);self.assertEqual(r['modelSHA256'],c.QWEN_SHA)

    def test_gemma_vision_options_same_and_one_image(self):
        cpu=Mock(return_value='fakeCPU2');a=c.engine_options(self.recipe('gemma-original'),cpu,ROOT);b=c.engine_options(self.recipe('gemma-clarified'),cpu,ROOT)
        self.assertEqual(a,b);self.assertEqual(a['max_num_images'],1);self.assertEqual(a['vision_backend'],'fakeCPU2')

    def test_qwen_wrong_stage_count_vision_model_and_unknown_condition_refuse(self):
        for change in ({'maximumCalls':36},{'maximumCalls':True},{'executionStages':list(c.ALL_STAGES)},{'visionSupported':True},{'visionSupported':0},{'modelSHA256':c.GEMMA_SHA},{'comparisonCondition':'unknown'}):
            r=self.recipe('qwen-text-clarified');r.update(change)
            with self.assertRaises(RuntimeError):c.validate_recipe(r)
        with self.assertRaises(RuntimeError):c.select_recipe(self.base,'unknown',self.config)

    def test_qwen_image_stages_unsupported_not_missing_expected_calls(self):
        report=score_outputs.assess(self.inputs,self.oracle,[],['arm2_text'],'qwen-text-clarified')
        self.assertEqual(report['expectedCalls'],12);self.assertEqual(report['missingCallsUNASSESSED'],12)
        for t in report['tasks']:
            self.assertEqual(t['arm3_blind']['state'],'UNSUPPORTED');self.assertEqual(t['arm3_compare']['state'],'UNSUPPORTED');self.assertEqual(t['arm2_text']['state'],'UNASSESSED')
        self.assertEqual(report['summary']['arm3_blind']['states']['UNSUPPORTED'],12)

    def test_other_condition_outputs_never_pooled(self):
        row={'comparisonCondition':'gemma-original','taskID':self.inputs['tasks'][0]['id'],'stage':'arm2_text','raw':'{}','disposition':'CANDIDATE'}
        result=score_outputs.assess(self.inputs,self.oracle,[row],c.ALL_STAGES,'gemma-clarified')
        self.assertEqual(result['returnedCalls'],0);self.assertEqual(result['missingCallsUNASSESSED'],36);self.assertIn('no pooling',result['invalidCompletionRecords'][0]['error'])

    def test_selected_packet_changes_only_recipe_pin_and_binds_source_and_condition(self):
        freeze=json.loads((ROOT/'packet-freeze.json').read_text());a=self.recipe('gemma-clarified');blob=(json.dumps(a,indent=2)+'\n').encode();out=c.derive_packet(freeze,a,'a'*64,'b'*64,blob)
        self.assertEqual(out['comparisonCondition'],'gemma-clarified');self.assertEqual(out['derivedFromSourcePacketSHA256'],'a'*64);self.assertEqual(out['comparisonConfigurationSHA256'],'b'*64)
        for before,after in zip(freeze['pins'],out['pins']):
            if before['path']=='recipe.json':self.assertEqual(after['sha256'],hashlib.sha256(blob).hexdigest())
            else:self.assertEqual(before,after)

    def test_derived_approval_wrong_condition_hash_count_or_missing_source_refused(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);r=self.recipe('qwen-text-clarified');blob=(json.dumps(r,indent=2)+'\n').encode();(p/'recipe.json').write_bytes(blob)
            f=c.derive_packet({'pins':[{'path':'recipe.json','bytes':0,'sha256':''}],'workerPinPaths':['recipe.json']},r,'a'*64,'b'*64,blob)
            (p/'packet-freeze.json').write_text(json.dumps(f));approval={'action':c.SCOPE_ACTION,'sourceFreezeSHA256':protocol.digest(p/'packet-freeze.json'),'recipeSHA256':protocol.digest(p/'recipe.json'),'comparisonCondition':'qwen-text-clarified','originalSourcePacketSHA256':'a'*64,'comparisonConfigurationSHA256':'b'*64,'plannedMaximumCalls':12,'newRecognizerCalls':0,'productionAdoption':False,'fullDocumentAssessment':False}
            (p/'root-inference-approval.json').write_text(json.dumps(approval));guard.verify_packet(p,r)
            for change in ({'comparisonCondition':'gemma-original'},{'plannedMaximumCalls':36},{'plannedMaximumCalls':True},{'originalSourcePacketSHA256':None},{'comparisonConfigurationSHA256':'c'*64}):
                (p/'root-inference-approval.json').write_text(json.dumps({**approval,**change}))
                with self.assertRaises(RuntimeError):guard.verify_packet(p,r)

    def test_workflow_three_independent_jobs_failfast_false_exact_push_no_inline_engine(self):
        text=(ROOT.parents[1]/'.github/workflows/local-ai-three-arm-research.yml').read_text()
        self.assertIn('fail-fast: false',text);self.assertIn('condition: [gemma-original, gemma-clarified, qwen-text-clarified]',text)
        self.assertIn('runs-on: ubuntu-24.04',text);self.assertIn('COMPARISON_CONDITION: ${{ matrix.condition }}',text)
        self.assertIn('--condition "$COMPARISON_CONDITION"',text);self.assertNotIn('Engine(',text);self.assertNotIn('for condition',text)

if __name__=='__main__':unittest.main(verbosity=2)
