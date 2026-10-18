# Additional local recognition comparisons

All arms consume the same SHA-pinned, already-used fictional PNG development
packet. A separate oracle is opened by the parent only after the native child
terminates. No school material, source annotation, expected class dictionary,
case repair, crop tuning, font routing or agreement selection enters inference.
These are component measurements, not fresh document/device qualification.

* `run.py` / `worker.py`: official NDLOCR-Lite PARSeq30/50/100, pinned upstream
  d001dad2013ceabb5736ff8d637474e443eb73c2. Native preprocessing and first-EOS
  autoregressive decoding. No CTC merging or detector. National Diet Library,
  Japan; [upstream](https://github.com/ndl-lab/ndlocr-lite), CC-BY-4.0. Preserve
  attribution. The separate detector/DINOv3 license is not used here.
* `run_tesseract.py` / `tesseract_worker.py`: official English tessdata_best,
  Tesseract5.5.0, OEM1/PSM7, original42 headers/six blanks, one thread. Raw TXT
  and TSV retained; exactly one terminal TXT LF removed for literal scoring.
* `download_got.py`, `run_got.py` / `got_worker.py`: official Apache2.0
  GOT-OCR-2.0-hf pinned assets, offline standard Transformers without remote
  code. CPU2/FP32/eager, native plain processor, no answer prompt or sampling.

All three conditions are rejected for general production recognition. GOT's
header results are an unqualified separate research lead. Its token confidence,
source localization, font generalization, full-document result and Windows
memory have not been established. Empty-image hallucinations remain raw errors.
The initial missing Tesseract output configuration and unsupported GOT SDPA
attempt are retained as execution failures, never counted as model refusals.
No model/catalog/threshold is changed by these tools.

Closed raw results and SHA/size inventories:
[NDL](../../research-results/ndlocr-native-20261013/README.md),
[Tesseract](../../research-results/tesseract-eng-best-20261013/README.md),
[GOT](../../research-results/got-ocr2-20261013/README.md).
Model/font binaries stay outside Git. The Linux GOT worker uses `resource` for
RSS and is not a Windows measurement tool. Runtime versions and all exact
artifact/source hashes are in each frozen protocol.

```sh
python -B -m unittest discover -s tools/ndlocr-comparison -p 'test_*.py'
python tools/ndlocr-comparison/run.py --upstream <pinned-ndlocr-lite> --output <new-owned-directory>
python tools/ndlocr-comparison/run_tesseract.py --assets <pinned-eng-best-assets> --output <new-owned-directory>
python tools/ndlocr-comparison/download_got.py --output <new-owned-assets>
python tools/ndlocr-comparison/run_got.py --assets <pinned-got-assets> --output <new-owned-directory>
```

As-run NDL: Python3.12, onnxruntime1.26.0, numpy2.2.2,
opencv-python-headless4.11.0.86, Pillow12.3.0, PyYAML6.0.3.
As-run GOT: torch2.7.1+cpu / transformers5.19.0; remaining versions are frozen
in the report. Weight acquisition precedes opening any image.
