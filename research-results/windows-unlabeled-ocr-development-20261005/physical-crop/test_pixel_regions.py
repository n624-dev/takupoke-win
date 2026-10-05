import unittest,numpy as np
from pixel_regions import plan
class Cases(unittest.TestCase):
 def canvas(self):
  rgb=np.full((30,60,3),255,np.uint8);mask=np.zeros((30,60),bool);mask[8:10,10:31]=True;mask[24:26,10:31]=True;mask[8:26,10:12]=True;mask[8:26,29:31]=True;rgb[mask]=0
  b={'Left':10.5,'Top':8.5,'Right':29.5,'Bottom':24.5,'closedRails':True};return rgb,mask,[b]
 def test_actual_rail_strips_only_preserve_all_nonrule(self):
  rgb,mask,boxes=self.canvas();rgb[15:19,15:19]=0;v=plan(rgb,mask,boxes);self.assertEqual(v['nonRuleInkPixels'],16);self.assertEqual(v['regions'][0]['box'],[12,10,29,24]);self.assertEqual(v['uniquelyOwnedNonRuleInkPixels'],16)
 def test_gray254_is_source_ink(self):
  rgb,mask,boxes=self.canvas();rgb[16,16]=254;v=plan(rgb,mask,boxes);self.assertEqual(v['nonRuleInkPixels'],1)
 def test_unknown_outer_border_residual_never_ignored(self):
  rgb,mask,boxes=self.canvas();rgb[26,10:31]=0
  with self.assertRaises(ValueError):plan(rgb,mask,boxes)
 def test_pixel_only_metadata_every_group_retained(self):
  rgb=np.full((60,100,3),255,np.uint8);mask=np.zeros((60,100),bool);rgb[10:15,10:20]=0;rgb[10:15,50:60]=0;v=plan(rgb,mask,[]);self.assertEqual(len(v['regions']),2);self.assertEqual(v['nonRuleInkPixels'],100)
 def test_overlapping_cell_ownership_terminal(self):
  rgb,mask,boxes=self.canvas();rgb[15:19,15:19]=0
  with self.assertRaises(ValueError):plan(rgb,mask,boxes+boxes)
 def test_open_cell_no_implicit_grid(self):
  rgb,mask,boxes=self.canvas();boxes[0]['closedRails']=False
  with self.assertRaises(ValueError):plan(rgb,mask,boxes)
 def test_internal_rule_never_erased(self):
  rgb,mask,boxes=self.canvas();mask[16,12:29]=True;rgb[16,12:29]=0
  with self.assertRaises(ValueError):plan(rgb,mask,boxes)
 def test_region_capacity_before_partial_crops(self):
  rgb,mask,boxes=self.canvas()
  with self.assertRaises(ValueError):plan(rgb,mask,boxes*129)
if __name__=='__main__':unittest.main()
