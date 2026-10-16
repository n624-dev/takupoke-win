# Local synthetic OCR adaptation (research)

This experiment changes no application provider, model catalog, confidence floor,
validator or iOS source. An improvement in text crops cannot qualify a complete
document. Model assets and generated pixels stay outside the repository.

`protocol.json` was fixed before training. The full vocabulary and BGR48 input
transform are retained. Only the SVTR encoder and linear classifier are trained;
the backbone and all BatchNorm statistics remain fixed. A frozen full-vocabulary
teacher limits drift. Four equally weighted categories contain independent random
codes, arbitrary ASCII, Japanese character strings, and mixed strings. These are
fictional strings, not school subjects, names, rooms or consumed OCR corrections.

Train (1,536 rows, Noto Sans/Serif), development (192 rows, MPLUS1p), and final
(384 rows, reserved Zen Kaku Gothic New/BIZ UDP Mincho) have independent seeds and
disjoint literal strings. Development selects the baseline or one epoch by exact
match, CER, then earliest epoch. The selected hash is persisted before final
pixels are generated. The final corpus never selects an inference candidate or
repairs a prediction. The separate `ood-protocol.json` fixes an additional 64-row
challenge using CJK characters never seen in train/development.

The corpus intentionally consists of single-line crops with controlled padding,
19–33 px font sizes and modest blur/JPEG/gray variation. It does not cover coloured
ink, arbitrary document layouts, every glyph class, or low resolution scans.
Blank/clipped/multiline probes are separate and are not scored as recoverable
ordinary text. Neither CTC confidence nor an empty recognition is an ink-proof or
damage detector.

The runtime used Python 3.12 and the pinned base model from the protocol, with
PyTorch CPU 2.7.1, Transformers 5.19.0 and ONNX Runtime 1.23.2. Assets/fonts are
downloaded from fixed revisions, hashed and licensed in experiment receipts.
No inference service or network is used by these scripts.

Every JSON/YAML/metadata text read uses explicit UTF-8. Generated text uses UTF-8
without a BOM and LF newlines, including the dictionary's final newline, so the
dictionary hash is independent of Windows' default code page and CRLF convention.

Example commands, with assets already provisioned:

```bash
python train.py --assets /path/to/base --output /path/to/new-experiment
python final_evaluate.py --assets /path/to/base --experiment /path/to/experiment --results /path/to/new-results
python ood_evaluate.py --assets /path/to/base --experiment /path/to/experiment --results /path/to/results
python profile_onnx.py --model /path/to/experiment/selected.onnx --pixels /path/to/experiment/final --output /path/to/new-profile.json
python convert_coreml.py --assets /path/to/base --experiment /path/to/experiment --results /path/to/results
python package.py --assets /path/to/base --experiment /path/to/experiment --results /path/to/results
```

Font source paths in `corpus.py` describe this managed research workspace. A
different machine must provision those exact hashed fonts at equivalent paths.
The experiment refuses to overwrite completed selection/final artifacts.

`verify_export.py` preserves and corrects the first run's invalid post-export
Torch mode comparison. Legacy PyTorch ONNX export restores the wrapper's original
training mode recursively. The wrapper must be explicitly `.eval()` so that the
child recognizer remains in eval mode. The correction reloads the original
selected checkpoint, uses no gold, and preserves the original model selection and
all pre-export crop accuracy outputs. The corrected default exporter now sets
both wrapper and child to eval. The invalid receipt remains available for audit.

The portable package contains standard ONNX, safetensors/config, the official
18,710-entry dictionary, external preprocessing/greedy CTC reference, hashes and
license provenance. Optional CoreML conversion on Linux does not test iPhone
predictions, performance or memory. An iOS consumer must independently retain
source positions, crop/cell ownership, unsupported-padding rejection, original
ink evidence and deterministic validation. This experiment edits no iOS files.

Both existing model formats take input `x` and return `probabilities`, in float32.
The ONNX graph declares symbolic batch and width dimensions; consumers must enforce
the application's batch 1 and width 320..960 bounds. CoreML explicitly fixes batch
1 and constrains width to 320..960 (default 320). Both use the external BGR48
normalized tensor transform, not an embedded RGB image processor. The CoreML outer
output feature description declares FLOAT32 without a shape; consumers must inspect
the actual result dimensions and require `[1,timeSteps,18710]`. These are schema
checks only, not an Apple runtime prediction test. Source fixes after the experiment
do not rebuild the existing package, ZIP, weights or evaluation receipts.

Production review must use immutable, new full-document pixels and values, with
no gold-driven candidate selection, no manually supplied headers/structure and
at most three actual body corrections per complete document. Missing positions,
ambiguous cell membership, clipped glyphs and multiline crops must not be accepted
from this recognizer's confidence alone. Keep the existing production baseline
until that independent qualification succeeds.
