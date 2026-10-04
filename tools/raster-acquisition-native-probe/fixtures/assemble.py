"""Rebuild fixed lossless image-only PDFs from frozen RGB PNG bytes, no fonts/OCR."""
from pathlib import Path
import hashlib
import json
import struct
import zlib

def png_stream(path):
    data = path.read_bytes()
    if data[:8] != b'\x89PNG\r\n\x1a\n': raise ValueError('PNG signature')
    cursor, parts, dimensions = 8, [], None
    while cursor < len(data):
        size = struct.unpack('>I', data[cursor:cursor+4])[0]
        kind, chunk = data[cursor+4:cursor+8], data[cursor+8:cursor+8+size]
        if len(chunk) != size or struct.unpack('>I', data[cursor+8+size:cursor+12+size])[0] != zlib.crc32(kind+chunk): raise ValueError('PNG chunk')
        if kind == b'IHDR':
            w,h,depth,color,compression,filtering,interlace = struct.unpack('>IIBBBBB', chunk)
            if (depth,color,compression,filtering,interlace) != (8,2,0,0,0) or not 1 <= w <= 4096 or not 1 <= h <= 4096: raise ValueError('Only bounded RGB PNG')
            dimensions = w,h
        if kind == b'IDAT': parts.append(chunk)
        cursor += size+12
        if kind == b'IEND': break
    if dimensions is None or not parts or cursor != len(data): raise ValueError('PNG completeness')
    return *dimensions, b''.join(parts)

def assemble(path, page_paths):
    objects = [b'<< /Type /Catalog /Pages 2 0 R >>',
        ('<< /Type /Pages /Count %d /Kids [%s] >>' % (len(page_paths), ' '.join(f'{3+i*3} 0 R' for i in range(len(page_paths))))).encode()]
    for i, png in enumerate(page_paths):
        width,height,pixels = png_stream(png); w,h = width*72/150,height*72/150
        objects.append((f'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w:.4f} {h:.4f}] /Resources << /XObject << /Im {5+i*3} 0 R >> >> /Contents {4+i*3} 0 R >>').encode())
        content = f'q {w:.4f} 0 0 {h:.4f} 0 0 cm /Im Do Q'.encode()
        objects.append(f'<< /Length {len(content)} >>\nstream\n'.encode()+content+b'\nendstream')
        objects.append((f'<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns {width} >> /Length {len(pixels)} >>\nstream\n').encode()+pixels+b'\nendstream')
    output, offsets = bytearray(b'%PDF-1.4\n%\xe2\xe3\xcf\xd3\n'), [0]
    for i,obj in enumerate(objects,1):
        offsets.append(len(output)); output.extend(f'{i} 0 obj\n'.encode()+obj+b'\nendobj\n')
    xref = len(output); output.extend(f'xref\n0 {len(offsets)}\n0000000000 65535 f \n'.encode())
    for offset in offsets[1:]: output.extend(f'{offset:010d} 00000 n \n'.encode())
    output.extend(f'trailer\n<< /Size {len(offsets)} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n'.encode()); path.write_bytes(output)

if __name__ == '__main__':
    root = Path(__file__).resolve().parent
    manifest = json.loads((root/'manifest.json').read_text())
    for fixture in manifest['fixtures']:
        directory = root/fixture['id']; oracle = json.loads((directory/'literal-oracle.json').read_text())
        pages = [directory/p['imageFile'] for p in oracle['pages']]
        for p,path in zip(oracle['pages'],pages):
            if hashlib.sha256(path.read_bytes()).hexdigest() != p['imageSha256']: raise ValueError('Frozen image SHA mismatch')
        assemble(directory/'fictional.pdf',pages)
        if hashlib.sha256((directory/'fictional.pdf').read_bytes()).hexdigest() != oracle['pdfSha256']: raise ValueError('Frozen PDF SHA mismatch')
    print(json.dumps({'fixedImageOnlyPdfs':len(manifest['fixtures']), 'scope':'Lossless assembly, not OCR or pipeline validation'}))
