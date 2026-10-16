# Japanese-font ASCII adaptation V4

Independent synthetic crop research; model execution is governed by the immutable `protocol.json`, registration and corpus manifests. No product default or iOS changes are made here.

The official PP-OCRv5 English architecture and full 438-way CTC dictionary are the foundation. Actual initializer and teacher are the successful adapted **font-balanced-v1 epoch4**, SHA256 `7313bee5dabf17998010c6f47844fe80649744b03c0366af05c627fc47314547`. Rejected V2/V3 weights are not used. Foundation pretraining font exposure is unknown.

V4 freezes every CNN parameter and batch-normalization statistic, adapting the encoder, full classifier and reject head only. The 10 Japanese training families include disclosed consumed V3 development fonts. Development Japanese families Murecho/NewTegomin are separate. Untouched V3 fresh Japanese families ShipporiAntique/ZenAntiqueSoft remain unopened until an eligible V4 development checkpoint is selected. Latin development/fresh families are consumed retention controls with new literals. BIZUDPGothic, ZenMaruGothic and BIZUDPMincho are reserved for the parent and excluded entirely. MPLUS2 was additionally reserved after the V4 corpus freeze; it is absent from V4 and remains reserved for later trials. The frozen protocol and corpora were not changed.

Training has 1,920 positives and 224 negative controls; development has 512 positives and 112 controls. Short arbitrary codes, case pairs and Il1/O0 atomic pairs preserve font advances, shared baselines, relative heights and internal spacing. Physical aliases exclude the **whole training unit** before updates. Development/fresh aliases remain in the main evaluation denominator; post-scoring diagnostics never filter selection. Natural English and all 95 printable ASCII characters provide retention controls. Source clipping, interior damage, multiline, noise, blank and CJK/mixed negatives retain their separate status.

Prior/root literal, normalized-input and image hashes are excluded; the root oracle is read only for complete strings and components. Shared characters are not prohibited or described as unseen. All raw CTC intervals, maximum probability per run, source-padding checks and reject values are retained. Probabilities are uncalibrated; no lexicon, label repair or prefix generation is introduced.

Run with the existing CPU runtime and two threads, in this order:

1. `download_fonts.py`, `assets.py`, `prepare.py`: font/license/source/protocol/exclusion and train/dev manifests freeze before model calls.
2. `train.py`: cache FP32 frozen CNN/teacher values, require full newdev original/cached equality before optimization, then four fixed epochs and baseline-inclusive full-dev selection.
3. `fresh.py`: only for a selected candidate, generate and score fixed independent fresh once. Never reselect from fresh.
4. `postfit.py`, `alias_diagnostic.py`: descriptive diagnostics after selection/fresh; baseline fallback uses fixed final epoch4 for trainfit.
5. `portable.py`, `package_portable.py`: only if fresh guards pass, export standard FP32 ONNX opset17/full438 and compare all fixed dev decisions plus dynamic numerical cases. Linux ORT validation is not Windows document or iPhone qualification.

The fixed V4 fresh failed its code accuracy and font guards. After that result, the parent explicitly requested a separate exploratory export of the **original development-selected epoch2 only**, after the original diagnostics close. `exploratory_epoch2.py` and `bundle_exploratory.py` implement that additional route investigation. The original trial remains FAIL with active baseline fallback; no epoch or threshold is changed, and fresh is not inferred again. Its small bundle references the existing checkpoint instead of duplicating it. This separate export does not pass the original portability/quality gate.

All generated images, caches, checkpoints and ONNX binaries stay under `/workspace/scratch/ascii-english-v5-japanese-fonts-v4`. Reviewable manifests, raw logs, decisions and final findings are retained under `research-results/ocr-synthetic-training-20261013/ascii-english-v5-japanese-fonts-v4`. Internal working policy stays outside Git. No optimizer repetition, fresh reselection, commit, push or automatic activation is permitted by this workflow.
