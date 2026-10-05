import unittest
from native_boxes import parse_words,parse_symbols
class NativeBoxes(unittest.TestCase):
 def test_exactnativewordorigin_confidence_nocharacter_split(self):
  t='level\tp\tb\tp\tl\tw\tleft\ttop\twidth\theight\tconf\ttext\n5\t1\t1\t1\t1\t1\t2\t3\t5\t6\t88.5\t試域01\n';r=parse_words(t,{'box':[100,200,120,220]});self.assertEqual(r[0]['box'],[102,203,107,209]);self.assertEqual(r[0]['text'],'試域01');self.assertEqual(len(r),1)
 def test_nativeoutsideboxpreservednotclipped(self):
  t='header\n5\t1\t1\t1\t1\t1\t-1\t0\t5\t6\t99\t1\n';r=parse_words(t,{'box':[100,200,120,220]});self.assertFalse(r[0]['insideOriginalRow']);self.assertEqual(r[0]['box'][0],99)
 def test_actualBOXbottomorigin_and_nativechars(self):
  r=parse_symbols('試 2 1 8 12 0\n',{'box':[100,200,120,220]});self.assertEqual(r[0]['box'],[102,208,108,219]);self.assertEqual(r[0]['confidence'],None)
 def test_malformednonfinitefailsnotpositivecredit(self):
  with self.assertRaises(ValueError):parse_words('h\n5\t1\t1\t1\t1\t1\t0\t0\t5\t6\tnan\t1\n',{'box':[0,0,20,20]})
if __name__=='__main__':unittest.main()
