"""Pinned official Tesseract5.5 C API. Warm model; fresh per-row recognition state."""
import ctypes as C
from pathlib import Path
import numpy as np
LIB=Path('/usr/lib/x86_64-linux-gnu/libtesseract.so.5.0.5')
class Tess:
 def __init__(self,datapath):
  self.lib=C.CDLL(str(LIB));self.handle=None;self.lastRecognizeReturned=False;self.lastOutputs={}
  definitions={'TessVersion':([],C.c_char_p),'TessDeleteText':([C.c_void_p],None),'TessBaseAPICreate':([],C.c_void_p),'TessBaseAPIDelete':([C.c_void_p],None),'TessBaseAPIInit2':([C.c_void_p,C.c_char_p,C.c_char_p,C.c_int],C.c_int),'TessBaseAPISetPageSegMode':([C.c_void_p,C.c_int],None),'TessBaseAPISetVariable':([C.c_void_p,C.c_char_p,C.c_char_p],C.c_int),'TessBaseAPIClear':([C.c_void_p],None),'TessBaseAPIClearAdaptiveClassifier':([C.c_void_p],None),'TessBaseAPISetImage':([C.c_void_p,C.POINTER(C.c_ubyte),C.c_int,C.c_int,C.c_int,C.c_int],None),'TessBaseAPIRecognize':([C.c_void_p,C.c_void_p],C.c_int),'TessBaseAPIGetUTF8Text':([C.c_void_p],C.c_void_p)}
  for name in ('TessBaseAPIGetHOCRText','TessBaseAPIGetTsvText','TessBaseAPIGetBoxText'):definitions[name]=([C.c_void_p,C.c_int],C.c_void_p)
  for name,(args,result) in definitions.items():f=getattr(self.lib,name);f.argtypes=args;f.restype=result
  self.version=self.lib.TessVersion().decode('ascii');self.handle=self.lib.TessBaseAPICreate()
  if not self.handle:raise RuntimeError('Tesseract handle creation failed')
  try:
   if self.lib.TessBaseAPIInit2(self.handle,str(datapath).encode(),b'jpn',1)!=0:raise RuntimeError('Pinnedjpn/OEM1 init failed')
   self.lib.TessBaseAPISetPageSegMode(self.handle,7)
   if not self.lib.TessBaseAPISetVariable(self.handle,b'hocr_char_boxes',b'1'):raise RuntimeError('Native character renderer flag unavailable')
  except BaseException:self.close();raise
 def text(self,name):
  f=getattr(self.lib,name);p=f(self.handle) if name=='TessBaseAPIGetUTF8Text' else f(self.handle,0)
  if not p:raise RuntimeError('Native output API returned NULL: '+name)
  try:return C.string_at(p).decode('utf-8',errors='strict')
  finally:self.lib.TessDeleteText(p)
 def read(self,image):
  self.lastRecognizeReturned=False;self.lastOutputs={}
  a=np.ascontiguousarray(image)
  if a.dtype!=np.uint8 or a.ndim!=3 or a.shape[2]!=3 or not a.size:raise ValueError('Original nonemptyRGB required')
  self.lib.TessBaseAPIClear(self.handle);self.lib.TessBaseAPIClearAdaptiveClassifier(self.handle)
  self.lib.TessBaseAPISetImage(self.handle,a.ctypes.data_as(C.POINTER(C.c_ubyte)),a.shape[1],a.shape[0],3,a.strides[0])
  if self.lib.TessBaseAPIRecognize(self.handle,None)!=0:raise RuntimeError('Native Recognize failed')
  self.lastRecognizeReturned=True
  for kind,name in [('text','TessBaseAPIGetUTF8Text'),('hocr','TessBaseAPIGetHOCRText'),('tsv','TessBaseAPIGetTsvText'),('box','TessBaseAPIGetBoxText')]:self.lastOutputs[kind]=self.text(name)
  return self.lastOutputs
 def close(self):
  if self.handle:self.lib.TessBaseAPIDelete(self.handle);self.handle=None
