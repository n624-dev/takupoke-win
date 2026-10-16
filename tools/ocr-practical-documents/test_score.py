import unittest
from score import assess

class PracticalScoringTests(unittest.TestCase):
 def control(self):
  gold={'expect':'exact','schoolYear':2029,'requiredSlotSet':[{'className':'2_IT','day':'1','period':1}],
   'timetable':{'lessons':[{'className':'2_IT','weekday':1,'period':1,'names':{'subject':'数学','teacher':'仮想担当','room':'O01'}}]}}
  raw={'id':'fictional','condition':'ink-input-frame','stage':'Engine','outcome':'engine-refused','failure':'humanUnresolved',
   'requiredSlots':1,'recognizerCalls':3,'detectorCalls':1,'milliseconds':10,'processPeakWorkingSetBytes':100,
   'formal':None,'document':{'RequiredSlots':[{'ClassName':'2_IT','Day':'1','Period':1}]}}
  inspected={'fields':[{'slot':{'ClassName':'2_IT','Day':'1','Period':1},'role':role,'Value':value,'minimumConfidence':.95,'lessonIndex':0}
   for role,value in gold['timetable']['lessons'][0]['names'].items()], 'reviewPlanEligible':True,'reviewTargets':[{}]}
  return raw,inspected,gold
 def test_high_confidence_error_remains_wrong_and_contributes_literal_cer(self):
  r,i,g=self.control();i['fields'][2]['Value']='001';x=assess(r,i,g)
  self.assertEqual(x['wrongFields'],1);self.assertEqual(x['highConfidenceWrongFields'],1)
  self.assertEqual(x['rawCharacterEdits'],1);self.assertEqual(x['confidenceFlaggedFields'],0)
  self.assertEqual(x['conditionalCER'],1/9)
 def test_missing_fields_are_reported_separately_from_conditional_cer(self):
  r,i,g=self.control();i['fields'].pop();x=assess(r,i,g)
  self.assertEqual(x['missingFields'],1);self.assertEqual(x['scoredFields'],2)
  self.assertEqual(x['conditionalCER'],0);self.assertFalse(x['allLiteralErrorsCorrectableWithinCurrentBudget'])
 def test_whitespace_and_duplicate_results_do_not_disappear_into_a_success(self):
  r,i,g=self.control();i['fields'][2]['Value']='O 01';i['fields'].append(i['fields'][0]);x=assess(r,i,g)
  self.assertEqual(x['rawCharacterEdits'],1);self.assertEqual(x['duplicateFields'],1)
  self.assertFalse(x['allLiteralErrorsCorrectableWithinCurrentBudget'])
if __name__=='__main__':unittest.main()
