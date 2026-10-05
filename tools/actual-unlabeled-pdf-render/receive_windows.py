"""Check transient source-only PDF job outputs before any actual renderer."""
import base64
import hashlib
import json
import os
from pathlib import Path
import sys
from contract import PDF_SHA, RGB_SHA

root = Path(sys.argv[1])
if root.is_symlink() or not root.is_dir() or any(root.iterdir()):
    raise ValueError("Empty real owned output directory required")
(root / ".owned-actual-pdf-render").write_bytes(b"actual-pdf-render-v1\n")
if os.environ["SYNTHETIC_PDF_COUNT"] != "3":
    raise ValueError("Fixed PDF cross-job part count mismatch")
parts = [os.environ[f"SYNTHETIC_PDF_PART{index}"] for index in range(3)]
if any(not 0 < len(part) <= 20000 for part in parts):
    raise ValueError("Bounded Windows environment PDF part length")
raw = "".join(parts)
metadata = os.environ["SYNTHETIC_MANIFEST_BASE64"]
if len(raw) > 60000 or len(metadata) > 32000:
    raise ValueError("Cross-job input bound")
pdf = base64.b64decode(raw, validate=True)
manifest = json.loads(base64.b64decode(metadata, validate=True).decode("utf-8", errors="strict"))
if len(pdf) != 41545 or hashlib.sha256(pdf).hexdigest() != PDF_SHA or manifest["pdfSHA256"] != PDF_SHA or manifest["pdfBytes"] != len(pdf) or manifest["embeddedRGBSHA256"] != RGB_SHA:
    raise ValueError("Original PDF/hash manifest mismatch")
if not manifest["verification"]["embeddedPixelEquality"] or not manifest["verification"]["actual1xRenderPixelEquality"]:
    raise ValueError("Original pixel verification missing")
(root / "input.pdf").write_bytes(pdf)
(root / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"receivedPDFSHA256": PDF_SHA, "receivedPDFBytes": len(pdf), "nativeRenderCalls": 0}))
