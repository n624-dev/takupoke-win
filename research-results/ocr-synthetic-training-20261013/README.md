# Synthetic PP-OCRv6 small adaptation, 2026-10-13

A lightweight research candidate improves independent synthetic crop recognition.
It is **not qualified or activated**. Existing production models, confidence
floors, validators and iOS code remain unchanged.

| Independent final condition | Base | Selected epoch 1 |
|---|---:|---:|
| All single-line crops | 320/384 | 351/384 |
| Arbitrary O/0/I/l/1-rich codes | 65/96 | 80/96 |
| Arbitrary ASCII | 87/96 | 89/96 |
| Random Japanese character strings | 82/96 | 92/96 |
| Mixed Japanese/ASCII | 86/96 | 90/96 |
| Character error rate | 2.832% | 1.324% |
| Incorrect literals with minimum confidence >=0.8 | 41 | 23 |
| Additional untrained-CJK challenge | 37/64 | 41/64 |

The main final has 36 newly correct literals and five regressions. The supplemental
challenge has four improvements and no regressions. Zen Kaku Gothic New and
BIZ UDP Mincho were reserved until the selected checkpoint hash was fixed.
Train, development and final use independent seeds and disjoint strings; final
answers never select a checkpoint or alter an inference output. Neither set
contains school originals, school-specific values or consumed comparison strings.
These are synthetic 19–33px, mostly black/gray single-line crops; coloured ink,
real documents and the complete 18,710-class alphabet are not certified.

Development exact match was 149/192 for the base, 162 for epoch 1, and 161 for
epochs 2 and 3. The predeclared exact/CER/earliest rule selected epoch 1. Training
used 1,536 independently generated Noto Sans/Serif crops, a fixed backbone and
BatchNorm statistics, trainable SVTR/classifier and CTC + full-vocabulary teacher
KL. Three CPU epochs took 327.39 seconds under concurrent load, with peak process
RSS 1,409.35 MiB. This is a Linux research measurement, not a Windows requirement.

Safety limits remain material. Blank probes produce no text in either model
(0/16). Clipped probes still produce text in all 16 cases; outputs with minimum
confidence >=0.8 increase from 8 to 11. Multiline probes produce text in all 16
cases; minimum-confidence >=0.8 outputs increase from 2 to 3. Thus training does
not establish damage or multiline rejection and can increase certainty on
unsupported inputs. Six of 384 selected-model CTC intervals touch normalized
padding; the native SourceBox guard must reject these, without clamping.

The standard ONNX artifact is 21,176,440 bytes, SHA256
`796b2eb06ed399a6799120b008a78da344f54dbe5151461b12f9bc9a1d1ec146`.
Fresh eval-only Torch and ONNX match text and intervals in all 384 rows, plus
dynamic widths 320/481/640/960 with batch 2. Maximum full-distribution difference
is 0.00089833; maximum decoded-piece confidence difference is 0.000016451.
All 384 decisions at the unchanged 0.8 floor agree. The first post-export Torch
comparison was invalid because legacy export restored a train-mode wrapper onto
the child model. Its receipt is preserved. An explicitly eval wrapper and fresh
checkpoint reload fix the comparison; checkpoint selection and pre-export
accuracy are unchanged.

Fresh-process ONNX CPU inference peaks at 137.75 MiB for the base and 132.29 MiB for the selected model on
Linux x86_64, using two threads. Median crop inference is 16.06 ms under concurrent
CPU work. These figures exclude PDF rendering/detection/application memory and
do not establish isolated speed, complete-document latency or Windows memory.

The portable package includes standard ONNX (only the standard empty operator
domain, opset 17), Torch safetensors/config, the original 18,710-entry dictionary,
exact BGR48 preprocessing/greedy CTC reference, source/license hashes and an
optional CoreML ML Program. The CoreML package converts successfully on Linux,
targets iOS 16+, uses float32 and flexible width 320..960, and occupies
21,192,005 bytes. **No CoreML prediction was run on an iPhone or Apple runtime.**
Torch tracing matches the selected recognizer on four width probes; that is not
CoreML runtime fidelity. The iOS repository, settings and remote are untouched.

Base-model revision is
`PaddlePaddle/PP-OCRv6_small_rec_safetensors@fe049fb103f57443fe8840c54ed06b702f3c1de5`
(Apache-2.0 model-card metadata). Selected safetensors SHA256 is
`8286b4fc102d5a398ae9a535872cb5223fd5369171dcec03f2af57725d06c046`.
All five public fonts and their SIL OFL 1.1 texts are pinned to Google Fonts
revision `295d98a7a0c17c68f1341eaeea354e7960ea70d3`.

Full-document qualification is still required: new immutable pixels and arbitrary
fictional values, automatic headers/structure, original glyph positions and cell
membership, deterministic validation, no gold-driven candidate choice or repair,
and at most three real body corrections per complete document. Confidence alone
must not accept clipped or multiline text. Keep the production baseline while
these requirements remain unmet.

Artifacts are outside Git at `/workspace/scratch/ocr-finetune-20261013/`:
`portable-package/`, `selected.onnx`, `selected.mlpackage`, `epoch-1/`, immutable
pixel manifests and logs. Reproduction source and frozen protocols are in
`tools/ocr-synthetic-training/`. This worker does not commit or push.
