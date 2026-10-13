# English G2 suitability tools

Research-only fixed recognizer comparison. Neither candidate is qualified or active; additional training and final evaluation were deferred after development failures. See the corresponding `research-results/ocr-synthetic-training-20261013/ascii-foundation-ctc-v1/README.md` for evidence and limits.

`baseline.py` verifies pinned official EasyOCR English G2 source/weights, loads tensor-only state safely, emits a single FP32 Gray64 greedy baseline on consumed development, then scores only after raw output closes. `framed.py` uses one declared all-original-ink whole-line framing condition; it does not alter source samples or correct text. `diagnose.py` reads completed predictions to count edit categories and condition regressions; it loads no model and runs no inference.

Each inference script refuses to overwrite its completed summary. Assets are intentionally outside Git at `/workspace/scratch/ascii-foundation-ctc-20261013-v1`; source PNGs are the already consumed `/workspace/scratch/ascii-line-ctc-20261013-v1/development`. Paths are explicit research workspace paths, not an app installer. Reproduction requires the recorded exact assets and dependencies, an isolated new output directory, and recognition that these pixels are consumed development. Do not relabel replay as new held-out evaluation.

The dictionary is official EasyOCR order: blank0, space43, Euro44, no UNK. Input is grayscale64 with bicubic resize/last-column padding. It is incompatible with existing BGR48 or from-scratch BGR64 preprocessing and dictionaries. There is no exported or deployable model in this namespace. iOS and production code remain untouched.

`header_infer.py` later runs a single parent-requested fixed48 ordinal short-header/white-control check without oracle access. Launch `header_score.py` only after that external inference process exits; `header_diagnose.py` reads saved outputs to separate literal contrasts and native sequence scores. Results remain negative consumed-development evidence, permanently excluded from training, not final or qualification.
