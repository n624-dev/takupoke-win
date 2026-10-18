import copy
import unittest

from audit_body import audit


def fixture():
    slot={'ClassName':'AI_1','Day':'1','Period':1}
    gold={'requiredSlotSet':[{'className':'AI_1','day':'1','period':1}],
        'timetable':{'lessons':[{'className':'AI_1','weekday':'1','period':1,'names':{'subject':'科甲','teacher':'教甲','room':'室O0'}}]}}
    sources=[];scopes=[]
    for role,text in enumerate(['科甲','教甲','室O0']):
        label=f'l{role}';value=f'v{role}';y=10+role*10
        sources.extend([{'Id':label,'Text':'ラベル','Page':1,'Box':{'X':1,'Y':y,'Width':2,'Height':4},'SourceOrder':role*2,'NativeConfidence':.999},
            {'Id':value,'Text':text,'Page':1,'Box':{'X':5,'Y':y,'Width':6,'Height':4},'SourceOrder':role*2+1,'NativeConfidence':.999}])
        scopes.append({'Role':role,'LessonIndex':0,'Page':1,'LabelSourceIds':[label],'Box':{'X':3,'Y':y,'Width':17,'Height':4}})
    doc={'Cells':[{'SourceIds':[s['Id'] for s in sources],'RoleScopes':scopes,'Slots':[slot]}],'Sources':sources}
    return {'id':'invented','condition':'fixed','outcome':'engine-refused','formal':None,'document':doc},gold


class BodyAuditTests(unittest.TestCase):
    def test_high_confidence_wrong_room_is_not_precision_success(self):
        raw,gold=fixture();raw['document']['Sources'][-1]['Text']='室00'
        r=audit(raw,gold)
        self.assertEqual(1,r['wrongFields']);self.assertFalse(r['allBodyLiteralExact'])
        self.assertEqual(1,r['aggregates'][2]['incorrectAboveDiagnosticFloor'])
        self.assertFalse(r['qualification'])

    def test_unassigned_original_atom_prevents_exact_credit(self):
        raw,gold=fixture();raw['document']['Sources'].append({'Id':'extra','Page':1,'Text':'墨','Box':{'X':22,'Y':10,'Width':1,'Height':1},'SourceOrder':7,'NativeConfidence':1})
        raw['document']['Cells'][0]['SourceIds'].append('extra')
        r=audit(raw,gold)
        self.assertEqual(0,r['assessedFields']);self.assertEqual(3,r['unassessedFields']);self.assertFalse(r['allBodyLiteralExact'])

    def test_actual_wrong_header_is_not_replaced_with_gold(self):
        raw,gold=fixture();raw['document']['Cells'][0]['Slots'][0]['ClassName']='AI_2'
        r=audit(raw,gold)
        self.assertEqual('unassessed',r['quality']);self.assertFalse(r['completeUniqueSlotSet'])

    def test_cancellation_cannot_receive_credit_from_remaining_document(self):
        raw,gold=fixture();raw['outcome']='execution-error-unassessed'
        self.assertEqual('unassessed',audit(raw,gold)['quality'])

    def test_correct_low_score_literal_is_exact_but_has_no_adoption_credit(self):
        raw,gold=fixture();raw['document']['Sources'][-1]['NativeConfidence']=.6
        r=audit(raw,gold)
        self.assertTrue(r['allBodyLiteralExact']);self.assertEqual(1,r['belowFloorFields']);self.assertFalse(r['qualification'])


if __name__=='__main__':unittest.main()
