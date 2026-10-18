# Terminal SVTR adaptation v1 — closed, original fallback

All eight scheduled epochs completed. The fixed selection retained original PP-OCRv6 medium because every trained candidate accepted fewer correct positive lines. This candidate is unqualified and inactive. The final-opening gate failed; Sawarabi Gothic/Mincho remain unopened, and no trained portable export/native Windows/iPhone or whole-document qualification was attempted.

## Fixed result

Positive denominator768, negative192, Japanese retention256. Acceptance is an uncalibrated diagnostic: nonempty, original full18710 CTC minimumconfidence >=.8, no emitted interval beyond validWidth, and for trained candidates reject probability <=.05. No output clamp/repair. Original fallback has no trained reject gate.

| Candidate | Exact /768 | Correct accepted | Wrong accepted | Negative accepted /192 | Japanese exact /256 | Original-correct Japanese regressions | Padding refusals |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| original | 418 | 369 | 152 | 37 | 254 | 0 | 28 |
| epoch-2 | 469 | 327 | 93 | 9 | 255 | 0 | 30 |
| epoch-4 | 470 | 336 | 111 | 12 | 255 | 0 | 31 |
| epoch-8 | 480 | 327 | 103 | 8 | 255 | 0 | 31 |

All candidates preserved40 empty blank outputs. Epoch8 still accepted8 interior-erasure negatives; clipped, multiline, noise and blank accepted0. This limited damaged-control result does not establish arbitrary damage/incomplete-source rejection. The epoch8 maximum wrongly accepted minimumconfidence was.999642. Original254 correct Japanese lines never regressed; trained255/256 is limited new-development evidence, not arbitrary Japanese preservation.

Epoch8 raw exact improved81 lines and regressed19 versus original. Font outcomes diverged: BIZUDGothic218/448→207, KaiseiTokumin200/320→273. Original-correct acceptance was369/768 versus trained327, so the original remained selected. Increased raw accuracy did not override the selection or safety gates.

The prospective continuation gate requires a selected trained candidate, >=97% raw exact, >=90% correct accepted, zero wrongly accepted positives/negatives, >=95% exact per development font, and zero original-correct Japanese retention regressions. Passing only permits a separately frozen final/portable research stage; it never qualifies a whole document by component agreement. Here the gate failed.

## Protocol and model

Protocol SHA256 `9ffc2c934300a005ab16903233441ef9fa43f91057b9281c7018b70b4f1768c2` was frozen before corpus/cache/baseline/training. Fresh nature phrases/random arbitrary codes, case/I-l-1/O-0 and Japanese, train4096positive+1024negative, newdev768positive+128extraJapanese retention+192negative. Prior dev/final/main-document strings, including the closed head-v1 training manifests, were exclusion-only. New train/development targets are mutually disjoint; no school originals, subjects, names or classrooms are examples. Development uses new literal/render seeds on BIZUDGothic/KaiseiTokumin font families consumed in the closed head stage. It is not an unused-font final.

Original pinned medium SHA256 `9c09abf0957f7968c7586464b7397b84ad2387a0497a351af40e9acc71b673ba`, official revision `50c7eacafc52fa7bcf4194e8cd08e46f8558504b`. Original CNN and local convolutional neck, full18710 classifier matrix/bias and dictionary stay intact. LowLR terminal SVTR890112parameters plus ASCII95 zero-initialized delta18335 and reject193 total908640 trainableparameters. The adapted encoder changes all class logits even though non-ASCII matrix rows remain fixed; fullteacherKL/feature retention and explicit Japanese regression checks protect the original behavior empirically, not by invariance.

Corrected initialization is in `../corrected-epsilon-v2`: exact original epsilon values (first four norm1e-5/final1e-6), preserved first-attempt failure, fixed48+8dynamic fidelity before training. Further before-optimizer newdev1088 comparison had identical original/reconstructed text/CTC intervals/floor decisions, feature maximum4.3869e-5/logit7.6294e-5/probability8.8215e-6. These are explicit float tolerances, not byte-equal Torch arithmetic or quality credit.

Whole-line RGB-nonwhite bbox plus two original pixels, common baseline/relative heights/internal gaps/Japanese prefix and all provided ink are retained. Native BGR48 original-center bilinear/uint8round/normalized-zero padding width320..960; input/context/shortcut/original-final-teacherfeature caches are FP32 throughout. No CJK or glyph clipping in positive preprocessing. Separate negative controls record actual ink loss. Exact native-T buckets introduce no artificial batch sequence padding or attention mask. Valid-source temporal-center mask is only for reject pooling; recognition/teacher objectives keep native padding. Parent minimum-width160 padding research is a different condition.

Training uses complete18710 CTC and complete18710 teacherKL plus original-feature MSE and reject BCE. The old frozen-feature staticOTHER/logit compression is not used: with an adapted encoder, omitted logits and their conditional distributions change. Eightepochs/B16/CPU2/fixeddev2,4,8/30mintraining and eachcache30min/ownRSS4GiB/minfree2GiB/additionaldisk1.25GiB were registered; no partial epoch became a candidate.

## Impossibility and source proof

Preoptimizer exact provided-PNG/native-FP32-input collision audit found3 natural whole-line pairs with differing literal targets (6rows). All6 were excluded from every optimizer update without guessed CTC targets or false damage labels; frozen manifests/scores were not repaired. Training optimizer rows therefore5114. Newdev collision count0 for provided/native inputs and exact CNNcontext+shortcut/original-finalfeature tensors. A lack of exact collisions does not prove learnability or robustness.

After completed selection only,64 previously consumed alias/full-prefix-loss controls were run once. Original accepted both incompatible source interpretations in30/32pairs; every trained checkpoint in32/32. Refusing both would also lose the valid counterpart. Crop-only prediction cannot establish identical-pixel script origin or whole missing-prefix geometry. Independent source/ink/cell ownership proof remains required. These are unscored controls, not training, selection, unused final or production credit.

## Diagnosis and next decision

Saved-output edit diagnosis shows l→1 falling81→21, but O→0 rising121→136 and remaining unchanged across evaluated epochs. Space/colon and non-ASCII dash errors remain. All five training fonts have varying ASCII advances (42..76 distinct); BIZUDGothic has one fixed ASCII advance, whereas KaiseiTokumin O/zero advances differ820/590. Different glyph outlines rule out assuming identical font vectors, but do not prove that low-resolution raster or CNN distinctions are sufficient. A proportional-width shortcut/domain-coverage gap is plausible, not established causality.

Extending this same late-SVTR run has weak value: epoch2/4/8 plateau on raw agreement, wrong accepts remain, the monospaced font regresses, and Japanese/blank preservation alone does not satisfy adoption. Further training is paused. A possible separate bounded experiment would add independently sourced Japanese monospace training coverage and investigate whether earlier CNN-height/local-neck information supports shape distinctions without a width cue. It must first establish original-logit fidelity, new train/dev/selection/negatives and a separate protocol; no existing final may be reused as new final, no devfont is silently moved to training. This proposal is not implemented or activated here.

## Runtime and audit

FP32 caching590.99s; eight training epochs plus fixed evaluations1186.04s; peak ownRSS2767.76MiB. Concurrent Linux CPU2, not isolated speed, Windows memory measurement or iPhone execution. Three-map cache hashes, raw hashes, immutable sources, train/dev/prior-literal disjointness, actual damage ink loss, initialization parity, UTF-8/LF I/O, final-directory absence and prior portableZIP unchanged SHA were checked. All checkpoints/pixels/maps remain outside Git; only source/docs/structured records are intended for integration. iOS and production activation remain untouched.
