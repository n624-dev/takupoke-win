# English V5 font-balanced-v1 closed development and ONNX handoff

Selected epoch4 meets the prospectively fixed practical research gates. This new officialHF-derived ASCII specialist improves codes on family-disjoint new developmentfonts. It is inactive, unqualified for wholeJapanese fields/full-documents, and no Sawarabi final was generated or consumed. Fourepochs only, no additional training/calibration after results.

| Same fixed newdev | OfficialHF original | Epoch2 | Selected epoch4 |
|---|---:|---:|---:|
| Whole CER /5672 characters | 3.843% | .899% | .617% |
| Code/look CER /1390 characters | 14.388% | 2.230% | 1.511% |
| Mono code CER /680 characters | 14.118% | 1.912% | 1.029% |
| Natural CER /3930 characters | .127% | .280% | .127% |
| Exact positives /384 | 271 | 340 | 355 |
| Exact code/look /160 | 62 | 135 | 145 |
| Exact mono code /80 | 26 | 69 | 74 |
| Exact natural /192 | 187 | 181 | 187 |
| Correct/wrong accepted positives | 248 /49 | 200 /15 | 285 /11 |
| Positive reviews required | 87 | 169 | 88 |
| Correct lowCTC rows | 22 | 129 | 69 |
| Negative accepted /112 | 16 | 11 | 13 |

Epoch4 improves93 previously wrong lines and regresses9; natural4improve/4regress despite unchanged aggregate. Code accepted104correct/2wrong out106, versus45/41 out86. Confidence .8/reject .05 is uncalibrateddiagnostic, unchanged afterresults; correct lowCTC reviews increased. Actual correction time/full-document burden is unmeasured. Highest wrongly accepted minimumCTC .996950. Errorzero is not required; older frozen zero-wrong/fidelity failures remain unchanged.

Safety limits: epoch4 clipped2/16 and interior_damage11/16 accepted, otherfive negativegroups0accept; blank16empty. These are independent negative kinds, not OCR accuracy on intactlines. All unsupportedCTCrightpadding refused. Oldconsumed unscored identicalpixel script/missingprefix controls18/32pairs both accepted (script14/prefix4); refusing both loses validcounterpart, so independent source/domain/completeness/ink ownership proof remains necessary. WholeJapanese fields cannot be represented by438, no oracle prefix cut or suffix-to-whole-field credit.

Training752 rows/dev496, train8/dev4new Latin families disjoint, everypositivegroup fonts equal/mono50percent/natural50percent. Fresh literals/seeds, allASCII95cmap coverage, previousliterals exclusion-only; no real schooldata/olddevfont moved to train. UnknownCJK negatives use consumedNoto fonts, not independent Japanese quality. FullofficialCNN/encoder/438head+reject121, BNrunningstats/config/eps unchanged; officialoriginalinitialweights, not old checkpoint extension. Wholeinkbbox2/BGR48/originalpixel-center FP32/resizeduint8round/internalgaps retained; noFP16. Observational font gaps use differing literals, not matched-text causal contrasts.

1313.387s training/cache238.084s/peak2732.234MiB CPU2/batch2, no budgetstop. Prior46handoff allbytes/SHA unchanged. All1248NPZ restoredinput/teacher/probability FP32 byte/dtype/shape hashes, checkpoints/BNfull438/source/UTF8LF audits pass. Measured alongside otherresearch, no isolatedWindows/iPhone latency claim.

StandardONNX opset17/FLOAT32,22,183,975bytes,customdomains none. Inputs x float32[B,3,48,W],valid_width int64[B]; outputs probabilities[B,T,438],reject_probability[B]. 12dynamic shapes batch1/2 widths320/321/481/500/640/960 alltext/interval/padding/decision match; fixeddev496/496allmatch. PriorselectedTorchraw includingconfidence/reject reproduced exactly with integerframe-center mask. ONNXdev maximumprobability1.776e-5/confidence1.496e-5/reject1.252e-6 differences, not bitwiseidentity. Separateexport103.475s/peak855.148MiB. Fullylocal/no training/update. Complete438dictionary blank0/space437, exactpreprocessing/CTCmax-run confidence/fullTpaddingrefusal/positionmap/provenance in portable-v1/metadata.json. Model,PNG,NPZ,SF stay outsideGit. CoreMLnotattempted/iPhoneunmeasured/iOSrepo untouched/noappswitch/no publicmodel/childpush.

Read-only metadata corrections retained with initialrecords: negative subgroup accepted field in frozen phase summaries was mistakenly overwritten by positive-onlycharacterization. OverallnegativeAccepted/raw/selection were correct; canonical groupcounts are in negative-group-summary-metadata-addendum.json and practical-error-review-diagnosis.json. Initialfontserialization audit said4.61.0; installed4.61.1 documented in environmentaddendum; all12staticfontbyte reproduction tests remain valid, no inference rerun. Frozenprotocol/usedsource/raw unchanged.

External package: /workspace/scratch/ascii-english-v5-font-balanced-20261014-v1/portable-v1 (model.onnx,dictionary.json,metadata.json,image_tensor.py,README.md,protocol.json). Selected SF checkpoint: siblingadaptation-v1/epoch-4.safetensors. Portable assessment is this newmodel vs its officialTorch checkpoint; old nativeONNX transplant identity is not claimed. Integration/reviewUX/full-document tests remain parent-owned.
