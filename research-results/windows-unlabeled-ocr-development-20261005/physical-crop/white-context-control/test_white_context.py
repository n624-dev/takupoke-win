import unittest,numpy as np
from white_context import extend
class WhiteContext(unittest.TestCase):
 def test_only_horizontal_white_and_identical_core(self):
  a=np.full((20,30,3),255,np.uint8);a[5:15,10:13]=0
  out,b=extend(a,[10,5,13,15]);self.assertEqual(b,[0,5,30,15]);np.testing.assert_array_equal(out[:,10:13],a[5:15,10:13]);self.assertEqual(np.count_nonzero(np.any(out!=255,axis=2)),30)
 def test_gray254_and_other_ink_stop_extension(self):
  a=np.full((20,30,3),255,np.uint8);a[5:15,10:13]=0;a[6,5]=254;a[7,20]=0
  _,b=extend(a,[10,5,13,15]);self.assertEqual(b,[6,5,20,15])
 def test_vertical_ink_outside_y_not_added(self):
  a=np.full((20,30,3),255,np.uint8);a[5:15,10:13]=0;a[4,0]=0
  _,b=extend(a,[10,5,13,15]);self.assertEqual(b,[0,5,30,15])
 def test_unknown_outside_or_float_or_bool_rejected(self):
  a=np.full((20,30,3),255,np.uint8)
  for b in ([-1,0,2,3],[0,0,31,3],[0.,0,2,3],[True,0,2,3]):
   with self.assertRaises(ValueError):extend(a,b)
if __name__=='__main__':unittest.main()
