"""Generate only fixed consumed fictitious pixels/PDF; no OCR or gold input."""
import argparse
import base64
import hashlib
import json
import sys
import zlib
from pathlib import Path
import urllib.request
import generate_pdf
import PIL
from PIL import features
from contract import PDF_SHA, RGB_SHA

FONT_URL = "https://raw.githubusercontent.com/notofonts/noto-cjk/523d033d6cb47f4a80c58a35753646f5c3608a78/Sans/OTC/NotoSansCJK-Regular.ttc"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("owned_root", type=Path)
    parser.add_argument("--github-output", type=Path)
    parser.add_argument("--font", type=Path)
    args = parser.parse_args()
    # Record environment before any byte pin can fail. Drift is a generator
    # prerequisite failure, never an OCR/model failure.
    print(json.dumps({"generatorRuntime": {"Python": sys.version, "Pillow": PIL.__version__,
          "FreeType": features.version("freetype2"), "PillowZlib": features.version("zlib"),
          "PythonZlibBuild": zlib.ZLIB_VERSION, "PythonZlibRuntime": zlib.ZLIB_RUNTIME_VERSION}}), flush=True)
    root = args.owned_root
    if root.is_symlink() or not root.is_dir() or any(root.iterdir()):
        raise ValueError("Empty real owned output directory required")
    (root / ".owned-actual-pdf-render").write_bytes(b"actual-pdf-render-v1\n")
    font = args.font
    if font is None:
        font = root / "public-font.ttc"
        with urllib.request.urlopen(FONT_URL, timeout=40) as response, font.open("xb") as target:
            size = 0
            while True:
                block = response.read(1024 * 1024)
                if not block:
                    break
                size += len(block)
                if size > 32 * 1024 * 1024:
                    raise ValueError("Public font capacity guard")
                target.write(block)
    png = generate_pdf.generate_png(font)
    pdf, rgb = generate_pdf.wrap_png(png)
    verification = generate_pdf.verify_pdf(pdf, rgb)
    if len(pdf) != 41545 or hashlib.sha256(pdf).hexdigest() != PDF_SHA or hashlib.sha256(rgb).hexdigest() != RGB_SHA:
        raise ValueError("Consumed PDF/embedded original pixel pins changed")
    if len(pdf) > 256 * 1024:
        raise ValueError("Bounded cross-job PDF transport capacity")
    (root / "input.pdf").write_bytes(pdf)
    manifest = {"scope": "Known consumed invented source; no new heldout, gold or OCR",
                "pdfSHA256": PDF_SHA, "pdfBytes": len(pdf), "pngSHA256": generate_pdf.PNG_SHA,
                "embeddedRGBSHA256": RGB_SHA, "verification": verification,
                "sourcePins": generate_pdf.PINS, "fontSHA256": generate_pdf.FONT_SHA}
    (root / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    if args.github_output:
        # Job outputs are transient workflow transport, not artifacts, source
        # files or public console binary. The PDF is wholly fictitious.
        data = base64.b64encode(pdf).decode("ascii")
        meta = base64.b64encode(json.dumps(manifest, separators=(",", ":")).encode()).decode("ascii")
        if (len(data) + len(meta)) * 2 > 900000:
            raise ValueError("Cross-job UTF16 output limit")
        parts = [data[i:i + 20000] for i in range(0, len(data), 20000)]
        if len(parts) != 3:
            raise ValueError("Fixed PDF cross-job part count")
        with args.github_output.open("a", encoding="utf-8", newline="\n") as target:
            # Individual Windows environment variables remain under32KiB.
            target.write("pdf_count=3\nmanifest_base64=" + meta + "\n")
            for index, part in enumerate(parts):
                target.write(f"pdf_part{index}=" + part + "\n")
    print(json.dumps(manifest, separators=(",", ":")), flush=True)


if __name__ == "__main__":
    main()
