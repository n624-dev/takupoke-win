"""Pinned font shape/advance audit only, no rendering or inference."""
import hashlib
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.pens.recordingPen import RecordingPen

from adapt_data import FONTS,RESULTS,save


def main():
 rows=[]
 for name,path in FONTS.items():
  font=TTFont(path);cmap=font.getBestCmap();glyphs=font.getGlyphSet();widths={chr(i):glyphs[cmap[i]].width for i in range(32,127)};outlines={}
  for char in 'O0Il1:：—–-':
   pen=RecordingPen();glyphs[cmap[ord(char)]].draw(pen);outlines[char]=(pen.value,glyphs[cmap[ord(char)]].width)
  rows.append({'font':name,'fontSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'asciiAdvanceDistinctValues':len(set(widths.values())),'OAdvance':widths['O'],'zeroAdvance':widths['0'],'IAdvance':widths['I'],'lAdvance':widths['l'],'oneAdvance':widths['1'],'outlineAndAdvanceIdentical':[{'left':a,'right':b,'identical':outlines[a]==outlines[b],'outlineIdentical':outlines[a][0]==outlines[b][0],'advanceIdentical':outlines[a][1]==outlines[b][1]} for a,b in [('O','0'),('I','l'),('l','1'),(':','：'),('—','-'),('—','–')]]})
 save(RESULTS/'font-metrics-diagnosis.json',{'scope':'Font source outline/advance audit, no rendered pixels or model inference; font metrics do not prove raster or CNN distinctions','fonts':rows,'interpretation':'Training covers mainly proportional ASCII advances; BIZUDGothic dev has fixed ASCII advance while KaiseiTokumin differs O vs zero width. A width shortcut/domain-coverage gap is plausible, not proven cause. No devfont/label is moved into training and no frozen condition is changed.'})


if __name__=='__main__':main()
