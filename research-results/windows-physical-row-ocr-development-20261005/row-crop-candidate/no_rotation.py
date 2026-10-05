"""Exact original PARSeq preprocess with ONLY its CCW branch omitted."""
import inspect,textwrap,hashlib
BLOCK='        if h>w*0.8:\n            img=cv2.rotate(img,cv2.ROTATE_90_COUNTERCLOCKWISE)\n'
def build(cls,np,cv2):
 original=inspect.getsource(cls.preprocess)
 if original.count(BLOCK)!=1:raise ValueError('Pinned single upstream rotation branch not found')
 changed=original.replace(BLOCK,'');namespace={'np':np,'cv2':cv2};exec(compile(textwrap.dedent(changed),'source-extracted-parseq-no-ccw-only','exec'),namespace)
 return namespace['preprocess'],{'sourceMethodSHA256':hashlib.sha256(original.encode()).hexdigest(),'generatedMethodSHA256':hashlib.sha256(changed.encode()).hexdigest(),'soleRemovedBytes':BLOCK,'unchanged':'linear fixed resize/BGR/float normalization/tensor shape/argmax/EOS dictionary'}
