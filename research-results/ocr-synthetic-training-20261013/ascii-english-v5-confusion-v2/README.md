# English V5 confusion continuation v2

A trained candidate passed the frozen development comparison. The architecture foundation is official PP-OCRv5 English full438. Actual initialization **and teacher** are the previously adapted active font-balanced epoch-4 checkpoint SHA `7313bee5dabf17998010c6f47844fe80649744b03c0366af05c627fc47314547`, matching active ONNX SHA `9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703`; the unadapted official weights are not this trial’s initializer or teacher.

This is a fictional synthetic crop experiment. It does not measure real timetable, complete-document, or native combined-route accuracy. At the original training handoff no product model was replaced or published, and iOS was not edited.

Subsequent application assessment: the [portable v2 artifact](../../../model-artifacts/ascii-english-v5-confusion-v2/README.md) is now published for research, but **not activated in the application**. The [actual manual-route comparison](../../ocr-model-routing-20261016/README.md) stayed292/384 exact while edits worsened106→123 and high-score errors24→36. Product default remains v1; no further training or checkpoint reselection follows this result. The crop findings and retained failures below remain unchanged.

| Comparison | Raw exact | Whole CER | Code CER | Natural CER | Correct/wrong accepted | Negative accepted |
| --- | --- | --- | --- | --- | --- | --- |
| New development active | 346/384 | 0.860% | 2.675% | 0.103% | 270/7 | 5/112 |
| New development epoch-1 | 357/384 | 0.681% | 2.006% | 0.026% | 332/7 | 9/112 |
| New development epoch-2 | 361/384 | 0.484% | 1.486% | 0.026% | 344/8 | 7/112 |

| Comparison | Raw exact | Whole CER | Code CER | Natural CER | Correct/wrong accepted | Negative accepted |
| --- | --- | --- | --- | --- | --- | --- |
| Independent fresh active | 360/384 | 0.463% | 0.731% | 0.154% | 291/13 | 11/112 |
| Independent fresh selected | 368/384 | 0.303% | 0.439% | 0.128% | 356/7 | 11/112 |

A read-only normalized-input audit found two shared all-white blank controls between train and development; no positive input overlaps. These controls are disclosed and development is not claimed independent final evidence. Fresh generation excludes every train/development PNG and normalized-input hash, including these blanks.

Development is consumed development. Fresh fonts/seed/literals are separate and are generated only after selection hash is fixed. Wrong accept zero is not required; CER, gains, regressions, natural-text retention, wrong accepts, negative categories, confidence/review counts and original-pixel padding refusal are reported separately. Uncalibrated confidence/reject scores do not prove source completeness. Japanese-primary byte identity is preserved; CJK negatives do not establish Japanese recognition quality.

Fresh raw improvements: 10; regressions: 2. Original active ONNX versus matching Torch decisions/intervals/padding: True across 496 rows. Portable-export usefulness: True. No further training/reselection follows fresh.

Correct raw lines requiring diagnostic review: development 76→17. Development paired gains/regressions are recorded in diagnosis.json. Literata development regresses raw79→78/96, whole CER1.488→1.637%, code errors12→15; its wrong accepts5→8. This subgroup regression is retained despite aggregate selection.

Fresh correct lines requiring review: 69→12. I/l/1/O/0-confusion wrong rows9→6, but accepted confusable wrong rows2→2. Fraunces fresh regresses raw90→89/96 and code errors2→5; SplineSansMono91→96, MartianMono93→93, Newsreader86→90. Fresh blank16 has three repeated normalized all-white pairs, no positive contradictions; old/train/dev/root-oracle literal/PNG/input overlaps are all zero (fresh-corpus-exclusion-audit.json).

Standard ONNX FP32 exported: `/workspace/scratch/ascii-english-v5-confusion-20261016-v2/portable-v2/model.onnx`, SHA `3271bfaa22d895a0d5d085346cfdd865c3eeb5a6542ae9c45f66fc48a254dc44`. Standard domains only, 12 dynamic batch/width shapes, and 496 consumed-dev Torch/ONNX text/interval/padding/accept decisions all agree: True. Selected pre-export Torch raw reproduces exactly. Numeric FP32 confidence differences are retained in the fidelity receipt; this is not a bitwise-identity claim. CoreML/iPhone inference was not attempted. At the original handoff, actual native combined-route and full-document assessment remained necessary. The completed routing comparison is linked above.

Training: 935.57 seconds; cache: 339.99 seconds; peak own RSS 2666.88 MiB; budget stop: False. CPU 2 threads, batch 2, two epochs. Active-checkpoint SHA `7313bee5dabf17998010c6f47844fe80649744b03c0366af05c627fc47314547`; selected SHA `99d7adc48be98da4d7038103cbc0b64b3a66c27905c20024ba37f17f58a8b7be`.

Initially registered training sources and protocol remain byte-identical to the training freeze. The initial fresh launcher hit a parent/sibling train.py import collision before freeze, image generation or model calls; explicit sibling-file import repaired that evaluation launcher, and its first failure log is retained. Original assets/checkpoints/cache/raw outputs are retained externally. Corpus, exclusion, initialization, contradiction and source audits are in the adjacent receipts. The extra-code-literal audit verifies no overlap with old-v1/old-v2/current-root code oracles before optimization. Standard model weights/images/cache remain outside Git.
