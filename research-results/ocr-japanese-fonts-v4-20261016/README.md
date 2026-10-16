# Japanese-font ASCII V4 fixed trial

Development selected **epoch-2**; fresh was opened once after its immutable checkpoint hash. **Original trial FAIL; active baseline fallback.** This is an independent synthetic crop study; no product default, Windows document qualification or iOS change is implied.

| Model | Exact /512 | Code exact /256 | Code edits | Whole CER | High CTC wrong | Wrong accepted | Neg accepted |
|---|---:|---:|---:|---:|---:|---:|---:|
| active-baseline | 432 | 193 | 70 | 1.3591% | 34 | 32 | 9 |
| epoch-1 | 445 | 198 | 64 | 1.1274% | 36 | 35 | 9 |
| epoch-2 | 448 | 202 | 59 | 1.0656% | 35 | 32 | 9 |
| epoch-3 | 442 | 195 | 67 | 1.1737% | 37 | 35 | 10 |
| epoch-4 | 441 | 194 | 67 | 1.1737% | 32 | 30 | 9 |

All 512 positive development rows, including physically aliased labels, participate in selection. The 112 negative controls retain separate counts. Eligibility requires code/whole CER improvement, no JP font edit regression, natural English retention and the fixed raw-confidence/false-accept/padding guards; no zero-error condition is used. Every epoch, complete raw CTC intervals/confidence/reject/source checks and every failure are retained.

The architecture/full438 dictionary come from pinned official PP-OCRv5 English. Actual initializer and teacher are font-balanced-v1 epoch4 (`7313bee5…`), rather than unadapted official, V2 or V3 weights. CNN weights and all batch-normalization statistics remained byte-identical; only encoder, full classifier and reject head were updated. All cached activations and teacher outputs are lossless FP32. Newdev original/cached records matched exactly before optimizer start.

Training JP families are disclosed consumed fonts, with new literals/images/normalized inputs. JP development Murecho/NewTegomin are separate; foundation pretraining exposure is unknown. Latin dev/fresh are consumed retention families with new literals. Root BIZUDPGothic/ZenMaruGothic/BIZUDPMincho are never loaded or rendered. The root oracle and legacy 21,201 hashes are used only for complete-string/component exclusion. No real-user document or derivative is used.

Post-selection train resubstitution code exact 754→827/960, edit errors 236→137. This diagnoses fit and receives no generalization credit. `alias-diagnostic.json` reports the all-row main denominator, alias subsets and actual-label upper bounds; no rows are removed.

Fixed fresh exact 432→438/512, code edits 75→79, gains 17, regressions 11. Fresh guards FAIL; no checkpoint or threshold was changed after fresh. Detailed font/retention/confidence/negative guards are preserved in `fresh-comparison.json`.

CPU2 trial: training 324.9s, teacher cache 324.4s, peak RSS 774.6MiB. Source/protocol/font/license/manifest/exclusion hashes are in registration/freeze receipts; raw failure ledger SHA256 `1ffb99d36924ad4117e1c168c8a9775d01accfa6e95c2c32c0dbbc16cafdf284`.
