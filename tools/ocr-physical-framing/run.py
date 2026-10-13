#!/usr/bin/env python3
"""Consume original fictional RLE pixels, without sending source annotations to OCR."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import urllib.request

ROOT=Path(__file__).resolve().parent
PACKET_SHA='7a87ca9355ee2577fb3fab05cfc9dc86ab5d870a8e49f2119b54ef70d260a54c'


def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()


def asset(path,root):return {'file':str(path.relative_to(root)),'bytes':path.stat().st_size,'sha256':sha(path)}


def write(path,obj):path.write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')


def main():
    p=argparse.ArgumentParser();p.add_argument('--ios-reference',type=Path,required=True)
    p.add_argument('--recognizer',type=Path,required=True);p.add_argument('--dictionary',type=Path,required=True)
    p.add_argument('--dotnet',required=True);p.add_argument('--output',type=Path,required=True)
    p.add_argument('--prepare-only',action='store_true');args=p.parse_args()
    source=args.ios_reference/'tests/fixtures/recovery-generic-ruled-original-two.json'
    if sha(source)!=PACKET_SHA:raise ValueError('Consumed original packet changed')
    args.output.mkdir(parents=True,exist_ok=False)
    for path,name,digest in [(args.recognizer,'rec.onnx','da72dc72ca4dc220df0dfde68c1dedc31c58d3e76a25871122e5056227d50092'),
                             (args.dictionary,'alphabet.json','d858428fbe0ada92b439a5f277b51bc1b318fd985d80946a3ce859d7f2446a92')]:
        if sha(path)!=digest:raise ValueError('Pinned recognizer/alphabet changed')
        shutil.copyfile(path,args.output/name)
    detector=args.output/'det.onnx'
    with urllib.request.urlopen('https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_det_onnx/resolve/e6f4fa85f00e168c862bc462aebca69eef9b3d3d/inference.onnx',timeout=60) as r:
        detector.write_bytes(r.read(4826519))
    if detector.stat().st_size!=4826518 or sha(detector)!='a431985659dc921974177a95adcfbb90fd9e51989a5e04d70d0b75f597b6e61d':raise ValueError('Pinned detector changed')
    data=json.loads(source.read_text(encoding='utf-8'));documents=[]
    for ordinal,c in enumerate(data['cases']):
        pages=[]
        for page in c['pages']:
            runs=base64.b64decode(page['rgbaRunsBase64'],validate=True);limit=page['width']*page['height']*4;rgba=bytearray()
            if len(runs)%8 or limit>4096*4096*4:raise ValueError('Invalid original runs')
            for count,r,g,b,a in struct.iter_unpack('<IBBBB',runs):
                if count<1 or count*4>limit-len(rgba):raise ValueError('Run length exceeds original')
                rgba.extend(bytes((r,g,b,a))*count)
            if len(rgba)!=limit or hashlib.sha256(rgba).hexdigest()!=page['rgbaSHA256']:raise ValueError('Original pixels changed')
            bgra=bytearray(rgba);bgra[0::4]=rgba[2::4];bgra[2::4]=rgba[0::4]
            path=args.output/f'd{ordinal}-p{page["page"]}.bgra';path.write_bytes(bgra)
            pages.append({'page':page['page'],'width':page['width'],'height':page['height'],'pixels':asset(path,args.output)})
        documents.append({'id':f'd{ordinal}','originalPDFSHA256':c['originalPDFSHA256'],'pages':pages})
    if len(documents)!=2 or any(len(d['pages'])!=5 for d in documents):raise ValueError('Original cohort changed')
    # Only pixel/artifact identity goes into the native manifest. textsAndInk,
    # glyph/role boxes, literal answers and oracle objects never enter it.
    manifest={'recipe':'physical-input-framing-consumed-originals-v1','sourcePacketSHA':PACKET_SHA,
              'detector':asset(detector,args.output),'recognizer':asset(args.output/'rec.onnx',args.output),
              'dictionary':asset(args.output/'alphabet.json',args.output),'documents':documents,
              'sources':{name:sha(ROOT/name) for name in ('run.py','Program.cs','PhysicalCropFrame.cs')},
              'inputFrame':'Connected ownership first; physical cell intersection removes only white/proven-rule pixels; no output clipping',
              'plannedConditions':['production','physical-input-frame'],'scope':'Consumed full original pixels; zero new independent documents'}
    write(args.output/'inputs.json',manifest)
    if args.prepare_only:
        print(json.dumps({'preparedDocuments':len(documents),'nativeCalls':0,'scope':'Preparation only, no quality assessment'}))
        return
    env={**os.environ,'DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_NOLOGO':'1','ORT_TELEMETRY_DISABLED':'1'}
    subprocess.run([args.dotnet,'build',str(ROOT/'Probe.csproj'),'-c','Release','--verbosity','quiet'],check=True,env=env,timeout=240)
    env={k:v for k,v in env.items() if not any(x in k.upper() for x in ('TOKEN','SECRET','PASSWORD','CREDENTIAL'))}
    result=subprocess.run([args.dotnet,str(ROOT/'bin/Release/net10.0/Takupoke.OcrComparison.dll'),
                           str(args.output/'inputs.json'),str(args.output/'observations.jsonl')],
                          capture_output=True,text=True,encoding='utf-8',timeout=960,env=env)
    (args.output/'worker-stdout.txt').write_text(result.stdout,encoding='utf-8',newline='\n')
    (args.output/'worker-stderr.txt').write_text(result.stderr,encoding='utf-8',newline='\n')
    raw=args.output/'observations.jsonl'
    receipt={'exitCode':result.returncode,'rawSHA':sha(raw) if raw.exists() else None}
    write(args.output/'raw-receipt.json',receipt)
    if result.returncode:raise RuntimeError('Native execution failure; observations retained')
    # The oracle is consulted only after the child is finished. This is a
    # paired consumed-data diagnosis, not a new held-out or deployment result.
    observations=[json.loads(x) for x in raw.read_text(encoding='utf-8').splitlines()]
    if len(observations)!=4 or len({(o['id'],o['condition']) for o in observations})!=4:raise ValueError('Missing/duplicate native conditions')
    summary=[]
    for o in observations:
        summary.append({k:o[k] for k in ('id','condition','stage','outcome','failure','requiredSlots','cells','lowConfidenceSources','detectorCalls','recognizerCalls','milliseconds','processPeakWorkingSetBytes')})
    write(args.output/'report.json',{'recipe':manifest['recipe'],**receipt,'observations':summary,'quality':'unassessed',
                                  'scope':'No new heldout/document/device qualification; no oracle correction'})
    print(json.dumps(summary,ensure_ascii=False),flush=True)


if __name__=='__main__':main()
