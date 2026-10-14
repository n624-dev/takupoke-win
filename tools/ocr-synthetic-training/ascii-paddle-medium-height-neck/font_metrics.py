"""Pinned font shape/advance audit only, no rendering or inference."""
import hashlib
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.pens.recordingPen import RecordingPen

from height_data import FONTS,RESULTS,save


def main():
 rows=[]
 for name,path in FONTS.items():
  font=TTFont(path);cmap=font.getBestCmap();glyphs=font.getGlyphSet();widths={chr(i):glyphs[cmap[i]].width for i in range(32,127)};outlines={}
  for char in 'O0Il1:：—–-':
   if ord(char) not in cmap:outlines[char]=None;continue
   pen=RecordingPen();glyphs[cmap[ord(char)]].draw(pen);outlines[char]=(pen.value,glyphs[cmap[ord(char)]].width)
  rows.append({'font':name,'fontSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'asciiAdvanceDistinctValues':len(set(widths.values())),'OAdvance':widths['O'],'zeroAdvance':widths['0'],'IAdvance':widths['I'],'lAdvance':widths['l'],'oneAdvance':widths['1'],'outlineAndAdvanceIdentical':[{'left':a,'right':b,'bothGlyphsPresent':outlines[a] is not None and outlines[b] is not None,'identical':outlines[a]==outlines[b] if outlines[a] is not None and outlines[b] is not None else None,'outlineIdentical':outlines[a][0]==outlines[b][0] if outlines[a] is not None and outlines[b] is not None else None,'advanceIdentical':outlines[a][1]==outlines[b][1] if outlines[a] is not None and outlines[b] is not None else None} for a,b in [('O','0'),('I','l'),('l','1'),(':','：'),('—','-'),('—','–')]]})
 save(RESULTS/'font-metrics-diagnosis.json',{'scope':'Font source outline/advance audit, no rendered pixels or model inference; font metrics do not prove raster or CNN distinctions','fonts':rows,'interpretation':'Training covers monospace/proportional fonts on independent newliterals; actual emitted ratios are separately audited. Fontoutline differences alone do not prove lowresolution raster/model identifiability; no frozen condition changes.'})


if __name__=='__main__':main()
