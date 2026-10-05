"""Horizontal original white context only, independent of recognized text/roles."""
import numpy as np

def extend(region,box):
 if region.dtype!=np.uint8 or region.ndim!=3 or region.shape[2]!=3:raise ValueError('RGB original required')
 if len(box)!=4 or any(isinstance(v,bool) or not isinstance(v,int) for v in box):raise ValueError('Integer original crop required')
 x,y,ex,ey=box;h,w=region.shape[:2]
 if not(0<=x<ex<=w and 0<=y<ey<=h):raise ValueError('Crop outside measured original region')
 left,right=x,ex
 while left>0 and np.all(region[y:ey,left-1]==255):left-=1
 while right<w and np.all(region[y:ey,right]==255):right+=1
 original=region[y:ey,x:ex];expanded=region[y:ey,left:right]
 if np.count_nonzero(np.any(expanded!=255,axis=2))!=np.count_nonzero(np.any(original!=255,axis=2)):raise AssertionError('Original nonwhite count changed')
 return expanded,[left,y,right,ey]
