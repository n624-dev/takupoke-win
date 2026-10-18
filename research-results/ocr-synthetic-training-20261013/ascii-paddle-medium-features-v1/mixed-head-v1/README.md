# Medium fullhead mixed-line v1 — closed, original fallback

This bounded research experiment did not meet the prospective continuation gate. The fixed selection retained the original PP-OCRv6 medium model: every trained candidate accepted fewer correct positive lines. No candidate is qualified or active. Reserved Sawarabi Gothic/Mincho final fonts remain unopened; no portable trained-model export or whole-document production claim was made.

## Fixed result

Positive denominator is 768, negative denominator 192, Japanese retention denominator 256 (128 positive Japanese plus 128 separate retention lines). “Accepted” is an uncalibrated diagnostic: nonempty, original CTC minimum confidence >= .8, no emitted interval beyond validWidth, plus trained reject probability <= .05. Original fallback has no trained reject head. Padding counts cover all 1088 development lines and are refused without clamping.

| Candidate | Exact /768 | Correct accepted | Wrong accepted | Negative accepted /192 | Japanese exact /256 | Padding refusals |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| original | 434 | 358 | 157 | 30 | 250 | 30 |
| epoch-4 | 468 | 257 | 64 | 3 | 250 | 31 |
| epoch-8 | 472 | 294 | 71 | 4 | 250 | 34 |
| epoch-16 | 467 | 308 | 70 | 2 | 250 | 34 |
| epoch-24 | 470 | 313 | 74 | 3 | 250 | 32 |

All candidates preserved 40/40 empty blank outputs and all 250 originally correct Japanese retention lines. This is limited development evidence, not preservation on arbitrary Japanese documents. Epoch 24 still accepted three damaged interior-erasure lines; epoch 16 accepted two. Large clipping/multiline/noise controls were refused by trained candidates, but do not establish rejection of every damaged or incomplete source.

Epoch 24 mixed-code exact was 116/256 versus original 122; mixed-confusable exact 112/256 versus 67; arbitrary ASCII short 116/128 versus 119; Japanese 126/128 in both. Epoch 24 retained 74 wrong accepted positive lines. Raw improvement alone did not override the frozen selection or gate.

The gate requires a selected trained candidate, >=97% positive raw exact, >=90% correctly accepted, zero wrongly accepted positives/negatives, >=95% raw exact per development font, and zero original-correct Japanese retention regressions. It failed. Development fonts are now consumed and must not later be called unused final fonts.

## Method and provenance

Official PaddlePaddle PP-OCRv6 medium ONNX revision `50c7eacafc52fa7bcf4194e8cd08e46f8558504b`, SHA256 `9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba`, Apache-2.0 provenance from the parent pinned asset. Initialization/feature extraction fidelity is separately recorded in the parent namespace. Original CNN/SVTR features and all 18,710 CTC classes remained intact. Zero-initialized deltas updated only the 95 printable ASCII classifier rows (18,335 parameters), with a separate 193-parameter reject head. Blank/non-ASCII matrix and bias rows stayed original buffers. Full 18,710 softmax/dictionary were used for every inference; no class whitelist, lexicon, or dictionary narrowing.

Training-only exact loss compression retains all target classes and ASCII/blank, with exact logsumexp of unchanged other logits. It is valid here because frozen features and unchanged non-ASCII rows fix every omitted logit. It must not be reused unchanged for encoder adaptation: changed features change omitted logits and their conditional teacher distribution.

Protocol SHA256 `1906abc761277c0d6886819c749637421fd333bade5a81dfa7029ccb25133232` was fixed before corpus/cache/baseline/training. Train comprises 6144 independent positives plus 1024 negatives; development comprises 768 positives, 128 separate Japanese retention, 192 negatives. Train fonts Noto Sans/Serif, MPLUS1p, ZenKaku Gothic, BIZUDPMincho were previously consumed. Development fonts BIZUDGothic and KaiseiTokumin were pinned from official Google Fonts and reserved after the parent checked prior records. New nature phrases and arbitrary ASCII codes are independent of school/source-document strings. Exact prior literals are exclusion-only; train/development targets are mutually disjoint. Paired confusables use common font, rendering seed, Japanese prefix, baseline and relative height.

Whole-line all-RGB-nonwhite bounding box plus two original pixels retains all provided ink, Japanese prefix, internal gaps and relative glyph height. Native BGR48 center-bilinear, uint8 rounding, normalized-zero right padding, width 320..960 are unchanged. Inputs, cached features and cached logits are FP32 throughout. No crop whitening, CJK removal, glyph splitting or output repair. Full original input widths, valid widths, CTC intervals and source offsets are retained in raw records. Valid-source temporal-center masking applies only to reject pooling; original padding remains in recognition and teacher objectives.

## Saved-output diagnosis

O→0 remains frequent; l→1 improves partially. Mixed-line errors also include ASCII colon→fullwidth colon, removed spaces/colon and non-ASCII em dash→hyphen/en dash/ideographic one. Non-ASCII rows are fixed, so ASCII-row changes cannot alter the relative rank of two fixed non-ASCII classes on a frozen feature vector. The edit alignment is diagnostic, never an output correction or inference routing rule. Moving to encoder adaptation is a separate prospective experiment, not a reopened selection here.

## Separate provenance contradictions

After completed selection, the already consumed 64 alias/prefix-loss controls were run once, outside training and selection. Thirty-two pairs have byte-identical provided pixels but incompatible source interpretations. Original accepted both in 30/32 pairs (26/28 script aliases, 4/4 complete-prefix-loss); epoch 24 in 27/32 (23/28, 4/4). Refusing both also loses the valid counterpart. A crop-only reject classifier cannot establish script origin or a whole missing prefix. Independent source geometry/script/ink ownership proof is required. This is not heldout final or production credit.

## Runtime and audit

FP32 feature caching took 862.41 seconds; all 24 training epochs plus the four fixed development evaluations took 95.79 seconds; peak own-process RSS 1851.53 MiB. Linux CPU2 and concurrent parent workload: these are not isolated speed, Windows memory or iPhone inference claims. Original graph is standard ONNX opset 11; no full-network Torch/CoreML conversion was claimed.

Frozen source and raw hashes, FP32 cache file hashes, train/development disjointness, measured ink-removal controls, explicit UTF-8/LF I/O, final-directory absence, and the previously packaged portable ZIP's unchanged SHA were verified. Source/pixel/feature/weight assets remain in dedicated scratch directories; only source, text documentation and structured research records are intended for integration. iOS and production candidate activation were untouched.

Reproduction uses the dedicated training venv and external pinned original assets. `mixed_train.py` refuses overwriting a frozen run; `mixed_report.py` and `mixed_errors.py` inspect completed records only; `mixed_probes.py` refuses a repeated fixed probe. Paths and protocol receipts document these research-environment assumptions; this is not a portable production training CLI.
