"""One fake PDF Reader/render, bounded exact pixel transport, no OCR or gold."""
import argparse
import base64
import gzip
import hashlib
import json
from pathlib import Path
import subprocess
from report_transport import emit_report, read_reports

CASE_ID = "consumed-unlabeled-pdf-render"


def decode_reports(lines):
    reports = read_reports(lines)
    if set(reports) != {CASE_ID}:
        raise ValueError("Required actual PDF report missing or unexpected")
    return reports


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("owned_root", type=Path)
    parser.add_argument("reader_dll", type=Path)
    parser.add_argument("renderer_dll", type=Path)
    args = parser.parse_args()
    root = args.owned_root
    if not (root / ".owned-actual-pdf-render").is_file():
        raise ValueError("Owned source-only synthetic directory marker missing")
    manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    pdf = root / "input.pdf"
    if pdf.stat().st_size != manifest["pdfBytes"] or hashlib.sha256(pdf.read_bytes()).hexdigest() != manifest["pdfSHA256"]:
        raise ValueError("Actual PDF identity pin mismatch before Reader/render")
    output = root / "native-render"
    output.mkdir(exist_ok=False)
    report = {"scope": "ONE consumed invented image-only PDF; actual Windows Reader/render; OCR/adoption unassessed",
              "inputManifest": manifest, "reader": None, "renderer": None, "operationalError": None,
              "nativeOCRCalls": 0, "formalReturned": False, "qualityAssessed": False}
    try:
        reader = subprocess.run(["dotnet", str(args.reader_dll), str(pdf), manifest["pdfSHA256"], CASE_ID],
                                capture_output=True, text=True, encoding="utf-8", timeout=190)
        if reader.returncode != 0:
            raise RuntimeError("Reader helper failed: " + reader.stderr[:2048])
        report["reader"] = json.loads(reader.stdout)
        r = report["reader"]
        if r["actualPdfSha256"] != manifest["pdfSHA256"] or r["readerAttempts"] != 1 or r.get("readerError", {}).get("stage") != "raster" or r["strictAttempts"] != 0 or r["captureComplete"]:
            raise ValueError("Expected actual Reader raster-only refusal for this image-only PDF")
        rendered = subprocess.run(["dotnet", str(args.renderer_dll), str(pdf), manifest["pdfSHA256"], CASE_ID, str(output)],
                                  capture_output=True, text=True, encoding="utf-8", timeout=190)
        if rendered.returncode != 0:
            raise RuntimeError("Renderer helper failed: " + rendered.stderr[:2048])
        report["renderer"] = json.loads(rendered.stdout)
        r = report["renderer"]
        if r["actualPdfSha256"] != manifest["pdfSHA256"] or r["renderAttempts"] != 1 or r["nativeOCRCalls"] != 0:
            raise ValueError("Original PDF/render identity mismatch")
        path = output / "page1.bgra"
        if path.stat().st_size != r["bgraBytes"] or not 0 < r["bgraBytes"] <= 32 * 1024 * 1024:
            raise ValueError("BGRA original bytes/size mismatch")
        pixels = path.read_bytes()
        if len(pixels) != r["width"] * r["height"] * 4 or hashlib.sha256(pixels).hexdigest() != r["bgraSha256"]:
            raise ValueError("BGRA original shape/hash mismatch")
        encoded = output / "page1-rendered-image.bin"
        if encoded.stat().st_size != r["renderedEncodedBytes"] or not 0 < encoded.stat().st_size <= 64 * 1024 * 1024:
            raise ValueError("Encoded original render bytes/size mismatch")
        encoded_bytes = encoded.read_bytes()
        if hashlib.sha256(encoded_bytes).hexdigest() != r["renderedEncodedSha256"]:
            raise ValueError("Encoded original render hash mismatch")
        report["actualBGRAgzipBase64"] = base64.b64encode(gzip.compress(pixels, compresslevel=9, mtime=0)).decode("ascii")
        report["actualRenderedEncodedBase64"] = base64.b64encode(encoded_bytes).decode("ascii")
    except Exception as error:
        report["operationalError"] = {"type": type(error).__name__, "message": str(error)[:4096]}
    finally:
        # Validate exact own transport locally, then stream bounded lines and a
        # short summary. No platform artifact, binary Git or second render.
        lines = []
        emit_report(CASE_ID, report, lines.append)
        if decode_reports(lines)[CASE_ID] != report:
            raise ValueError("Transport self comparison failed")
        for line in lines:
            print(line, flush=True)
        r = report["renderer"] or {}
        actual_reader = report["reader"] or {}
        print(json.dumps({"summary": CASE_ID, "readerRasterRefusal": actual_reader.get("readerError", {}).get("stage") == "raster" and actual_reader.get("readerReturned") is False,
                          "rendererReturned": bool(report["renderer"]), "width": r.get("width"), "height": r.get("height"),
                          "bgraSHA256": r.get("bgraSha256"), "operationalError": report["operationalError"],
                          "nativeOCRCalls": 0, "qualityAssessed": False}), flush=True)
    if report["operationalError"] is not None:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
