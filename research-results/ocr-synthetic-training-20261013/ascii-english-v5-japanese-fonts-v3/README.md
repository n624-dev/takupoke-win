# Japanese-font ASCII v3: original trial failed

The fixed three-epoch trial retained active **font-balanced-v1**. None of epochs 1–3 passed the registered development gate. The original fresh set was never generated or recognized. A separately requested epoch1 ONNX is an **exploratory, failed-gate artifact** for consumed native routes; it does not change this selection or qualify a product model.

The actual initializer **and teacher** were font-balanced-v1 epoch4, SHA256 `7313bee5dabf17998010c6f47844fe80649744b03c0366af05c627fc47314547`, corresponding to active ONNX `9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703`. The foundation is the pinned official HF PP-OCRv5 English full438 architecture, revision `e4abce2bbd335f6c3eda0507b7f8e8abd5af3419`. This was not initialization from untouched official weights; rejected Latin-font v2 weights were not used. Japanese primary and iOS were untouched.

## Fixed development result

384 positives comprise 192 arbitrary JP-font ASCII codes and 192 Latin natural-English/printable retention lines; 112 separate controls cover clipping, internal damage, multiline, noise, white, CJK and mixed-script input. Confidence .8, reject .05, full438 CTC, source intervals and padding refusal were unchanged and remain uncalibrated.

| Condition | Exact /384 | JP code exact /192 | JP code edits | Whole CER | Wrong accepted | Negative accepted |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Active v1 | 304 | 124 | 79 | 1.945% | 34 | 11 |
| Epoch 1 | 312 | 128 | 73 | 1.732% | 35 | 12 |
| Epoch 2 | 306 | 124 | 82 | 1.988% | 46 | 11 |
| Epoch 3 | 314 | 130 | 69 | 1.646% | 40 | 14 |

Epoch1's relative code-CER reduction was 7.6%, below the predeclared 10%. Epoch2 regressed. Epoch3 reduced code edits 12.7%, but wrong acceptance increased by six and negative acceptance by three, exceeding the registered guards. No failed checkpoint was substituted into the original selection. Baseline fallback is recorded in `selection.json`.

Epoch3 development had 24 gains and 14 regressions; ZenOldMincho exact fell 74→68/96 while YuseiMagic rose 50→62/96. Natural English rose 156→157/160. Raw high-CTC wrong predictions increased 35→43 overall and 25→37 on codes. Code l→I edits fell 23→9, but O→0 rose 23→31 and 1→l rose 14→24. These regressions matter even though whole CER improved. Negative false acceptance was clipped2/internal9→clipped2/internal12; white/CJK/mixed/multiline/noise acceptance was zero in these limited controls. Confidence/reject cannot prove source completeness or safe adoption.

## Data, exclusions and reproducibility

Train1280+112: SawarabiGothic/Mincho, KleeOne and KaiseiTokumin code glyphs; IBM Plex Mono, Inconsolata, Crimson Text and Alegreya English retention. These are consumed research fonts, not holdouts. New development code families were ZenOldMincho/YuseiMagic. Reserved ShipporiAntique/ZenAntiqueSoft fresh fonts were downloaded only, never rendered or recognized. Foundation pretraining font exposure is unknown.

Codes use general upper/lower/digit/separator strings, short lengths and randomly positioned Il1O0; no fixed school-room prefix or two-digit grammar. Whole ink bbox+2, baseline/relative height/internal spacing, FP32 BGR48 original-pixel sampler, valid width and CTC geometry are explicit. Japanese prefixes are not generation targets: the product must separately preserve and validate the original Japanese prefix/source.

Old public synthetic strings/PNG/input hashes were exclusions only. Root BGRA original and bbox+2 normalized conditions6176 were excluded. The supplemental audit normalized 37,776 old PNG rows from29 manifests, found no missing images and no nonwhite old/new or within-new tensor collisions. Identical white tensors are intentional physical controls and receive no independence credit. A deduplicated corpus does not prove that every counterfactual character or source corruption is distinguishable.

Training used CPU2threads, batch2, fixed3epochs, v1 teacher KL.8/featureMSE.02/rejectBCE.5 and frozen BN statistics. All official CNN/encoder/classifier weights were trainable. Training2087.99s/cache340.60s, peak3036.4MiB; concurrent CPU load prevents interpreting this as a platform benchmark. Initial model feature/probability self-consistency passed. Core train/data/model/adapter/fresh/protocol bytes remained frozen. Later diagnostic/export scripts have their own receipts. One export-only draft metadata correction is explicitly recorded in `final-verification.json`.

## Closed diagnostics, without reselection

The last epoch3 was fixed for failure diagnosis; it was not promoted. Train resubstitution improved code465→572/640, edits208→74, and whole1083→1211/1280. Train I→l edits nevertheless rose42→63 while l→I fell66→8. I267/l246 target counts do not show a simple surplus of l labels, so no single cause is established.

The registered mild-shear64 positive probe used consumed training fonts and new strings, expanded canvases and ±6/±10 degrees. It improved exact50→57, CER4.50→2.12%, wrong acceptance4→2. This is robustness monitoring, not fresh or full-document qualification.

Source/normalized-input geometry tested six train/dev families across360 pairs each, multiple sizes/densities and surrounding glyphs. SawarabiGothic had17 exact normalized aliases: seven already identical in the source raster and ten introduced by external bbox framing; zero additional aliases arose in48px resize after framing. Seven aliases had surrounding glyphs. Other five tested families had zero aliases in these conditions. Advance, glyph bounds, original canvas differences, crop offsets, valid widths and baselines are recorded. Isolated glyph equality does not imply all whole codes are indistinguishable; these limited probes do not establish impossibility. They also do not justify treating higher input resolution as the primary remedy.

## Separate exploratory epoch1 export

At the parent's explicit request, fixed epoch1 was exported for old, consumed native-route exploration despite original gate FAIL. Standard FP32 ONNXopset17, full438 dictionary, x/int64 valid_width and uncalibrated reject output were preserved. All496 consumed development rows matched Torch text/CTC intervals/padding/acceptance; original Torch raw was reproduced exactly with its original two-thread condition. Twelve dynamic batch/width shapes were checked. Max probability difference1.60e-5 and reject1.89e-6 were retained.

The first export tried Torch1thread and failed strict raw numeric reproduction; all files/log/source are preserved under `exploratory-epoch1-v3-failed-thread1`. Returning to original Torch2threads resolved the strict gate without changing weights or thresholds. No fresh result or optimization was repeated.

Valid ONNX: SHA256 `95bab179e0d6cc75269419078d68696cd4679661f94f6128b858b7a074fe2971`,22,183,975bytes. The external33-file/44,857,459byte bundle includes ONNX, adapter checkpoint, full438 dictionary, preprocessing/CTC/source contract, foundation provenance and model/font licenses. It is under `/workspace/scratch/ascii-english-v5-japanese-fonts-v3/exploratory-epoch1-v3/`; package hashes and fidelity receipts are in the corresponding research subfolder. Linux fidelity is not native Windows speed/full-document qualification, CoreML/iPhone execution or default activation. Parent composite/native decisions remain separate.

A v4 draft proposes full ambiguity-unit exclusion, broader Japanese font styles and frozen-CNN encoder/head adaptation with new development families. It is a hypothesis supported by the fit/transfer gap, not a proven fix. Its optimizer is held while the parent tests the exploratory composite on all required documents/pages. Fresh remains unopened. No child commit/push, internal-document publication or iOS edit occurred.
