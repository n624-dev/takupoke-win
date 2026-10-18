"""Separate source-proof-only contradictions, never train or select on their labels."""
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image,ImageDraw
from safetensors.torch import load_file
import torch

from data import ROOT,REPO,DICTIONARY,draw,frame,font,save,tensor
from model import load_base,Transfer
from image_tensor import greedy

RESULTS=REPO/'research-results/ocr-synthetic-training-20261013/ascii-paddle-transfer-v1'
DIRECTORY=ROOT/'source-proof-probes'
ALIASES=[('A','А'),('C','С'),('E','Е'),('H','Н'),('O','О'),('P','Р'),('X','Х'),('a','а'),('c','с'),('e','е'),('o','о'),('p','р'),('x','х')]


def prepare():
    if (DIRECTORY/'manifest.json').exists():raise ValueError('Probe pixels already frozen')
    DIRECTORY.mkdir(parents=True,exist_ok=True);rows=[];comparisons=[]
    for name in ['NotoSansJP400','NotoSerifJP400','KiwiMaru-Regular','HinaMincho-Regular']:
        for ascii_value,alias_value in ALIASES:
            left,_=draw(ascii_value,name,190773);right,_=draw(alias_value,name,190773)
            a,source_a=frame(left);b,source_b=frame(right)
            identical=a.size==b.size and np.array_equal(np.asarray(a),np.asarray(b))
            comparisons.append({'font':name,'ascii':ascii_value,'cyrillic':alias_value,'sameFramedPixels':identical})
            if not identical:continue
            pair=f'alias-{len(rows)//2:03d}'
            for suffix,image,source,script in [('ascii',a,source_a,'printableASCII'),('cyrillic',b,source_b,'unsupportedCyrillic')]:
                path=DIRECTORY/f'{pair}-{suffix}.png';image.save(path)
                rows.append({'id':path.stem,'pair':pair,'kind':'scriptAlias','font':name,'file':path.name,'pngSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'framedRgbSha256':hashlib.sha256(np.asarray(image).tobytes()).hexdigest(),'source':source,'sourceInterpretation':script,'sourceProofRequired':True,'incompatibleExpectedHandling':'ASCII literal may be recognized; unsupported script must be refused from source evidence. Pixels alone cannot distinguish.'})
        # A genuine whole-prefix cut: fully render prefix and tail, then remove
        # the complete prefix through blank gap. A fresh tail render has the
        # exact same framed pixels, proved before any recognizer is invoked.
        face=font(name,64);ascent,descent=face.getmetrics();margin=16;baseline=margin+ascent;tail='uQ7';prefix='B';advance=int(np.ceil(face.getlength(prefix)))+24
        fresh=Image.new('RGB',(int(np.ceil(face.getlength(tail)))+margin*2,ascent+descent+margin*2),'white')
        ImageDraw.Draw(fresh).text((margin,baseline),tail,font=face,fill=(5,5,5),anchor='ls')
        original=Image.new('RGB',(fresh.width+advance,fresh.height),'white')
        ink=ImageDraw.Draw(original);ink.text((margin,baseline),prefix,font=face,fill=(5,5,5),anchor='ls');ink.text((margin+advance,baseline),tail,font=face,fill=(5,5,5),anchor='ls')
        damaged=original.crop((advance,0,original.width,original.height));clean,source_a=frame(fresh);cut,source_b=frame(damaged)
        assert clean.size==cut.size and np.array_equal(np.asarray(clean),np.asarray(cut))
        assert np.any(np.asarray(original)[:,:advance]!=255)
        original.save(DIRECTORY/f'{name}-unclipped-source.png')
        pair=f'prefix-{name}'
        for suffix,image,source,interpretation in [('complete',clean,source_a,'completeTail'),('lost-prefix',cut,source_b,'originalPrefixFullyOutsideCrop')]:
            path=DIRECTORY/f'{pair}-{suffix}.png';image.save(path)
            rows.append({'id':path.stem,'pair':pair,'kind':'wholePrefixRemoved','font':name,'file':path.name,'pngSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'framedRgbSha256':hashlib.sha256(np.asarray(image).tobytes()).hexdigest(),'source':source,'sourceInterpretation':interpretation,'sourceProofRequired':True,'incompatibleExpectedHandling':'Complete tail may be recognized; original-prefix loss must be refused from source geometry/ink ownership. Pixels alone cannot distinguish.'})
    manifest={'scope':'Unscored contradiction controls only; not training targets, selection, independent final or production credit','rows':rows,'fontAliasComparisons':comparisons,'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    save(DIRECTORY/'manifest.json',manifest);save(RESULTS/'source-proof-probe-freeze.json',manifest)
    print(json.dumps({'prepared':len(rows),'samePixelPairs':len(rows)//2,'aliasPairs':sum(row['kind']=='scriptAlias' for row in rows)//2,'finalOpened':False}))


def evaluate():
    if not (ROOT/'selection.json').exists():raise ValueError('Wait for completed frozen training selection')
    manifest=json.loads((DIRECTORY/'manifest.json').read_text(encoding='utf-8'))
    assert hashlib.sha256(Path(__file__).read_bytes()).hexdigest()==manifest['sourceSha256']
    torch.set_num_threads(2);torch.set_num_interop_threads(1)
    selection=json.loads((ROOT/'selection.json').read_text(encoding='utf-8'))
    original,official=load_base();models=[('original',original,official,False)]
    base,original_dictionary=load_base();reduced=Transfer(base,original_dictionary);models.append(('reduced-untrained',reduced,DICTIONARY,False))
    for candidate in selection['candidates']:
        base,dictionary=load_base();model=Transfer(base,dictionary)
        assert hashlib.sha256(Path(candidate['path']).read_bytes()).hexdigest()==candidate['sha256']
        model.load_state_dict(load_file(candidate['path']),strict=True);models.append((candidate['name'],model,DICTIONARY,True))
    summaries=[]
    for name,model,dictionary,trained in models:
        model.eval();raw=RESULTS/f'source-proof-{name}-unjudged.jsonl'
        if raw.exists():raise ValueError('Do not overwrite fixed probe outputs')
        records=[]
        with raw.open('x',encoding='utf-8',newline='\n') as stream,torch.inference_mode():
            for row in manifest['rows']:
                path=DIRECTORY/row['file'];assert hashlib.sha256(path.read_bytes()).hexdigest()==row['pngSha256']
                value,valid=tensor(Image.open(path));x=torch.from_numpy(value[None])
                if isinstance(model,Transfer):prob,reject=model(x);reject_probability=float(reject[0])
                else:prob=model(pixel_values=x).last_hidden_state;reject_probability=None
                probability=prob[0].numpy();pieces=greedy(probability,dictionary);text=''.join(piece['Text'] for piece in pieces);minimum=min((piece['Confidence'] for piece in pieces),default=0);supported=all(piece['End']*value.shape[-1]<=valid*probability.shape[0] for piece in pieces)
                accepted=bool(text) and all(32<=ord(c)<=126 for c in text) and minimum>=.8 and supported and (not trained or reject_probability<=.05)
                record={'id':row['id'],'prediction':text,'pieces':pieces,'rejectProbability':reject_probability,'minimumConfidence':minimum,'supported':supported,'accepted':accepted,'sourceProofRequired':True}
                records.append(record);stream.write(json.dumps(record,ensure_ascii=False)+'\n')
        groups={}
        for row,record in zip(manifest['rows'],records):groups.setdefault(row['pair'],[]).append(record)
        for pair in groups.values():assert len(pair)==2 and pair[0]['prediction']==pair[1]['prediction'] and pair[0]['accepted']==pair[1]['accepted']
        summaries.append({'name':name,'samePixelPairs':len(groups),'pairsAcceptedInBothConflictingSources':sum(pair[0]['accepted'] for pair in groups.values()),'pairsRejectedInBothSources':sum(not pair[0]['accepted'] for pair in groups.values()),'rawSha256':hashlib.sha256(raw.read_bytes()).hexdigest(),'limits':'Rejecting both also loses legitimate ASCII. Classification cannot resolve identical pixels with contradictory source/script provenance; require independent source proof.'})
    save(RESULTS/'source-proof-probe-summary.json',{'scope':manifest['scope'],'models':summaries});print(json.dumps(summaries))


if __name__=='__main__':
    import sys
    if len(sys.argv)!=2 or sys.argv[1] not in ['prepare','evaluate']:raise ValueError('prepare or evaluate')
    if sys.argv[1]=='prepare':prepare()
    else:evaluate()
