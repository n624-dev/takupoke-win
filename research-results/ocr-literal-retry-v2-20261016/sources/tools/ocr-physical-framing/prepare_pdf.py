#!/usr/bin/env python3
"""Lossless image-only PDFs from owned original BGRA files, without annotations."""
import argparse
import hashlib
import json
from pathlib import Path
import zlib


def main():
    p=argparse.ArgumentParser();p.add_argument('--manifest',type=Path,required=True);args=p.parse_args()
    root=args.manifest.parent;manifest=json.loads(args.manifest.read_text(encoding='utf-8'))
    for doc in manifest['documents']:
        objects=[b'<< /Type /Catalog /Pages 2 0 R >>',b''];pageids=[]
        for page in doc['pages']:
            item=page['pixels'];source=(root/item['file']).read_bytes()
            if len(source)!=item['bytes'] or hashlib.sha256(source).hexdigest()!=item['sha256']:raise ValueError('Pixel identity changed')
            w,h=page['width'],page['height']
            if len(source)!=w*h*4 or any(a!=255 for a in source[3::4]):raise ValueError('Only original opaque source pixels accepted')
            rgb=bytearray(w*h*3);rgb[0::3]=source[2::4];rgb[1::3]=source[1::4];rgb[2::3]=source[0::4]
            image=zlib.compress(rgb,9);n=len(objects)+1;pageids.append(n)
            objects.append(f'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w/2:g} {h/2:g}] /Resources << /XObject << /I {n+1} 0 R >> >> /Contents {n+2} 0 R >>'.encode())
            objects.append(f'<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {len(image)} >>\nstream\n'.encode()+image+b'\nendstream')
            content=f'q {w/2:g} 0 0 {h/2:g} 0 0 cm /I Do Q\n'.encode()
            objects.append(f'<< /Length {len(content)} >>\nstream\n'.encode()+content+b'endstream')
        objects[1]=f'<< /Type /Pages /Count {len(pageids)} /Kids ['.encode()+b' '.join(f'{n} 0 R'.encode() for n in pageids)+b'] >>'
        pdf=bytearray(b'%PDF-1.4\n%\xe2\xe3\xcf\xd3\n');offsets=[0]
        for n,body in enumerate(objects,1):
            offsets.append(len(pdf));pdf+=f'{n} 0 obj\n'.encode()+body+b'\nendobj\n'
        xref=len(pdf);pdf+=f'xref\n0 {len(offsets)}\n0000000000 65535 f \n'.encode()
        for offset in offsets[1:]:pdf+=f'{offset:010d} 00000 n \n'.encode()
        pdf+=f'trailer\n<< /Size {len(offsets)} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n'.encode()
        path=root/(doc['id']+'-lossless.pdf');path.write_bytes(pdf)
        doc['originalDrawingPDFSHA256']=doc['originalPDFSHA256'];doc['originalPDFSHA256']=hashlib.sha256(pdf).hexdigest()
        doc['sourcePDF']={'file':path.name,'bytes':len(pdf),'sha256':doc['originalPDFSHA256']}
    manifest['sourceScope']='New lossless image-only PDF containers; consumed original drawing pixels, no extra independent document credit'
    (root/'pdf-inputs.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps({'imageOnlyPDFs':len(manifest['documents']),'modelCalls':0}))


if __name__=='__main__':main()
