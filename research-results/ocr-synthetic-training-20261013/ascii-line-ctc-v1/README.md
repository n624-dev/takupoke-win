# From-scratch ASCII line CTC: rejected at development

The fixed 24-epoch CPU experiment is closed without opening its reserved final.
The 217,682-parameter network preserved whole-line baselines and vertical features,
but failed to generalize to Kosugi, a font not previously supplied to a recognizer.

| Fixed development checkpoint | Exact /384 | Correct accepted | Incorrect accepted |
|---|---:|---:|---:|
| Epoch 8 | 5 | 0 | 0 |
| Epoch 16 | 53 | 15 | 42 |
| Epoch 24 | 61 | 29 | 57 |

All 128 negative development probes were rejected by the predeclared combined
CTC/reject/source-support policy. This is insufficient: 57 incorrect positive
literals were accepted, while only 29 correct literals were accepted. A model that
rejects negatives but reads legitimate unfamiliar fonts incorrectly is unusable.
The minimum-confidence and reject thresholds were not adjusted after seeing this.

Training used 4,096 new printable-ASCII lines and 1,280 CJK/clipped/multiline/noise
negatives, with no real school originals/values or consumed literal strings as
targets. The source density was 2x/4x and every character in a line shared its
baseline and resize. Actual ink was removed for clipped negatives. The prototype
used BGR64, original-pixel-center bilinear/uint8 rounding, normalized-zero padding,
blank 0, space 1 and UNK 96. These differ from Paddle's BGR48 contract.

The fixed selection rule chose epoch 24, SHA256
`2a4c726434b7bdd980a370b9e3daba852bd28f4709e60bc2a8b2f38a79e5fd4a`.
Its low training loss does not establish generalization or safe confidence.
Training took 1,063.90 seconds on two CPU threads and peaked at 2,317.88 MiB in
the Linux research process. No Windows inference speed/memory claim is made.

Sawarabi Gothic and Sawarabi Mincho final samples/pages were never generated or
inferred. Sawarabi Mincho had earlier source-only PDF extraction use, not recognizer
use; Sawarabi Gothic had never been rendered. Both remain reserved for a useful
candidate's genuinely independent recognizer evaluation. This prototype receives
no final accuracy, whole-document, production qualification or activation credit.

Source and frozen protocol remain in `tools/ocr-synthetic-training/ascii-line-ctc/`;
weights, training pixels and logs are outside Git in
`/workspace/scratch/ascii-line-ctc-20261013-v1/`. ONNX/CoreML conversion source was
prepared, but exporting a rejected model was not needed. iOS sources and existing
recognizer artifacts remain untouched; this worker performs no commit/push.
