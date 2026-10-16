# Height/local-neck adaptation v1 — closed, original fallback

All eight registered epochs completed. Increased raw agreement did not meet continuation/adoption criteria: every trained candidate accepted fewer correct positives than the original. Original PP-OCRv6 medium remained selected, unqualified research only. Sawarabi final fonts were not rendered or inferred; no trained export or whole-document qualification was attempted.

## Fixed result

Positive512, negative192, Japanese retention192 (64positiveJP+128separateJP). Acceptance is an uncalibrated research diagnostic: nonempty, original complete18710CTCminimumconfidence>=.8, nointervalbeyondvalidWidth; trained additionallyrejectprobability<=.05. No clamp/outputrepair. Originalfallback has no trainedrejectgate.

| Candidate | Exact /512 | Correct accepted | Wrong accepted | Negative accepted /192 | Japanese exact /192 | Original-correct JP regressions | Padding refusals |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| original | 277 | 232 | 117 | 36 | 192 | 0 | 26 |
| epoch-2 | 353 | 159 | 33 | 11 | 192 | 0 | 29 |
| epoch-4 | 359 | 168 | 27 | 6 | 192 | 0 | 31 |
| epoch-8 | 366 | 208 | 42 | 11 | 192 | 0 | 28 |

All192Japanese retention lines and40emptyblank outputs were preserved in all candidates. Epoch8 still accepted11interior-erasure negatives; clipping/multiline/noise/blank accepted0. This is limited control evidence, not rejection of arbitrary damaged sources. Maximum wrongly accepted minimumconfidence was.999836. Prospective selectedtrained/raw>=97%/correctaccepted>=90%/wrong0/negative0/perfont>=95%/zerooriginal-correctJPregressions gate failed. Component agreement never qualifies or activates a model.

Epoch8 raw improvements100/regressions11. KosugiMaru146/304→184/304, ZenAntique131/208→182/208; neither reaches the perfont gate. Correctaccepted232→208, despite wrongaccepted117→42. Sourceafterselection contrasts are kept separate from these metrics.

## Protocol and data

ProtocolSHA256 `b42f2f952fb1622968b46ad2d60446310c672b6be8ec15176e51d954d04b79f7`, sources/assets/corpus fixed before caching/baseline/optimizer. Fresh nature phrases/arbitraryASCII/code/case/I-l-1/O-0, train2048positive+512negative, dev512positive+128JP-retention+192negative. New train/dev targets disjoint from each other and prior literals (including oldhead/SVTR manifests and mainfictional oracles, exclusion-only). No school originals/subjects/names/rooms were used as examples, no classwhitelist/oracle outputrouting/repair.

Training NotoSans/Serif plus newly pinned Japanese monospaceMPLUS1Code400/BIZUDMincho. MPLUS/BIZbroaderproject lineage had prior exposure; entirely unseen fontlineage is not claimed. Newdev KosugiMaru/ZenAntique were reserved after ownrecord search and parent notification; parentKosugi-Regular had source-only use, distinct face/file but sharedproject. Sawarabi fonts remain finalreserved.

The protocol intended50:50 mono/proportional positivegroups. A postrun source audit found inherited paired/unpairedfont j-index assignment actually emits57.8125%mono training (1184/2048),59.375%mono development (304/512). `post-run-font-balance-audit.json` records exact counts/cause. Frozenprotocol/manifests/raw/selection were not resampled or rewritten; identical pixels were used for all candidates, perfont outcomes remain separate. This is an implementation limitation, not an exact50:50 experiment. A future generator must balance/shuffle pair units explicitly before freezing.

FontCmapcoverage is checked before rendering: MPLUS1Code lacksfullwidthcolon andKosugiMaru lacksemdash; unsupported literal/font combinations reroll, without fallback/tofu target or inferred output. All acceptedliterals have their glyphs; full95ASCIIcoverage remains. Source-only retrospective coverage audit found0missingglyph literalrows in the older closedmixedhead/SVTRruns, whose raw/selection were untouched.

Original ink-nonwhitebbox+2sourcepixels, baseline/relativeglyphheight/internalgap/Japaneseprefix are preserved; no CJK/glyph裁断 or positivewhitening. NativeBGR48 centerbilinear+uint8round, FP32normalization, normalizedzero rightpadding width320..960. Parentminimum160padding is a separate condition. Exact prepool-width buckets preserve oddcolumns and add no temporalpadding/attentionmask. Original floorAvgPool and groupedcorrection outputT are used for source time→pixel mapping; emitted paddingintervals are refused, neverclamped.

## Model and objectives

OriginalCNNprefix, original twoSVTRblocks, BNmean/variance/affine and full18710matrix+bias/blank0/dictionary are frozen. Poolcorrection4608+neckConv296256+ASCIIdelta18335+reject193 =319392trainable. Native initialization provenance/fidelity is separately recorded at the namespace root. Before optimizer, all832newdev original/reconstructed text/CTCinterval/floor decisions agreed; featuremax5.4359e-5/logit2.0599e-4/prob2.8938e-5. Floatingbyte arithmetic equality is not claimed.

Complete18710CTC and complete18710teacherKL, originalfeatureMSE andrejectBCE. No staticOTHER loss compression; encoder features change omittedclass probabilities. Frozenbatchnorm/SVTRtensor invariance was checked against originalcheckpoint for everycandidate. Source/provided/nativeFP32input collisions with incompatible labels are audited beforeoptimizer; this runfound0, CTCframefeasibility verified. Newdev inputcollisioncount0 is not proof of learnability or arbitrarysourceidentity.

## Separate source-proof controls

Postselection only,64previously consumed same-pixel scriptalias/whole-prefix-loss controls. Original acceptedboth incompatible interpretations30/32pairs; epoch2 15/32,epoch4 23/32,epoch8 26/32. Refusing both loses the legitimate counterpart too. Crop-only classification cannot establish identical-pixel scriptorigin or wholemissingprefix; independent source/ink/cellownership proof remains required. These probes are not targets, selection, unused final or productioncredit.

## Diagnosis and continuation

O→0 editcount134→44, but reverse0→O rises54; l→1 is replaced substantially by l→I (50→121). Colon/spaceerrors improve, arbitrarycode accuracy and damagedsourceacceptance still fail. Monospace coverage and earlyheight/localneck adaptation show some effect, yet trade one confusion for another and do not reach deployment requirements. Continuing the same fixed condition is paused; a stronger Japanese foundation under the parent's independent comparison is a better next decision point. This is not proof that all further training is impossible, nor closure of the productionaccuracy goal.

## Runtime and audit

Traincache355.51s/1.997GiB, devcache120.16s/.650GiB; compressedratios.9050/.9057 with exactrestoredFP32hashes. Eightepochs+fixeddev2/4/8 took860.59s, peakownRSS2123.79MiB, CPU2/concurrentLinux, no isolated/nativeWindows/iPhoneperformance claim. All plannedepochs completed within30min/4GiBRSS/3GiBownaddition/2GiBfree reserve.

Source/raw/checkpoint/cache hashes, eachlosslessNPZ restored dtype/shape/bytes, fixedBN/SVTR tensors, disjointliterals/glyphcoverage/actualdamageinkloss, UTF8/LF/compile, priorportableZIPunchanged andfinaldirectoryabsence verified. Weights/pixels/maps/internalpolicy remain outside Git; source/docs/structuredrecords only for integration. iOS/productionactivation unchanged; no childcommit/push.
