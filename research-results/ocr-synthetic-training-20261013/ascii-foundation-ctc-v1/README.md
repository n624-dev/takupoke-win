# English foundation CTC: development-only decision

This candidate remains **research-only, unqualified and inactive**. No additional training is justified by this bounded suitability check, and no final fonts or final documents were opened. This is a decision to defer this training path, not evidence that adapting English CTC can never improve it. Parent research on acquisition, original-ink ownership and independent full documents continues; the production-quality goal remains unmet.

## Fixed model and two declared input conditions

Official EasyOCR `v1.7.2` English G2 recognizer: 3,781,345 parameters, FP32 CPU2, one first-pass greedy decoding pass. The release PTH is 15,143,997 bytes, SHA256 `e2272681d9d67a04e2dff396b6e95077bc19001f8f6d3593c307b9852e1c29e8`, and matches the official config MD5 `5864788e1821be9e454ec108d61b887d`. The single expected ZIP member was verified before extraction. Loading uses `torch.load(weights_only=True)`, requires tensor-only state, and checks strict names/shapes. Installed model source files match the pinned official files. Code/config/license/download hashes are preserved in `baseline-freeze.json`; assets remain outside Git. The pinned repository license is Apache 2.0. No separate model card or separate weight-license document was found in this download; this report does not independently certify distribution rights beyond that provenance.

The original dictionary order is retained: blank index 0, 95 printable ASCII characters and Euro, 97 classes total. Space is index 43 and Euro index 44. There is no UNK or reject head; it must not be decoded using the from-scratch experiment's ASCII-sorted dictionary.

Both conditions apply the official recognizer's Gray64 AlignCollate/NormalizePAD logic directly to original line crops: OpenCV grayscale, PIL bicubic resize, normalization `(pixel/255 - .5)/.5`, width at least 64 rounded to a 64 multiple and bounded at 960, `validWidth = ceil(64*sourceWidth/sourceHeight)`, and last-column replication padding. There is no detector, rotation, contrast retry, lexicon, character filtering, quantization or text correction. This is a recognizer baseline, not a claim of complete EasyOCR Reader parity.

1. Baseline: the complete original development crop.
2. Framed: all RGB samples differing from white 255 determine one whole-line bounding rectangle, expanded by two **original** pixels and clipped only to the source canvas. Every original nonwhite sample remains in that rectangle. Internal gaps and shared line baseline are preserved before the same Gray64 preprocessing. No whitening, ink threshold, denoising, glyph-wise crop, score-dependent framing or oracle selection is used.

These are the **already consumed** ASCII-v1 development PNGs: 384 positive lines and 128 controls, with pixel hashes checked. Kosugi was previously used by the parent for source-only PDF extraction and by ASCII-v1 for recognizer development. This is neither independent held-out accuracy nor new final credit. Baseline was frozen before its calls; framed was declared after diagnosing baseline and frozen before its calls. Neither frozen source was subsequently modified. Complete unjudged raw JSONL was closed before literal scoring. Gold never determined model input, output, framing or decoding.

Experimental acceptance is unchanged in both conditions: nonempty printable ASCII output, minimum emitted-piece confidence >=0.8, and no CTC interval entering padding. It is an **uncalibrated research diagnostic**, not a production policy.

## Outcome

| Consumed development metric | Full original crop | Whole-line framing |
|---|---:|---:|
| Exact positive lines /384 | 70 | 75 |
| Character error rate | 17.114% | 17.205% |
| Exact accepted positive lines | 49 | 59 |
| Incorrect accepted positive lines | 76 | 80 |
| Accepted negative controls /128 | 8 | 12 |
| Code exact /96 | 23 | 23 |
| Lookalike exact /96 | 7 | 6 |
| Natural English exact /96 | 35 | 39 |
| Arbitrary printable ASCII exact /96 | 5 | 7 |

Framing improved 26 positive rows and regressed 21. All 512 crops retained all original nonwhite samples, but recognition and safety did not improve consistently. Framed accepted controls were clipped 4/32, multiline 6/32 and noise 2/32; CJK 0/32. Zero CJK acceptance does not establish script rejection for unseen or mixed inputs.

Read-only minimum-edit diagnosis counts case and `I/l/1`, `O/0` mismatch rows separately. Framed lookalike rows contain 52 confusable-mismatch rows, including 12 accepted; case mismatches occur in 16 rows, including one accepted. Framed code rows contain 32 confusable mismatch rows, including 10 accepted, and 15 case mismatch rows, including five accepted. Wrong accepted lines reach minimum-piece confidence **0.999493**. Insertion of space remains common: 195 aligned insertions, including 42 in accepted lines. Alignment is descriptive, not glyph localization or an inference repair.

No dedicated paired `AI/Al/A1` header set was evaluated in this 384-positive-line experiment. These arbitrary-code controls therefore do not claim literal success on any parent header fixture. Cell count and source coverage must never substitute for exact case-sensitive header recognition and original-character ownership.

## Decision and limits

Defer this foundation model's synthetic fine-tuning. Relative to the rejected from-scratch development checkpoint, the starting recognizer offers little exact-line improvement and lacks rejection machinery; original-ink framing still accepts many wrong codes and damaged/multiline controls. Fine-tuning could change this, but current evidence does not support committing another synthetic adaptation with an expectation of production-level accuracy. The previous general-model adaptation also left high-confidence confusable errors and unsafe damage outputs, so its improvement is insufficient precedent.

No epochs, learning-rate trials, confidence-threshold tuning, dictionary prior, checkpoint selection or final inference occurred for this namespace. Sawarabi Gothic/Mincho remain downloaded-but-unrendered reserved final fonts for future frozen research; Sawarabi Mincho's earlier parent source-only use remains disclosed. A future experiment needs a new training/dev protocol and untouched final evaluation, source/crop contradiction probes, literal header controls and independent native full-document qualification with at most three body corrections and no manual structure/header repair.

There is no ONNX/CoreML export for this rejected suitability candidate. Existing general-model portable artifacts remain unchanged. A future export would need its own **Gray64**, dictionary and last-column-padding contract, not the existing BGR48 or BGR64 contract. SourceBox mapping would include the framed crop offset and original crop width/height: an interval endpoint `t` maps to model-column coordinate `t*inputWidth/timeSteps`, which must lie within `validWidth`; its source x is `cropLeft + column*cropWidth/validWidth`. This approximate CTC extent is not independent proof that complete original glyphs or cell membership are owned. Padding must be refused rather than clamped. iOS code/settings/remotes were not edited; no iPhone qualification is claimed.

Linux CPU2 baseline took 13.25 seconds for 512 crops with median model-only 22.97ms and peak RSS391.56MiB; framed took19.38 seconds including crop work with peak396.11MiB. Concurrent workloads and preprocessing differ, so these are resource observations, not comparative speed or native Windows/iPhone performance claims.

Evidence: `baseline-freeze.json`, `baseline-summary.json`, `baseline-unjudged.jsonl`, `framed-freeze.json`, `framed-summary.json`, `framed-unjudged.jsonl`, `development-diagnosis.json`. Source is under `tools/ocr-synthetic-training/ascii-foundation-ctc/`. Commit/push and production integration belong to the parent.

A later parent-requested fixed short-header check evaluated the consumed paired literals separately. It also failed suitability:26/42 exact and13 wrong research-accepted headers, including A1→Al in both fonts. See [the consumed-header report](consumed-headers/README.md). No additional training/export/final opening followed.
