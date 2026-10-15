"""Freeze executable/input/source bytes, close worker, then allow assessment."""
import argparse
import json
import shutil
import subprocess
from pathlib import Path
from prepare import ROOT, asset, sha, write

def main():
    p=argparse.ArgumentParser();p.add_argument('--study',type=Path,required=True);p.add_argument('--dotnet',required=True);a=p.parse_args();out=a.study.resolve()
    assert not (out/'freeze.json').exists()
    project=Path(__file__).with_name('DecodingProbe.csproj')
    subprocess.run([a.dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet'],check=True)
    reader=out/'reader';shutil.copytree(project.parent/'bin/Release/net10.0',reader)
    # Preserve only the host's runtime dependencies in this copied reader.
    for child in (reader/'runtimes').iterdir():
        if child.name!='linux-x64':shutil.rmtree(child)
    sources=[*project.parent.glob('*.cs'),project,Path(__file__),Path(__file__).with_name('prepare.py'),*[ROOT/'src/Takupoke.Infrastructure/Recovery'/name for name in ['OcrAsciiRefinement.cs','OcrCtcDecoder.cs','OcrInputTransform.cs','OcrInkCropFrame.cs','OnnxAsciiOcr.cs','OnnxJapaneseOcr.cs']]]
    write(out/'freeze.json',dict(inputs=asset(out/'inputs.json',out),oracle=asset(out/'oracle.json',out),protocol=asset(out/'protocol.json',out),reader=[asset(p,out) for p in sorted(reader.rglob('*')) if p.is_file()],sources={str(p.relative_to(ROOT)):sha(p) for p in sources}))
    with (out/'stdout.txt').open('x') as stdout,(out/'stderr.txt').open('x') as stderr:
        result=subprocess.run([a.dotnet,str(reader/'Takupoke.OcrComparison.dll'),str(out/'inputs.json'),str(out/'raw.jsonl'),str(out/'tensors.bin.gz')],stdout=stdout,stderr=stderr,timeout=1900)
    write(out/'receipt.json',dict(exitCode=result.returncode,raw=asset(out/'raw.jsonl',out),tensors=asset(out/'tensors.bin.gz',out)))
    assert result.returncode==0
    print(json.dumps(dict(study=str(out),exitCode=result.returncode,rawSHA256=sha(out/'raw.jsonl'))))

if __name__=='__main__':main()
