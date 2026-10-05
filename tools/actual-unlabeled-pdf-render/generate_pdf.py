"""Lossless PDF wrapper for an already consumed, wholly invented pixel input.

No oracle, drawing-glyph inventory, PDF, OCR output or network is read.
The caller supplies only the public pinned font and an owned output directory.
"""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import io
import json
import zlib
from PIL import Image

ROOT = Path(__file__).resolve().parent
PINS = {
    'generate.py': 'a5bbf0151bfd8fbb6a37005613dbc407c59d0d4ea8aa4d30f0f2041081ed0b39',
    'drawing-source.json': '25e8f58156bc3a955b3b8bf4616ad1267e627b9aff37676a829b3f3e625d815e',
}
PNG_SHA = '3bb95ebcce616cf24c6b4c431f6fe9b790491e5d22085f74e4430cfd5871b946'
FONT_SHA = 'b76b0433203017ca80401b2ee0dd69350349871c4b19d504c34dbdd80541690a'
PDF_SHA = '7ceb34d191dc48a5d6bc072e75898172cd20f356f6c9c312f558635d6a454323'
MARKER = '.owned-unlabeled-pdf-input'
MARKER_BYTES = b'unlabeled-pdf-input-v1\n'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def generate_png(font_path):
    # Pin code before executing it; never invoke its creation-only main().
    for name, expected in PINS.items():
        if sha((ROOT / 'source' / name).read_bytes()) != expected:
            raise ValueError('source pin mismatch: ' + name)
    if sha(font_path.read_bytes()) != FONT_SHA:
        raise ValueError('public font pin mismatch')
    spec = importlib.util.spec_from_file_location('pinned_pixel_source', ROOT / 'source/generate.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    source = json.loads((ROOT / 'source/drawing-source.json').read_text(encoding='utf-8'))
    png, _ = module.render(source, font_path)
    if len(png) != 44401 or sha(png) != PNG_SHA:
        raise ValueError('rendered PNG pin mismatch')
    return png


def wrap_png(png):
    """Use RGB FlateDecode without resampling, text, fonts or semantic fields."""
    if sha(png) != PNG_SHA:
        raise ValueError('input PNG pin mismatch')
    with Image.open(io.BytesIO(png)) as image:
        image.load()
        if image.mode != 'RGB' or image.size != (3740, 800):
            raise ValueError('unexpected source pixel contract')
        rgb = image.tobytes()
    compressed = zlib.compress(rgb, level=9)
    content = b'q\n3740 0 0 800 0 0 cm\n/Im0 Do\nQ\n'
    objects = [
        b'<< /Type /Catalog /Pages 2 0 R >>',
        b'<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
        b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 3740 800] /CropBox [0 0 3740 800] /Rotate 0 /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>',
        (b'<< /Type /XObject /Subtype /Image /Width 3740 /Height 800 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Interpolate false /Filter /FlateDecode /Length '
         + str(len(compressed)).encode('ascii') + b' >>\nstream\n' + compressed + b'\nendstream'),
        b'<< /Length ' + str(len(content)).encode('ascii') + b' >>\nstream\n' + content + b'endstream',
        b'<< /Producer (Independent invented lossless image PDF adapter v1) >>',
    ]
    pdf = bytearray(b'%PDF-1.7\n%\xe2\xe3\xcf\xd3\n')
    offsets = [0]
    for index, value in enumerate(objects, start=1):
        offsets.append(len(pdf))
        pdf.extend(str(index).encode('ascii') + b' 0 obj\n' + value + b'\nendobj\n')
    xref = len(pdf)
    pdf.extend(b'xref\n0 7\n0000000000 65535 f \n')
    for offset in offsets[1:]:
        pdf.extend(f'{offset:010d} 00000 n \n'.encode('ascii'))
    identifier = PNG_SHA[:32].encode('ascii')
    pdf.extend(b'trailer\n<< /Size 7 /Root 1 0 R /Info 6 0 R /ID [<' + identifier + b'><' + identifier + b'>] >>\nstartxref\n')
    pdf.extend(str(xref).encode('ascii') + b'\n%%EOF\n')
    return bytes(pdf), rgb


def verify_pdf(pdf, rgb):
    """Independently decode actual PDF before any downstream OCR can start."""
    import fitz
    with fitz.open(stream=pdf, filetype='pdf') as document:
        if len(document) != 1:
            raise ValueError('unexpected PDF page count')
        page = document[0]
        if tuple(page.rect) != (0.0, 0.0, 3740.0, 800.0) or page.rotation != 0 or page.get_text():
            raise ValueError('unexpected image-only PDF page contract')
        images = page.get_images(full=True)
        if len(images) != 1:
            raise ValueError('unexpected embedded image count')
        embedded = fitz.Pixmap(document, images[0][0])
        rendered = page.get_pixmap(matrix=fitz.Matrix(1, 1), colorspace=fitz.csRGB, alpha=True)
        if embedded.width != 3740 or embedded.height != 800 or embedded.n != 3 or embedded.samples != rgb:
            raise ValueError('embedded decoded RGB mismatch')
        expected_rgba = Image.frombytes('RGB', (3740, 800), rgb).convert('RGBA').tobytes()
        if rendered.width != 3740 or rendered.height != 800 or rendered.n != 4 or rendered.samples != expected_rgba:
            raise ValueError('actual PDF rendered RGBA mismatch')
    return {
        'verifier': 'PyMuPDF', 'version': fitz.VersionBind,
        'embeddedDecodedRGBSHA256': sha(rgb),
        'actualPDFRenderedRGBA1xSHA256': sha(expected_rgba),
        'embeddedPixelEquality': True, 'actual1xRenderPixelEquality': True,
        'pageCount': 1, 'width': 3740, 'height': 800, 'rotation': 0,
    }


def write_owned(directory, pdf, receipt):
    if directory.is_symlink() or not directory.is_dir():
        raise ValueError('owned output must be an existing real directory')
    marker = directory / MARKER
    if marker.is_symlink() or marker.read_bytes() != MARKER_BYTES:
        raise ValueError('owned output marker mismatch')
    targets = [directory / 'unlabeled-consumed-development.pdf', directory / 'receipt.json']
    if any(p.exists() or p.is_symlink() for p in targets):
        raise ValueError('refusing to overwrite previous output')
    created = []
    try:
        with targets[0].open('xb') as stream:
            created.append(targets[0])
            stream.write(pdf)
        with targets[1].open('x', encoding='utf-8', newline='\n') as stream:
            created.append(targets[1])
            json.dump(receipt, stream, ensure_ascii=False, indent=2)
            stream.write('\n')
    except BaseException:
        for path in created:
            path.unlink()
        raise


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--font', type=Path, default=Path('/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'))
    parser.add_argument('--owned-output', type=Path, required=True)
    args = parser.parse_args()
    png = generate_png(args.font)
    pdf, rgb = wrap_png(png)
    if len(pdf) != 41545 or sha(pdf) != PDF_SHA:
        raise ValueError('PDF encoding pin mismatch')
    verification = verify_pdf(pdf, rgb)
    receipt = {
        'version': 1, 'family': 'Already consumed invented unlabeled40 development input',
        'PNGSHA256': sha(png), 'PNGBytes': len(png), 'PDFSHA256': sha(pdf), 'PDFBytes': len(pdf),
        'publicFontSHA256': FONT_SHA, 'sourcePins': PINS,
        'PDFPixelTransform': 'Top-left source pixel (x,y) maps to PDF (x,800-y); MediaBox/CropBox 3740x800, Rotate0.',
        'encoding': 'Original1x RGB pixels, lossless FlateDecode, Interpolate false, no text/font objects.',
        'verification': verification, 'OCRCalls': 0, 'modelCalls': 0,
        'scope': 'PDF materialization and independent pixel verification only; actual Apple/Windows renderer pixels and full recovery remain unevaluated. No new heldout or gold.',
    }
    write_owned(args.owned_output, pdf, receipt)
    print(json.dumps(receipt, ensure_ascii=False))


if __name__ == '__main__':
    main()
