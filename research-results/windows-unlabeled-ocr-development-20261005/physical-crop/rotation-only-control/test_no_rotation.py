import sys,unittest,types
import numpy as np,cv2
sys.path.insert(0,'/workspace/recovery-research/windows-nonpaddle-ocr-20261004/ndl-source/src')
from parseq import PARSEQ
from no_rotation import build
class Rotation(unittest.TestCase):
 def test_nonrotation_input_byte_equal(self):
  f,_=build(PARSEQ,np,cv2);self_=types.SimpleNamespace(input_width=256,input_height=24);a=np.arange(12*60*3,dtype=np.uint8).reshape(12,60,3)
  np.testing.assert_array_equal(PARSEQ.preprocess(self_,a),f(self_,a))
 def test_rotated_input_matches_exact_unrotated_resize_not_new_padding(self):
  f,_=build(PARSEQ,np,cv2);self_=types.SimpleNamespace(input_width=256,input_height=24);a=np.arange(15*9*3,dtype=np.uint8).reshape(15,9,3);before=a.copy();actual=f(self_,a)
  expected=np.ascontiguousarray(cv2.resize(a,(256,24),interpolation=cv2.INTER_LINEAR)[:,:,::-1]).astype(np.float32);expected/=127.5;expected-=1
  np.testing.assert_array_equal(actual,expected.transpose(2,0,1)[None]);np.testing.assert_array_equal(a,before);self.assertFalse(np.array_equal(actual,PARSEQ.preprocess(self_,a)))
if __name__=='__main__':unittest.main()
