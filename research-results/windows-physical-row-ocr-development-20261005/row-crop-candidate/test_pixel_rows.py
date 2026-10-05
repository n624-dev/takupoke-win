import unittest,numpy as np
from pixel_rows import rows
class Rows(unittest.TestCase):
 def plan(self,a):return {'regions':[{'id':0,'box':[0,0,a.shape[1],a.shape[0]]}],'nonRuleInkPixels':int(np.any(a!=255,axis=2).sum())}
 def test_three_rows_not_assumed_and_gray_preserved(self):
  a=np.full((20,20,3),255,np.uint8);a[2:4,3:8]=0;a[12,4]=254;r=rows(a,self.plan(a));self.assertEqual(len(r['rows']),2);self.assertEqual(r['uniquelyOwnedOriginalPixels'],11)
 def test_disconnected_dot_kept_separate(self):
  a=np.full((20,20,3),255,np.uint8);a[2,8]=0;a[6:10,8]=0;r=rows(a,self.plan(a));self.assertEqual(len(r['rows']),2);self.assertEqual(r['uniquelyOwnedOriginalPixels'],5)
 def test_double_ownership_rejected(self):
  a=np.full((20,20,3),255,np.uint8);a[2,8]=0;p=self.plan(a);p['regions']*=2
  with self.assertRaises(ValueError):rows(a,p)
 def test_incomplete_previous_plan_is_not_success(self):
  a=np.full((20,20,3),255,np.uint8);a[2,8]=0;p=self.plan(a);p['nonRuleInkPixels']=2
  with self.assertRaises(ValueError):rows(a,p)
 def test_original_reading_order_frozen_before_outputs(self):
  a=np.full((20,20,3),255,np.uint8);a[2,3]=0;a[12,15]=0;p={'regions':[{'id':1,'box':[10,10,20,20]},{'id':0,'box':[0,0,10,10]}],'nonRuleInkPixels':2};r=rows(a,p);self.assertEqual([x['parentRegionID'] for x in r['rows']],[0,1])
if __name__=='__main__':unittest.main()
