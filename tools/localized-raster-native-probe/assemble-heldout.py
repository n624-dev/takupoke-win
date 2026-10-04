"""Losslessly assemble independent frozen PNGs; never creates or edits any gold."""
import hashlib
import importlib.util
import json
from pathlib import Path
ROOT = Path(__file__).resolve().parent
assembler = ROOT.parent / 'raster-acquisition-native-probe/fixtures/assemble.py'
if hashlib.sha256(assembler.read_bytes()).hexdigest() != '825cbb7d226585fc8e194f30484812b98b4d4edf00881e15521e14f2a5dcd3e9':
    raise ValueError('Original PNG assembler changed')
spec = importlib.util.spec_from_file_location('original_png_assembler', assembler)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
manifest_path = ROOT / 'holdout/manifest.json'
if hashlib.sha256(manifest_path.read_bytes()).hexdigest() != '050b7640b6de309db2be78a932cf558f4d36b3b89c29b95f7737228e025079b9':
    raise ValueError('Independent holdout manifest changed')
manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
for fixture in manifest['fixtures']:
    directory = ROOT / 'holdout' / fixture['id']
    oracle_bytes = (directory / 'literal-oracle.json').read_bytes()
    if hashlib.sha256(oracle_bytes).hexdigest() != fixture['oracleSha256']: raise ValueError('Independent oracle changed')
    oracle = json.loads(oracle_bytes)
    pages = []
    for page in oracle['pages']:
        path = directory / page['imageFile']
        if hashlib.sha256(path.read_bytes()).hexdigest() != page['imageSha256']: raise ValueError('Original PNG changed')
        pages.append(path)
    module.assemble(directory / 'fictional.pdf', pages)
    if hashlib.sha256((directory / 'fictional.pdf').read_bytes()).hexdigest() != fixture['pdfSha256']: raise ValueError('PDF bytes changed')
print(json.dumps({'fixedUnusedPDFs': len(manifest['fixtures']), 'nativeCalls': 0, 'scope': 'Immutable original PNG assembly only'}))
