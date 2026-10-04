import unittest,numpy as np
from pixel_trim import OriginalPixelProof
class ProofTests(unittest.TestCase):
 def image(self):return np.full((20,30,3),255,np.uint8)
 def test_only_white_margin_and_all_ink(self):
  a=self.image();a[5:9,8:13]=0;p=OriginalPixelProof(a,np.zeros((20,30),bool));crop,r=p.trim([2,2,20,15],0);self.assertEqual(r['trimmedBox'],[8,5,13,9]);self.assertEqual(crop.shape,(4,5,3));self.assertEqual(r['inkPixels'],r['trimmedInkPixels'])
 def test_antialiased_gray_is_retained(self):
  a=self.image();a[3,4]=254;a[8,12]=0;p=OriginalPixelProof(a,np.zeros((20,30),bool));_,r=p.trim([1,1,20,15],0);self.assertEqual(r['trimmedBox'],[4,3,13,9]);self.assertEqual(r['inkPixels'],2)
 def test_outside_rejected_not_clipped(self):
  p=OriginalPixelProof(self.image(),np.zeros((20,30),bool))
  with self.assertRaises(ValueError):p.trim([-1,1,9,8],0)
  with self.assertRaises(ValueError):p.trim([1,1,31,8],0)
 def test_empty_no_false_empty_proof(self):
  p=OriginalPixelProof(self.image(),np.zeros((20,30),bool));_,r=p.trim([2,2,10,10],0);self.assertFalse(r['sourceAdmissible']);self.assertIsNone(r['trimmedBox'])
 def test_rule_ink_kept_but_source_rejected(self):
  a=self.image();mask=np.zeros((20,30),bool);mask[4,2:20]=True;a[mask]=0;a[8,8]=0;p=OriginalPixelProof(a,mask);_,r=p.trim([1,2,22,12],0);self.assertTrue(r['physicalRuleInkPresent']);self.assertFalse(r['sourceAdmissible']);self.assertEqual(r['inkPixels'],19)
 def test_shared_ink_parent_child_conflicts(self):
  a=self.image();a[5:7,5:7]=0;p=OriginalPixelProof(a,np.zeros((20,30),bool));p.trim([2,2,12,12],0);p.trim([4,4,9,9],1);o=p.ownership();self.assertEqual(len(o['sharedInkConflicts']),1);self.assertTrue(all(r['conflictingOwner'] for r in o['regions']))
 def test_white_overlap_not_glyph_conflict(self):
  a=self.image();a[4:6,4:6]=0;a[4:6,16:18]=0;p=OriginalPixelProof(a,np.zeros((20,30),bool));p.trim([1,1,13,10],0);p.trim([10,1,22,10],1);self.assertEqual(p.ownership()['sharedInkConflicts'],[])
 def test_two_physical_cells_not_claimed_one(self):
  a=self.image();mask=np.zeros((20,30),bool);mask[2,2:28]=mask[17,2:28]=True;mask[2:18,2]=mask[2:18,27]=mask[2:18,15]=True;a[mask]=0;a[8,8]=a[8,22]=0;p=OriginalPixelProof(a,mask);_,r=p.trim([3,3,27,17],0);self.assertFalse(r['singleBoundedPhysicalCell']);self.assertFalse(r['sourceAdmissible'])
if __name__=='__main__':unittest.main()
