# Consumed short-header suitability check

**Not suitable for activation; additional training and ONNX export remain deferred.** The parent-requested fixed set of 42 short headers and six white controls gave exact26/42, with13/21 in each font cohort. Research acceptance retained24 correct headers and13 wrong headers. Blank predictions were empty6/6; no emitted interval entered padding. Retaining source ink does not establish correct interpretation.

The condition is unchanged English G2 FP32 CPU2, official97-class dictionary/full greedy output, first pass, all-original-nonwhite bounding rectangle plus two original pixels, then official Gray64 bicubic/last-column-padding preprocessing. No alternate condition, retry, case conversion, dictionary repair or target-based routing was run. Original PNG and decoded BGRA hashes were verified. All48 original nonwhite crops were retained. Each unjudged row stores original dimensions, crop offsets, source crop SHA, input/valid widths, padding columns, raw CTC intervals, unclamped approximate source boxes, minimum emitted-piece score, and official EasyOCR sequence confidence.

`fictional-pixels.json` SHA256 `6e54d6bc0cc2213dc51a6e0ff67f3551a736e4e75641ec00f96c7b9de87f14ff` contains no gold. Ordinals were fixed before inference: n072..095 and n168..191. The external Python inference process closed its48-row raw output and completed with exit0 before a separate scorer first opened `oracle.json` (SHA256 `1c5652c38a5d84691d5105bd4e851349bc1879d38b2a1cc2a2b6169c12683095`). No target or cohort reached inference. Raw SHA256 `b6ee25c69aaa149abc75574086467f69fcb7a7f36ee49d615d5bc5a5902145ca` is preserved. This is consumed fictional development, not new held-out or final accuracy. All these images and literals are permanently excluded from future training.

| Printed literal | Sans output | Serif output |
|---|---|---|
| AI_1 | AL1 — wrong accepted | AI_1 — exact, rejected |
| AI_2 | Al_2 — wrong rejected | AL2 — wrong accepted |
| Al_1 | Al_1 — exact accepted | AL_1 — wrong rejected |
| Al_2 | AL2 — wrong accepted | AL_2 — wrong accepted |
| A1_1 | Al_1 — wrong accepted | Al_1 — wrong accepted |
| A1_2 | Al_2 — wrong accepted | Al_2 — wrong accepted |

Acceptance in this table is the unchanged uncalibrated research gate: nonempty printableASCII, minimum emitted-piece confidence>=0.8 and source-supported CTC extent. It is not a production eligibility decision. Official native sequence confidences are retained independently; at the same fixed descriptive floor0.8 they include21 correct and11 wrong headers. Highest wrong minimum-piece confidence is0.998389; highest wrong official sequence confidence is0.997402. Neither statistic constitutes confidence calibration. The Serif A1_1 error has minimum-piece0.801105 and official sequence0.798281, illustrating that the two scores differ; changing their meaning cannot be used to claim exact recognition.

The parent separately reported GOT-OCR2 at19/21 Sans and21/21 Serif on the same consumed headers. It was not rerun here, and its full-document, safety, speed and confidence eligibility are separate questions. English G2's header-only result offers no reason to overturn the prior decision to defer this adaptation. This is not a proof that all possible fine-tuning is futile; it is a bounded negative suitability result with explicit high-confidence literal failures.

Inference took0.704 seconds for48 crops, median model-only12.44ms, peak RSS359.84MiB under concurrent Linux work. These are not native Windows or iPhone speed/memory qualification. No model training, ONNX/CoreML export, final-font rendering/inference, production activation, iOS change or child commit/push occurred. Existing general-model portable artifacts remain unchanged. Sawarabi Gothic/Mincho final reservation remains in force.

`freeze.json`, `unjudged.jsonl`, `inference-complete.json`, `judged.json`, `summary.json` and `native-confidence-diagnosis.json` preserve the evidence. Source: `header_infer.py`, `header_score.py`, `header_diagnose.py` under `tools/ocr-synthetic-training/ascii-foundation-ctc/`.
