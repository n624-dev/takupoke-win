import unittest,copy
from coordinates import translate
class Cases(unittest.TestCase):
 def sample(self):return {'id':7,'text':'unchanged','boundingBox':[[1,2],[1,9],[8,2],[8,9]]},{'id':3,'box':[100,200,110,215]}
 def test_original_array_text_and_point_order_unchanged(self):
  u,r=self.sample();old=copy.deepcopy(u);v,ok=translate(u,r,99);self.assertTrue(ok);self.assertEqual(u,old);self.assertEqual(v['boundingBox'],[[101,202],[101,209],[108,202],[108,209]]);self.assertEqual(v['originalNativeID'],7);self.assertEqual(v['id'],99)
 def test_outside_export_never_clipped(self):
  u,r=self.sample();u['boundingBox']=[[-1,2],[-1,9],[8,2],[8,9]];v,ok=translate(u,r,0);self.assertFalse(ok);self.assertEqual(v['boundingBox'][0],[99,202])
 def test_nonfinite_reject(self):
  u,r=self.sample();u['boundingBox'][0][0]=float('nan')
  with self.assertRaises(ValueError):translate(u,r,0)
 def test_boolean_is_not_coordinate(self):
  u,r=self.sample();u['boundingBox'][0][0]=True
  with self.assertRaises(ValueError):translate(u,r,0)
 def test_crossed_shape_is_not_repaired(self):
  u,r=self.sample();u['boundingBox'][2][1]=3
  with self.assertRaises(ValueError):translate(u,r,0)
if __name__=='__main__':unittest.main()
