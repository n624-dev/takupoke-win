import unittest,numpy as np
from token_stats import describe
class Tests(unittest.TestCase):
 def test_stable_extreme_logits_identity_and_eos(self):
  v=np.array([[[1000,1001,-9000],[1005,-9000,1000],[1,2,3]]],np.float32);b=v.tobytes();s=describe(v,'甲乙');self.assertEqual(v.tobytes(),b);self.assertEqual(s['firstActualEOSIndex'],1);self.assertEqual(s['tokens'][0]['actualArgmaxID'],1);self.assertEqual(s['tokens'][0]['actualArgmaxText'],'甲');self.assertAlmostEqual(s['tokens'][0]['derivedArgmaxSoftmax'],1/(1+np.exp(-1)));self.assertFalse(s['tokens'][2]['beforeFirstEOS'])
 def test_tie_uses_original_numpy_argmax(self):
  v=np.zeros((1,2,3),np.float32);s=describe(v,'甲乙');self.assertEqual(s['tokens'][0]['actualArgmaxID'],0);self.assertAlmostEqual(s['tokens'][0]['derivedArgmaxSoftmax'],1/3)
 def test_truncation_and_nonfinite_are_unknown(self):
  v=np.ones((1,5,3),np.float32);self.assertFalse(describe(v,'甲乙',2)['tokenCaptureComplete']);v[0,0,0]=np.nan;s=describe(v,'甲乙');self.assertFalse(s['tokenCaptureComplete']);self.assertEqual(s['tokens'],[])
 def test_wrong_dictionary_contract_no_fabrication(self):
  self.assertFalse(describe(np.ones((1,2,4),np.float32),'甲乙')['expectedClassContract'])
if __name__=='__main__':unittest.main()
