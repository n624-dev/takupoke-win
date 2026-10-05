"""OFFLINE exact upstream post-detector replay from immutable detections. Native0."""
from pathlib import Path
import sys,json,hashlib,xml.etree.ElementTree as ET,yaml
P=Path(__file__).resolve().parent;R=Path('/workspace/recovery-research/windows-nonpaddle-ocr-20261004');sys.path.insert(0,str(R/'ndl-source/src'));import ocr
classes=list(yaml.safe_load((R/'ndl-source/src/config/ndl.yaml').read_text())['names'].values());plan=json.loads((P/'regions-before-native.json').read_text());regions={r['id']:r for r in plan['regions']};rows=[json.loads(l) for l in (P/'native-raw.log').read_text().splitlines() if l.startswith('{')];out=[]
for r in rows:
 if r.get('type')!='detections':continue
 region=regions[r['regionID']];x,y,ex,ey=region['box'];result=[{0:[]},{i:[] for i in range(17)}]
 for d in r['detections']:
  a,b,c,e=d['box']
  if d['class_index']==0:result[0][0].append([a,b,c,e])
  result[1][d['class_index']].append([a,b,c,e,d['confidence'],d['pred_char_count']])
 xml=ocr.convert_to_xml_string3(ex-x,ey-y,'region-'+str(region['id'])+'.png',classes,result);root=ET.fromstring('<OCRDATASET>'+xml.replace('&','&amp;')+'</OCRDATASET>');ocr.eval_xml(root,logger=None)
 lines=[]
 for node in root.findall('.//LINE'):
  lines.append(dict(node.attrib))
 out.append({'regionID':region['id'],'actualDetectionCount':len(r['detections']),'parsedLineCount':len(lines),'parsedLinesBeforeTrim':lines,'upstreamFallbackWouldApply':not lines and bool(r['detections']),'originalRegion':region['box'],'originalDetections':r['detections']})
v={'nativeCalls':0,'source':'exact original convert_to_xml_string3 + eval_xml from pinned official source','rawSHA256':hashlib.sha256((P/'native-raw.log').read_bytes()).hexdigest(),'sourceParserSHA256':hashlib.sha256((R/'ndl-source/src/ndl_parser.py').read_bytes()).hexdigest(),'regions':out};(P/'offline-layout-trace.json').write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n');print('90region offline trace',len(out))
