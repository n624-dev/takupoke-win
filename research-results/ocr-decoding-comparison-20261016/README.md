# Native-code accuracy exploration, 2026-10-16 JST

These are **consumed fictional development** results on all 85 original primary
line crops from the first previously evaluated native Windows page. All 72 body
fields are scored through their existing source-field provenance, including the
48 subject/teacher fields. The baseline has 66/72 exact fields, 18/24 exact rooms,
and 6 character edits among 452 reference characters. Crops were not selected
using errors. Worker input contains pixels, models and frozen primary observations;
reference values are separate and assessed after successful closed inference.

| Method | Exact fields / 72 | Character edits | Decision |
| --- | ---: | ---: | --- |
| Existing v1 manual route | 66 | 6 | Baseline |
| Beam 8, v1/v2 mean, original or margin 2 | 66 | 6 | No gain |
| Margin 0, v1 | 64 | 12 | Regresses |
| Official English v5, native floor | 66 | 6 | No gain; retaining low scores loses fields |
| Server v5 | 66 | 6 | No exact-field gain |
| Japanese v3 | 58 | 14–18 | Regresses |
| Tesseract English best, fixed PSM 7/13 | 57–58 | 19–21 | Regresses |
| GLM-OCR original FP32, raw or framed transport | 66 | 12 | Wrong fields remain wrong; more character errors |
| Qwen3-VL-2B Q8, raw or framed transport | 61 | 15–17 | Regresses |
| Exploratory Japanese-font epoch 1, margin 2, v1 fallback | **67** | **5** | **1 gain, 0 losses; whole-document test pending** |

Every declared model/decoder/length/framing arm, including regressions, is in its
`report.json`; native observations are byte-exact gzip records with original hashes
in the curation manifest. Full tensors and reader binaries remain in owned scratch
and their hashes are retained. Individual fictional source crops are published in
`independent-tails/packet.json`; no private PDF, full page or real-school data is
included. The first Tesseract attempt failed collecting TSV before any scored call;
its failure/source are retained. CLI output-renderer flags were corrected without
changing the two registered recognition conditions.

**The original v3 standalone development gate failed and selected v1.** Its fresh
set remains unopened. Epoch 1 was requested separately for an exploratory actual
route experiment using consumed development; it is not relabeled as a passing
original-trial selection. The 7.6% component code-CER reduction missed the original
10% minimum. The new document-route experiment instead requires independently
verified literal gains, preserved source/field coverage and bounded review on
whole native documents. No fresh values selected a checkpoint or framing.

The frozen proposal uses greedy CTC and existing same-length/source/reject checks.
An eligible manual code first uses the exploratory expert on a fixed two-pixel
frame preserving **every original ink pixel**. Invalid expert text, rejection or
padding uses the complete existing v1 decision. The ordinary route, headers and
Japanese prefix are retained. Native confidence and CTC source intervals are never
raised or clamped. The actual optional C# implementation matches all 85 proposed
line strings; its complete glyphs and confidences are retained in
`native-actual-expert-equivalence`. No default bundle is activated here.

The separate one-page v1/v1 full-pipeline control matches acquisition, source
document and formal output exactly. Both refuse `weekdays` because only Monday
was supplied. This establishes comparison identity, not positive whole-document
qualification. Actual whole-document comparison uses five weekdays and four
positive documents: two consumed fonts and two previously unused fonts with new
literal codes. Missing-day and missing-class negatives are also included.
[The prospective protocol](../../tools/ocr-whole-model-comparison/README.md)
specifies coverage, review, native rendering and paired improvement requirements;
its Windows result is pending at this publication.

The isolated Sawarabi Gothic `I`/`l` raster can be identical while its advance
differs. All six complete native error-code alternatives tested here have different
rasters; they are not declared fundamentally unrecognizable. The training report
separately records exact aliases for some other font/rendering conditions.

GLM took 780.22 seconds with peak RSS 6,831,321,088 bytes; Qwen took 122.90 seconds
with 3,341,713,408 bytes. These ran alongside training and other short checks on
Linux. They are not isolated Windows CPU timings. Autoregressive/Tesseract scores
are not substituted for calibrated field confidence or CTC source proof.

[Training and all failed gates](../ocr-synthetic-training-20261013/ascii-english-v5-japanese-fonts-v3/README.md) ·
[Portable exploratory ONNX](../../model-artifacts/ascii-english-v5-japanese-fonts-v3-epoch1-exploratory/README.md)
