"""Synthetic and frozen-source controls; no model/runtime import or replay."""
from copy import deepcopy
import importlib.util
import json
from pathlib import Path
import unittest
from unittest import mock
import request_contract as r

ROOT = Path(__file__).resolve().parent


def task(identifier='synthetic'):
    sources = [{'id': str(i), 'text': text, 'box': [10, 10 + i * 10, 10, 8],
                'sourceLine': i, 'sourceOrder': 100 - i, 'nativeConfidence': .99,
                'fromOcr': True, 'chunkID': i, 'ctcStart': 0, 'ctcEnd': 1,
                'owner': 'cell'} for i, text in enumerate(('例Ω', '例Ψ', '例Δ'))]
    expose = [{k: deepcopy(s[k]) for k in ('id', 'text', 'box', 'sourceLine', 'sourceOrder', 'owner')} for s in sources]
    return {'id': identifier, 'sources': sources,
            'promptInput': {'cellID': identifier, 'cellBox': [0, 0, 50, 50],
                            'coordinateScope': 'estimated CTC', 'sources': expose,
                            'surroundingClosedBoxes': []},
            'cropPath': 'unused.png', 'cropSHA256': '0' * 64,
            'originalRGBSHA256': '1' * 64, 'cropBox': [1, 1, 49, 49],
            'originalBGRASHA256': '2' * 64, 'acquisitionRows': [],
            'originalChunkIDs': [0, 1, 2], 'sourceProof': 'NO_ROLE_PROOF',
            'developmentConsumed': True}


def edit(t, index, key, value):
    t['sources'][index][key] = value
    if key in t['promptInput']['sources'][index]:
        t['promptInput']['sources'][index][key] = deepcopy(value)


def candidate(comparison=None):
    value = {'state': 'CANDIDATE', 'lessons': [{'subject': ['0'], 'teacher': ['1'], 'room': ['2']}]}
    if comparison is not None:
        value['comparison'] = comparison
    return value


def decode(value, t=None, stage='text'):
    return r.decode(json.dumps(value, ensure_ascii=False), t or task(), stage)


class RequestControls(unittest.TestCase):
    def test_no_model_or_oracle_import(self):
        text = (ROOT / 'request_contract.py').read_text() + (ROOT / 'prepare.py').read_text()
        self.assertNotIn('import litert', text)
        self.assertNotIn('oracle-evaluation-only.json', text)
        self.assertNotIn('fixture-manifest.json', text)
        self.assertNotIn('paint.json', text)

    def test_focal_and_context_explicit_formats(self):
        value = r.request(task(), r.caller_intent(task()))
        self.assertIn('LTRB', value['coordinateFormats']['focalCell'])
        self.assertIn('XYWH', value['coordinateFormats']['sourceBox'])
        self.assertEqual(['0', '1', '2'], value['sourceInventoryOrder'])
        self.assertEqual('ABSENT', value['productionFieldExtractionAssignments'])

    def test_uniform_caller_query_not_text_routing(self):
        t = task(); edit(t, 0, 'text', '月曜日')
        self.assertEqual(r.PURPOSE, r.caller_intent(t)['purpose'])
        self.assertFalse(r.caller_intent(t)['productionFieldExtraction'])

    def test_reject_production_mode_or_false_authority(self):
        for key, value in [('purpose', 'fieldExtraction'), ('authority', True), ('productionFieldExtraction', True)]:
            with self.subTest(key=key):
                intent = r.caller_intent(task()); intent[key] = value
                with self.assertRaises(r.Refusal): r.request(task(), intent)

    def test_exact_source_hash_bound(self):
        t = task(); intent = r.caller_intent(t); edit(t, 0, 'text', '改変')
        with self.assertRaises(r.Refusal): r.request(t, intent)

    def test_payload_rewrite_rejected(self):
        t = task(); t['promptInput']['sources'][0]['text'] = '改変'
        with self.assertRaises(r.Refusal): r.inspect_source(t)

    def test_gold_keys_rejected(self):
        t = task(); t['expectedTuples'] = []
        with self.assertRaises(r.Refusal): r.caller_intent(t)
        t = task(); edit(t, 0, 'expectedRole', 'subject')
        with self.assertRaises(r.Refusal): r.inspect_source(t)

    def test_partial_focal_overlap_rejected(self):
        t = task(); edit(t, 0, 'box', [49, 10, 10, 8])
        with self.assertRaisesRegex(r.Refusal, 'FOCAL_SOURCE_OUTSIDE'): r.inspect_source(t)

    def test_context_owner_requires_unique_closed_box(self):
        t = task(); edit(t, 0, 'owner', 'context')
        with self.assertRaises(r.Refusal): r.inspect_source(t)
        t['promptInput']['surroundingClosedBoxes'] = [[0, 0, 50, 50]]
        with self.assertRaises(r.Refusal): r.inspect_source(t)

    def test_duplicate_context_owner_rejected(self):
        t = task(); t['promptInput']['surroundingClosedBoxes'] = [[60, 0, 90, 50]] * 2
        with self.assertRaises(r.Refusal): r.inspect_source(t)

    def test_confidence_refusal_before_model(self):
        t = task(); edit(t, 0, 'nativeConfidence', .799999)
        self.assert_refusal_no_calls(t, 'NATIVE_CONFIDENCE_REFUSAL')

    def test_unknown_or_nonfinite_confidence_refused(self):
        for confidence in [None, True, float('nan'), 1.01]:
            t = task(); edit(t, 0, 'nativeConfidence', confidence)
            with self.assertRaises((r.Refusal, ValueError)): r.inspect_source(t)

    def test_ocr_middle_dot_refuses_without_normalization(self):
        t = task(); edit(t, 0, 'text', '演1\u00b7題7')
        self.assert_refusal_no_calls(t, 'ORIGINAL_OCR_MIDDLE_DOT_AMBIGUOUS')
        self.assertEqual('演1\u00b7題7', t['sources'][0]['text'])

    def test_vector_middle_dot_not_rewritten(self):
        t = task(); edit(t, 0, 'text', '演1\u00b7題7'); edit(t, 0, 'fromOcr', False)
        value = r.request(t, r.caller_intent(t))
        self.assertEqual('演1\u00b7題7', value['focalCell']['sources'][0]['text'])
        self.assertFalse(value['productionAdoption'])

    def test_no_focal_sources_not_empty_proof(self):
        t = task(); t['sources'] = []; t['promptInput']['sources'] = []
        self.assert_refusal_no_calls(t, 'NO_FOCAL_SOURCE_NOT_EMPTY_PROOF')

    def test_nonfinite_bool_zero_boxes_reject(self):
        for box in [[10, 10, 0, 1], [True, 10, 1, 1], [10, 10, float('inf'), 1]]:
            t = task(); edit(t, 0, 'box', box)
            with self.assertRaises((r.Refusal, ValueError)): r.inspect_source(t)

    def test_aggregate_budget_precedes_serialization(self):
        t = task(); t['sources'][0]['text'] = 'x' * 20000
        with mock.patch.object(r, 'canonical', side_effect=AssertionError('serialization occurred')):
            with self.assertRaisesRegex(r.Refusal, 'AGGREGATE_TEXT'): r.caller_intent(t)

    def test_nested_foreign_shape_budget(self):
        t = task(); value = []
        for _ in range(14): value = [value]
        t['acquisitionRows'] = value
        with self.assertRaises(r.Refusal): r.caller_intent(t)

    def test_prepare_plan_shape_refusal_never_serializes(self):
        for key, value in [('text', 'x' * 20000), ('nested', [[[[]]]]*129)]:
            t = task()
            if key == 'text': t['sources'][0]['text'] = value
            else: t['acquisitionRows'] = value
            with mock.patch.object(r, 'canonical', side_effect=AssertionError('refused content serialized')):
                plan = r.prepare_plan([t], {})
                self.assertEqual('REFUSED_BEFORE_MODEL', plan['records'][0]['eligibility'])
                self.assertIsNone(plan['records'][0]['sourceSnapshotSHA256'])
                result = r.execute_plan(plan, [t], lambda _: self.fail('work after aggregate refusal'))
                self.assertEqual(0, result['calls'])

    def test_selection_identifier_bound_precedes_hash(self):
        for identifier in ['x' * 129, None, True]:
            t = task(); t['id'] = identifier
            with mock.patch.object(r.hashlib, 'sha256', side_effect=AssertionError('selection hashed unbounded ID')):
                with self.assertRaises(r.Refusal): r.prepare_plan([t], {})

    def test_body_schema_has_no_header_escape_or_gold_count(self):
        value = r.schema(task(), 'text')
        self.assertEqual({'state', 'lessons'}, set(value['properties']))
        self.assertEqual(4, value['properties']['lessons']['maxItems'])
        fields = value['properties']['lessons']['items']['properties']
        self.assertEqual(set(r.FIELDS), set(fields))
        for field in r.FIELDS: self.assertEqual(['0', '1', '2'], fields[field]['items']['enum'])

    def test_compare_enum_forbids_not_applicable(self):
        self.assertEqual(['AGREE', 'DISAGREE', 'UNKNOWN'], r.schema(task(), 'compare')['properties']['comparison']['enum'])
        with self.assertRaises(r.Refusal): decode(candidate('NOT_APPLICABLE'), stage='compare')
        self.assertEqual('AGREE', decode(candidate('AGREE'), stage='compare')['comparison'])

    def test_text_does_not_accept_comparison_extra(self):
        with self.assertRaises(r.Refusal): decode(candidate('AGREE'))

    def test_unknown_refused_no_empty_proof(self):
        for state in ['UNKNOWN', 'REFUSED']:
            self.assertEqual(state, decode({'state': state, 'lessons': []})['state'])
            with self.assertRaises(r.Refusal): decode({'state': state, 'lessons': candidate()['lessons']})

    def test_empty_candidate_or_teacher_rejected(self):
        with self.assertRaises(r.Refusal): decode({'state': 'CANDIDATE', 'lessons': []})
        value = candidate(); value['lessons'][0]['teacher'] = []
        with self.assertRaises(r.Refusal): decode(value)

    def test_context_same_enum_but_not_body_evidence(self):
        t = task(); extra = deepcopy(t['sources'][0]); extra.update(id='ctx', text='月', box=[60, 10, 10, 8], owner='context'); t['sources'].append(extra)
        t['promptInput']['sources'].append({k: deepcopy(extra[k]) for k in ('id','text','box','sourceLine','sourceOrder','owner')}); t['promptInput']['surroundingClosedBoxes'] = [[60, 0, 90, 50]]
        for field in r.FIELDS: self.assertIn('ctx', r.schema(t, 'text')['properties']['lessons']['items']['properties'][field]['items']['enum'])
        value = candidate(); value['lessons'][0]['subject'] = ['ctx']
        with self.assertRaises(r.Refusal): decode(value, t)

    def test_complete_focal_partition_required(self):
        t = task(); extra = deepcopy(t['sources'][0]); extra.update(id='extra', box=[25, 10, 5, 8]);t['sources'].append(extra)
        t['promptInput']['sources'].append({k: deepcopy(extra[k]) for k in ('id','text','box','sourceLine','sourceOrder','owner')})
        with self.assertRaisesRegex(r.Refusal, 'INCOMPLETE_FOCAL_PARTITION'): decode(candidate(), t)

    def test_duplicate_and_source_array_order(self):
        value = candidate(); value['lessons'][0]['subject'] = ['0','1']; value['lessons'][0]['teacher'] = ['1']
        with self.assertRaises(r.Refusal): decode(value)
        t = task(); edit(t, 0, 'text', 'A'); self.assertEqual(candidate(), decode(candidate(), t))
        # SourceOrder metadata deliberately descends: original array wins.
        self.assertEqual([100, 99, 98], [s['sourceOrder'] for s in t['sources']])

    def test_role_swap_is_not_production_certificate(self):
        value = candidate(); value['lessons'][0]['teacher'],value['lessons'][0]['room'] = ['2'],['1']
        self.assertEqual(value, decode(value))
        # Semantic roles remain UNPROVEN; membership/partition is insufficient.
        self.assertEqual('ABSENT', r.request(task(), r.caller_intent(task()))['productionFieldExtractionAssignments'])

    def test_response_duplicate_properties_nonfinite_and_size(self):
        for raw in ['{"state":"UNKNOWN","state":"CANDIDATE","lessons":[]}', '{"state":"UNKNOWN","lessons":NaN}', ' ' * (r.MAX_RESPONSE_BYTES + 1)]:
            with self.assertRaises((r.Refusal, ValueError)): r.decode(raw, task(), 'text')

    def test_compare_quote_is_diagnostic_not_evidence(self):
        quote = {'state':'CANDIDATE','lines':['改字']}
        text = r.prompt(task(), r.caller_intent(task()), 'compare', quote)
        self.assertIn('NOT_ID_EVIDENCE', text); self.assertIn('NOT_APPLICABLE is forbidden', text)
        self.assertEqual(['0','1','2'], r.schema(task(), 'compare')['properties']['lessons']['items']['properties']['subject']['items']['enum'])

    def test_label_blind_selection_and_no_backfill(self):
        tasks = [task(str(i)) for i in range(10)]; selected = r.select_tasks(tasks)
        ids = [t['id'] for t in selected];edit(selected[0],0,'nativeConfidence',.7)
        plan = r.prepare_plan(tasks,{t['id']:{'state':'UNKNOWN','lines':[]} for t in tasks})
        self.assertEqual(ids,[x['taskID'] for x in plan['records']]);self.assertEqual(12,plan['sourceEligibleMaximumCalls'])

    def test_execution_no_retries_after_operational_failure(self):
        tasks=[task()];plan=r.prepare_plan(tasks,{'synthetic':{'state':'UNKNOWN','lines':[]}});calls=[]
        def fail(spec): calls.append(spec);raise RuntimeError('native failure')
        with self.assertRaises(RuntimeError): r.execute_plan(plan,tasks,fail)
        self.assertEqual(1,len(calls))

    def test_execution_rechecks_source_before_later_call(self):
        tasks=[task()];plan=r.prepare_plan(tasks,{'synthetic':{'state':'UNKNOWN','lines':[]}});calls=[]
        def mutate(spec): calls.append(spec);edit(tasks[0],0,'text','改変');return {}
        with self.assertRaises(r.Refusal): r.execute_plan(plan,tasks,mutate)
        self.assertEqual(1,len(calls))

    def test_execution_cap_and_fresh_conversation_requirement(self):
        tasks=[task(str(i)) for i in range(4)];plan=r.prepare_plan(tasks,{t['id']:{'state':'UNKNOWN','lines':[]} for t in tasks});seen=[]
        result=r.execute_plan(plan,tasks,lambda spec:seen.append(spec) or {'raw':'fake'})
        self.assertEqual(16,result['calls']);self.assertTrue(all(s['freshConversationRequired'] for s in seen))
        bad=deepcopy(plan);bad['maximumCalls']=17
        with self.assertRaises(r.Refusal):r.execute_plan(bad,tasks,lambda _:self.fail('must not call'))

    def test_actual_frozen_selection_and_refusals(self):
        tasks=json.loads((ROOT.parent/'local-ai-three-arm-research/inputs.json').read_text())['tasks']
        plan=r.prepare_plan(tasks,{t['id']:{'state':'UNKNOWN','lines':[]} for t in tasks})
        self.assertEqual(['ta8e5321bf2145f08','t000466d2ba598e3e','tb92df51f3d0d45cc','td0c28684ec33746d'],[t['taskID'] for t in plan['records']])
        self.assertEqual(['NATIVE_CONFIDENCE_REFUSAL','ORIGINAL_OCR_MIDDLE_DOT_AMBIGUOUS'],[t['refusal'] for t in plan['records'] if 'refusal' in t])
        self.assertEqual(8,plan['sourceEligibleMaximumCalls'])

    def test_shared_field_extraction_bytes_preserved(self):
        import hashlib
        prompt=ROOT.parent/'recovery-prompt-contracts/prompts/field-extraction-v4.txt'
        self.assertEqual('c24039ae4317a433a14f01697d77813424a3a1c20a70327189964b2fc60bb188',hashlib.sha256(prompt.read_bytes()).hexdigest())

    def test_reference_prompt_schema_byte_parity(self):
        from reference_contract import specification, pinned_reference
        protocol, instruction = pinned_reference()
        t = task(); diagnostic = {'state':'UNKNOWN','lines':[]}
        for stage in r.STAGES:
            quote = diagnostic if stage == 'compare' else None
            spec = specification(t, 'REFERENCE_454', stage, quote)
            expected = instruction + '\n原文データ:\n' + json.dumps(t['promptInput'],ensure_ascii=False,separators=(',',':'))
            if quote is not None:
                expected += '\n別の盲検画像転記候補(原文IDの証拠ではない):\n' + json.dumps(quote,ensure_ascii=False)
                expected += '\n元の原文IDだけを選び、転記との比較をAGREE/DISAGREE/UNKNOWNで示してください。転記で原文を書き換えないでください。'
            self.assertEqual(expected,spec['prompt']);self.assertEqual(protocol.id_schema(t),spec['schema'])

    def test_reference_pin_failure_precedes_dependent_call(self):
        from reference_contract import pinned_reference
        with mock.patch.object(Path, 'read_bytes', return_value=b'changed'):
            with self.assertRaisesRegex(r.Refusal,'REFERENCE_SOURCE_PIN'):pinned_reference()

    def assert_refusal_no_calls(self,t,reason):
        plan=r.prepare_plan([t],{t['id']:{'state':'UNKNOWN','lines':[]}})
        self.assertEqual(reason,plan['records'][0]['refusal'])
        value=r.execute_plan(plan,[t],lambda _:self.fail('dependent model work after source refusal'))
        self.assertEqual(0,value['calls']);self.assertEqual(4,len(value['rows']))


if __name__ == '__main__': unittest.main()
