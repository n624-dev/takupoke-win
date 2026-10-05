"""Exact native word/symbol boxes; integer origin conversion only, never clipping."""
import math

def parse_words(tsv,row):
 x,y,ex,ey=row['box'];words=[]
 for line in tsv.splitlines()[1:]:
  v=line.split('\t',11)
  if len(v)!=12:raise ValueError('NativeTSV columns')
  if v[0]!='5':continue
  a,b,w,h=map(int,v[6:10]);confidence=float(v[10]);text=v[11]
  if not math.isfinite(confidence) or not(-1<=confidence<=100) or w<=0 or h<=0:raise ValueError('Nativeword geometry/confidence invalid')
  words.append({'text':text,'localBox':[a,b,a+w,b+h],'box':[x+a,y+b,x+a+w,y+b+h],'nativeWordConfidence':confidence,'confidenceScope':'Tesseractnativeword0..100, not calibratedPaddle/NDLprobability','insideOriginalRow':0<=a<a+w<=ex-x and 0<=b<b+h<=ey-y})
 return words

def parse_symbols(box,row):
 x,y,ex,ey=row['box'];symbols=[];height=ey-y
 for line in box.splitlines():
  v=line.rsplit(' ',5)
  if len(v)!=6:raise ValueError('NativeBOX columns')
  char=v[0];a,b,c,d,page=map(int,v[1:]);top,bottom=height-d,height-b
  if a>=c or top>=bottom or page!=0:raise ValueError('NativeBOX geometry/page invalid')
  symbols.append({'text':char,'nativeBottomLeftBox':[a,b,c,d],'localTopLeftBox':[a,top,c,bottom],'box':[x+a,y+top,x+c,y+bottom],'confidence':None,'confidenceScope':'BOXAPIhasno confidence; hocr cinfo raw supplies native x_conf whereavailable','insideOriginalRow':0<=a<c<=ex-x and 0<=top<bottom<=height})
 return symbols
