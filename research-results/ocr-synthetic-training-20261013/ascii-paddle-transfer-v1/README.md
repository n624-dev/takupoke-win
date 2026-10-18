# PP-OCRv6 small ASCII transfer: bounded development result

**Learning improved this recognizer, but the candidate remains unqualified and inactive. Final evaluation and portable exports are deferred.** The fixed run selected epoch8. Independent development exactness improved405→472/512, correct research-accepted lines368→432, and wrong accepted lines46→23. Twenty-three wrong lines still passed the unchanged diagnostic, including19 with I/l/1 or O/0 mismatches; the highest wrong minimum-piece confidence was0.997401. The previously registered continuation gate failed. Sawarabi final fonts remain unrendered and uninferred.

## Frozen method and data

Protocol semantic SHA256 `306b85beb9904c7695cfa0261ee6edfc0d091ae7b563ce33cdf9b1efa6291054`. Official unadapted PP-OCRv6 small safetensors revision `fe049fb103f57443fe8840c54ed06b702f3c1de5`, SHA256 `f65a332afe5aa663f0b9d5706f4ae8457b5b4058a842d5c1eb22df505c27d642`. The original CNN is frozen, and all BN statistics remain fixed in eval mode. The pretrained SVTR encoder trains at low LR1e-4; the classifier and separate reject head train at3e-4. Blank and95 printableASCII classifier rows/biases are copied exactly from the original18,710 dictionary. New dictionary is blank0, U32..126 indices1..95 (space1), UNK96. UNK mean-row initialization does not preserve excluded logit/probability mass. Total model3,023,186parameters.

Loss is CTC +0.5rejectBCE +0.05 frozen-teacher KL. The teacher retains the original full-vocabulary head; its excluded-class probability mass is summed exactly into the projected UNK class. Negatives target UNK and reject, never a lost full text. Fixed epochs/evaluations/budget/selection include the original model as a fallback. Simple dictionary reduction is measured separately before any updates and is never credited as training.

Train4096 positives and1024 negatives; development512 positives and192 negatives, with independent seeds/literals. Positive categories are balanced arbitrary codes, paired case changes, paired I/l/1 and O/0 changes, and arbitrary printableASCII. Case/confusable pairs share font/rendering seed. All95 printableASCII characters occur at least71 times in positive training. There are no class-name lists or whitelist decoding. Train fonts are previously consumed NotoSansJP/NotoSerifJP/MPLUS1p/ZenKakuGothicNew/BIZUDPMincho. Dev KiwiMaru/HinaMincho were confirmed unused by the parent; pinned public font/OFL hashes are retained. ZenMaru was already used in earlier research and is downloaded but unused here; it is not described as an unused font. Reserved Sawarabi Mincho's earlier source-only exposure remains disclosed.

Prior literals were collected for exclusion only from the consumed vision oracle, prior synthetic manifests, parent fresh physical-frame oracle and existing research JSON/JSONL. All157 snapshot files were unchanged during verification; no training/dev target intersects the excluded strings, and train/dev are mutually disjoint. These prior strings and pixels were not training examples. No school originals were read.

Original RGB!=white255 defines one whole-line bbox expanded by2 original pixels. All provided-original nonwhite samples, shared baseline, relative character heights and internal gaps remain intact. Production BGR48 center-bilinear/uint8-round, width320..960 and normalized-zero padding are retained. No whitening, threshold, per-glyph resize, oracle framing choice or text repair occurs. The native preprocessing function initially produces FP32, but this bounded run stores normalized train/dev tensors as FP16 to reduce RAM, then converts them back to FP32 for every baseline and trained evaluation. This quantizes normalized input values: all candidates receive the same quantized tensors, so relative development comparisons remain applicable to that condition, but they do not establish byte-identical production FP32 inputs or native Windows quality. Original RGB/crop geometry and uint8 interpolation remain unchanged. This precision limitation was identified in post-run source review; frozen protocol, raw predictions and selection are not rewritten or rerun. Medium fidelity and future medium caches use FP32 throughout. Synthetic clipping removes20–40% of original ink height before framing; all202 clipped train+dev rows have measured ink loss. Keeping all remaining crop ink does not recover the earlier lost ink. Separate paired dev native-input comparison found no exactly identical tensors; this does not prove recoverability.

## Fixed development comparison

| Metric | Full18710 original | Reduced untrained | Epoch4 | Selected epoch8 |
|---|---:|---:|---:|---:|
| Positive exact /512 |405|409|455|472|
| CER |3.471%|3.356%|2.667%|1.172%|
| Correct accepted |368|371|385|432|
| Incorrect accepted |46|50|26|23|
| Negative accepted /192 |14|35|0|0|
| Unsupported padding rows /704 |42|42|13|13|
| Code exact /128 |122|122|111|119|
| Case exact /128 |123|123|120|126|
| Confusable exact /128 |57|57|108|108|
| Printable exact /128 |103|107|116|119|

The reduced-only model increases unsafe acceptance, notably mixed-CJK20/32. Its conditional probability rise is not quality improvement. Epoch8 improves74 rows and regresses7 against the full original; compared with reduced untrained it improves70 and regresses7. Confusable raw exactness plateaus at108/128 between4 and8. General code remains below the original122/128.

Dev font results differ: HinaMincho213→251/256 exact, correct accepted195→224, wrong23→2; KiwiMaru192→221/256 exact, correct accepted173→208, wrong23→21. This does not establish unseen-font robustness.

Research acceptance is unchanged: nonempty printableASCII/noUNK, emitted minimum confidence>=0.8, learned reject probability<=0.05, and all CTC intervals inside validWidth. Baselines without trained rejection use the same original confidence/domain/padding criteria. These are uncalibrated diagnostics, not activation policy. The13 selected padding failures are positive code2/confusable1/printable10;12 are textually correct. They remain refused; no interval clamping is performed.

All six negative groups have32 dev rows. Selected acceptance is0 for CJK, mixedCJK, clipped, multiline, noise and blank. Three clipped rows still produce ASCII text but are refused. All32 blank inputs produce the trained UNK output, not an empty string; ASCII hallucinations and acceptance are0. This is not evidence of successful empty decoding. Large synthetic crop cuts often contact the provided canvas border, so these controls cannot establish rejection of interior erasure, tiny damage or every unknown script.

## Source-proof contradictions and continuation decision

Before their inference, separate unscored controls prove28 Latin/Cyrillic alias pairs and4 complete-prefix-loss/complete-tail pairs have exactly the same framed pixels. They are never training targets, model-selection metrics or final credit. Every fixed model gives the same output/acceptance to both members of each pair. Selected epoch8 accepts24 alias pairs in both conflicting script interpretations and all4 prefix-loss pairs in both complete/incomplete source interpretations. Rejecting both members of the other4 pairs also loses valid ASCII. A crop-only reject head cannot solve these contradictions; independent complete-source/script evidence remains required.

The continuation gate was registered at epoch2, before any trained dev checkpoint: selected trained, >=97% raw exact, >=90% correct accepted, zero wrong/negative accepted, and >=95% exact per dev font. Selected epoch8 reaches92.19% raw exact and84.38% correct accepted, still23 wrong, with KiwiMaru86.33% exact. It fails the gate. We keep final untouched and defer ONNX/CoreML packaging rather than treating relative improvement as production eligibility. This research continuation gate itself is not full-document qualification.

The30minute training budget expired at1800.97 seconds after9 complete epochs and part of epoch10. Only predeclared completed evaluation checkpoints4 and8 plus original were eligible; partial10 was not selected or saved as a candidate. Peak processRSS2635.67MiB, CPU2; concurrent Linux work means no nativeWindows/iPhone speed or total-app memory claim. Selected safetensors12,205,624bytes SHA256 `490c9ddc53bde0416463ebe822cb65178a66188604690b4c7d7b19f5e0bc7dff`, outside Git at `/workspace/scratch/ascii-paddle-transfer-20261013-v1/epoch-8.safetensors`.

A stronger next foundation worth investigating is the official PP-OCRv6 medium model: earlier consumed headers were19/21 Sans and21/21 Serif, and its standardONNX is about76.55MB. This is a proposal, not medium-transfer training or qualification. Current transformers has no medium recognizer class. A next separate experiment should first extract the original CNN/SVTR feature output and exact CTC matrix/bias from its pinned official graph, reproduce original full logits/probabilities/text/intervals without gold, and confirm portable standard operations. Frozen-feature transfer could then be assessed with copied ASCII rows, while preserving excluded probability mass instead of treating renormalization as accuracy. Parent full-document medium comparison remains independent.

## Evidence and limits

`freeze.json`, `exclusion-receipt.json`, `corpus-receipt.json`, `corpus-verification.json`, `font-receipt.json`, `initialization.json`, per-candidate unjudged raw/summary, `selection.json`, `development-diagnosis.json`, registered continuation gate, source-proof raw/summary, and `verification.json` preserve provenance. Source under `tools/ocr-synthetic-training/ascii-paddle-transfer/`. Frozen training source and every candidate raw hash, UTF8/LF I/O, disjointness, actual clipping, prospective registration and previous portable ZIP integrity were verified. Python compile succeeded.

No final data or production full-document evaluation was opened, no output correction occurred, no candidate was activated, and no ONNX/CoreML export was produced. Existing general-model portable artifacts remain byte-identical. iOS was not edited. Child commit/push was not performed; parent owns integration. Full-document literal headers, original glyph/cell ownership, <=3 actual body corrections, native Windows and device eligibility remain unresolved; the overall production goal remains unmet.
