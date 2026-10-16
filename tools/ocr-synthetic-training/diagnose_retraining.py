"""Read-only diagnosis of consumed results; no inference or candidate selection."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.recordingPen import RecordingPen
from fontTools.ttLib import TTFont
import numpy as np
from PIL import ImageFont

from corpus import FONTS


def trimmed_mask(font, character):
    mask=font.getmask(character)
    pixels=np.asarray(mask).reshape(mask.size[1],mask.size[0])
    ys,xs=np.where(pixels>0)
    return pixels[ys.min():ys.max()+1,xs.min():xs.max()+1] if len(xs) else pixels


def font_diagnosis(name):
    path=Path(FONTS[name]);font=TTFont(path);cmap=font.getBestCmap();glyphs=font.getGlyphSet();pairs=[]
    for left,right in [('I','l'),('l','1'),('O','0'),('へ','ヘ'),('タ','夕')]:
        outlines=[];boxes=[]
        for char in [left,right]:
            glyph=glyphs[cmap[ord(char)]];pen=RecordingPen();glyph.draw(pen);outlines.append(pen.value)
            bounds=BoundsPen(glyphs);glyph.draw(bounds);boxes.append(bounds.bounds)
        same_sizes=[]
        for size in range(19,34):
            pil=ImageFont.truetype(str(path),size);a=trimmed_mask(pil,left);b=trimmed_mask(pil,right)
            if a.shape==b.shape and np.array_equal(a,b):same_sizes.append(size)
        pairs.append({'characters':[left,right],'sameUnshiftedVectorOutline':outlines[0]==outlines[1],'vectorBounds':boxes,'advanceAndLeftBearing':[font['hmtx'][cmap[ord(char)]] for char in [left,right]],'sameTightlyTrimmedRasterSizes':same_sizes})
    return {'font':name,'fontSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'pairs':pairs,'limits':'Tight-trim equality is an isolated-glyph observation. It does not prove complete line pixels, advance widths, positions or context are identical. Fonts and results are already consumed; no new accuracy item.'}


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--results',required=True,type=Path);parser.add_argument('--output',required=True,type=Path);args=parser.parse_args()
    if args.output.exists():raise ValueError('Diagnosis exists; preserve it')
    baseline={row['id']:row for row in json.loads((args.results/'final-baseline-records.json').read_text(encoding='utf-8'))['records']}
    rows=json.loads((args.results/'final-selected-records.json').read_text(encoding='utf-8'))['records'];failures=[row for row in rows if not row['exact']]
    substitutions=Counter();row_errors=Counter();different_lengths=[]
    for row in failures:
        if len(row['gold'])!=len(row['prediction']):different_lengths.append(row['id']);continue
        kinds={f'{left}→{right}' for left,right in zip(row['gold'],row['prediction']) if left!=right}
        row_errors.update(kinds)
        substitutions.update(f'{left}→{right}' for left,right in zip(row['gold'],row['prediction']) if left!=right)
    controls=json.loads((args.results/'final-selected-controls.json').read_text(encoding='utf-8'))['groups']
    report={
        'scope':'Consumed final diagnostics only; no new final, inference, training, threshold tuning, checkpoint choice or production qualification',
        'existingResults':{'rows':len(rows),'incorrectRows':len(failures),'incorrectRowsAtUnchangedFloor08':sum(row['minimumConfidence'] is not None and row['minimumConfidence']>=.8 for row in failures),'maximumIncorrectMinimumConfidence':max(row['minimumConfidence'] for row in failures),'regressions':[row['id'] for row in rows if not row['exact'] and baseline[row['id']]['exact']],'failureRowsByFont':dict(Counter(row['font'] for row in failures)),'substitutions':dict(substitutions),'rowsContainingSubstitution':dict(row_errors),'differentLengthRows':different_lengths,'clippedControls':controls['clipped'],'multilineControls':controls['multiline']},
        'fontDiagnostics':[font_diagnosis(name) for name in FONTS],
        'decision':'Do not start another synthetic-training run now. Prioritize full-document acquisition/provenance and independent untried recognizers; reconsider only after a reproducible, visibly distinguishable glyph error remains in correctly acquired complete lines.',
        'reasons':['Additional identical-distribution epochs already plateaued: development149 baseline,162 epoch1,161 epochs2/3.','Most residual errors concern font-specific I/l geometry, with isolated raster ambiguity at small sizes; extra font exposure might improve context use but is not evidence of safe source reconstruction.','Training reduced crop errors but increased high-confidence output on clipped/multiline probes. CTC confidence cannot certify complete original support.','The <=3 corrections requirement applies to actual complete documents and their headers/structure, not crop exact-match averages.'],
        'illustrativeCorrectionBudget':{'bodyFields':400,'minimumExactWithAtMostThreeCorrections':397,'requiredPercent':99.25,'limits':'Arithmetic illustration only. It is not an independence assumption, prediction of real-document error rates, or a crop-based qualification rule.'},
        'possibleNextConditionNotFrozenOrStarted':{'prerequisites':'Parent obtains complete-line original pixels at sufficient source density, verifies cell membership and damage/blank/padding rejection, and compares existing fixed trained model with untried CPU recognizers on new documents.','ifTrainingBecomesJustified':'Use a new protocol with independently generated train/dev/document-final, expanded public font/style/source-density coverage, preserved full line baselines and positions, broad vocabulary retention, and a very low learning rate only for the last backbone stage. Existing final pixels/literals never enter training or count as fresh final.','selection':'Base and previous fixed model remain candidates on new dev; freeze one checkpoint and any confidence/rejection policy before generating a new unused-font whole-document final.','safety':'Include independent fully visible near-lookalikes and clipped/multiline/blank/ownership controls. No manual headers, no gold repair, <=3 actual body corrections per full document.','portability':'Retain standard ONNX, dictionary/BGR48/CTC interval contracts and optional CoreML conversion; no iOS source edits or Apple-device qualification claim.'},
    }
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps({key:value for key,value in report.items() if key!='fontDiagnostics'},ensure_ascii=False))


if __name__=='__main__':main()
