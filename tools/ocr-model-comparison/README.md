# Same-pixel OCR model and color comparison

This fixed CPU experiment compares official PP-OCRv5 mobile and PP-OCRv6 medium
with original BGR and one fixed luminance-gray condition. It also measures the
former two-pass CTC decoder and the new production decoder on the **same native
probability tensor**, requiring identical text, order, CTC intervals and confidence.
There is no extra model inference for the decoder timing comparison.

The corpus contains 192 newly painted ordinal rows: per font, 72 invented body
fields, 21 printed class/lookalike headers and three white controls. Noto Sans JP
and Noto Serif JP have independently pinned public font bytes and distinct literal
seeds. Sizes 14/18/22 and four source colors are fixed before inference. Every
nonwhite source pixel plus four available margin pixels defines one crop. All
four model/color arms use that geometry; gray uses integer luminance
`(299*R+587*G+114*B+500)/1000`. No OCR result selects or changes a crop.

Models, dictionaries, fonts, input pixels, assertion oracle, generator and decoder
are hashed before the 768 scheduled calls. Original model files are pinned by
revision, size and SHA-256, including the separately derived V6 alphabet. The
worker reads ordinal pixels and artifact identities only. Raw output is flushed
and hashed before the scorer opens the literal oracle. There is no teacher list,
role answer, previous OCR response or expected text in recognition inputs.

Exact whole literals, Unicode-scalar CER (including spaces and punctuation),
correct/incorrect rows above the unchanged 0.8 diagnostic floor, uncertain/empty
outputs and unsupported CTC padding are separate. Blank controls cannot be
counted as confident transcription. Missing, duplicate or foreign observations
make scoring fail rather than silently reducing a denominator. Two fonts and
color arms do not provide independent document success counts.

The outcome digest retains literal text, CTC intervals, padding/floor dispositions
and input widths, while excluding OS-dependent float probabilities and timing.
It supports a separate native Windows comparison of the same output contract.
Native module identity is captured while a model session is alive, before the
recognition calls, using a case-insensitive filename match on Windows.

Run with .NET 10 and the pinned Python requirements:

```sh
python -m pip install -r tools/ocr-model-comparison/requirements.txt
python -m unittest discover -s tools/ocr-model-comparison -p 'test_*.py'
python tools/ocr-model-comparison/run.py --output /tmp/takupoke-ocr-comparison --cleanup
```

The output directory must not already exist. It contains only owned public model
and font downloads, generated pixels and fictional output. `--cleanup` removes
it in `finally`; the aggregate report is printed before cleanup. CI also performs
outer cleanup after failures. No Actions artifact/cache or model activation is
created. ONNX telemetry is disabled. Public models are Apache-2.0; pinned fonts
are from Google Fonts under SIL OFL, and no downloaded assets are committed.

The first call includes model warmup. Native inference timings include the
synchronous call, not preprocessing or PDF acquisition. CTC timing averages
three fixed repetitions with alternating old/new order and prior unmeasured
decoding; it is a decoder comparison, not total application speed. Process peak
working set includes sequential model sessions and is not a device RAM minimum.
The 12-minute cooperative worker deadline and 15-minute outer timeout are
operational limits; native `Run` is synchronous.

This is **recognition component evidence**. Detection, original-PDF rendering,
source/role ownership, merges, parallel lessons, document-wide Validator, manual
correction and saved adoption are not executed. Favorable character scores do
not qualify a model or permit production grayscale preprocessing. The research
workflow additionally checks native Windows reproducibility; Linux alone cannot
establish Windows runtime or physical-device performance.
