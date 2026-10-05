import math

def translate(unit,region,adapter_id):
 b=unit['boundingBox'];x,y,ex,ey=region['box']
 if len(b)!=4 or any(len(p)!=2 for p in b):raise ValueError('Native rectangle shape')
 if any(isinstance(v,bool) or not isinstance(v,(int,float)) or not math.isfinite(v) for p in b for v in p):raise ValueError('Native coordinate finite numeric contract')
 xs=[p[0] for p in b];ys=[p[1] for p in b]
 if len(set(xs))!=2 or len(set(ys))!=2 or len(set(map(tuple,b)))!=4:raise ValueError('Native rectangle not axis aligned/positive')
 local_inside=min(xs)>=0 and min(ys)>=0 and max(xs)<=ex-x and max(ys)<=ey-y
 v=dict(unit);v.update(originalNativeID=unit['id'],regionID=region['id'],originalNativeLocalBoundingBox=b,boundingBox=[[px+x,py+y] for px,py in b],id=adapter_id,IDScope='new adapter namespace; original nativeID/raw unchanged',parentRegionBox=region['box'],geometryScope='pixel-derived recognition ROI with EXACT integer translation; no clipping or character subdivision')
 return v,local_inside
