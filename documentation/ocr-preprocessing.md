# Fixed-model OCR preprocessing

The recognizer now resizes with bilinear sampling, uses a 48-pixel height,
separately records valid resized width and input width, and pads the right side
with zero **after** normalization. Short crops use input width 320; longer crops
use the reference's floored target width and bounded valid width. The existing
960-pixel input-width limit remains. Crops requiring a wider tensor fail safely
instead of being compressed to fit.

The reference is PaddleX revision `917f10be35f644d01a6bf57cf8c312bdba0a4183`,
[OCRReisizeNormImg](https://github.com/PaddlePaddle/PaddleX/blob/917f10be35f644d01a6bf57cf8c312bdba0a4183/paddlex/inference/models/text_recognition/processors.py).
Its source SHA-256 is `86f9e763de84358c1a0de2d73e60372d2153c77ee40d9af036595a8387dd412d`.
The exact PaddleX version producing the pinned ONNX export remains unknown.
The existing graph declares dynamic width; this does not establish that every
width is supported. A fixed model-input width incompatible with the transform
is rejected before inference. Model files, dictionary, confidence thresholds,
CTC blank/repeat policy, Rules and Validator are unchanged.

BGR channel order and normalization match the pinned configuration. Resizing
rounds bilinear values to uint8 before float normalization. Tests use samples
computed independently by the actual pinned Python class with OpenCV 4.11.0;
the declared tolerance is one uint8 level (`2/255` after recognition
normalization, plus `1e-6` for floating arithmetic). This allows OpenCV's integer
interpolation rounding differences, rather than claiming byte-identical output.

CTC time positions cover the padded input width. Their inverse map therefore
uses both input width and valid width when returning original crop coordinates.
A nonblank decoded interval that touches padding fails; it is never clipped
into the crop. Fractional crops sample only pixel centres inside their original
support. Character intervals remain CTC alignment estimates, not independently
measured character-ink boundaries or proof that recognized characters are right.

The detector uses bilinear sampling with its existing 960-pixel limit and
32-pixel stride sizing. The referenced detector recipe uses a different
resize-long/128-stride shape and DB contour/crop processing; those differences
remain. This change reproduces selected resize/padding behavior, not the full
official OCR pipeline. It provides no new model-quality or literal-recovery pass.

Before recognition, crops are completed using 8-connected nonwhite pixels in
the original raster. A component must have exactly one original crop owner;
its minimal pixel bounds can extend that same recognition input. Unowned
or shared components, text connected to a physical rule, newly enclosed
foreign ink, and overlapping crop-supported pixels cause refusal. Rule scans,
ownership and component traversal share the existing 64-million pixel-work
limit. No disconnected ink is hidden by enlarging coverage alone. Successful
coverage uses the exact completed recognition inputs; CTC coordinates use the
same inputs. This establishes crop support, not correct characters or cells.

For explicitly marked OCR pages, Builder may additionally recognize existing
public header patterns from one native source line. Supplied native order must
already match spatial atom order with consecutive source orders; all atoms must
share vertical overlap. Crossing a physical rule or overlapping another line's
scope prevents this supplemental candidate. Matches retain whole original
atoms and boxes and are deduplicated against legacy labels by page, value and
ordered source IDs. Vector labels, distance limits and Validator are unchanged.
A recovered year header does not imply that later cell/header geometry passes.

An internal observer is unset by default and performs no logging. When a local
fictional-data diagnostic opts in, it receives the original and completed crop,
valid/input widths, time count, and already decoded piece text, confidence and
time interval before the unchanged empty/low-confidence refusal. It supplies
no recognition output and requires no additional inference. Observer exceptions
propagate as execution failures; the observer does not bypass safety guards.

Historical full-acquisition and tensor-diagnostic v1 recipes relied on the
removed nearest/direct-width helper. Their workflow is manual-only and their
entry point rejects incompatible current source before model installation,
reporting unassessed with zero native calls. Archived exact-source results stay
unchanged. Any future fixed-model measurement needs a separately pinned recipe
and the independent original-PDF/full-formal oracle; it cannot reuse a historical
recipe name or infer correctness from higher confidence alone.

## 閉じた内部領域の罫線復旧

元の接続検査で罫線を確定できる場合は、線の座標と端点をそのまま保持する。全候補が接続検査で消える場合だけ、観測した直交線との交点の間へ線を限定して再検査する。線の延長、画素やOCR文字の変更、検査上限の緩和はしない。孤立した文字線やH字型の開いた線は罫線にならず、交点外の線や灰色の印字も未読インクの検査対象に残る。

この改善はセル境界候補の取得であり、科目・教員・教室の割り当てや文書全体の完全性を認可しない。既存Validatorと未読印字の検査を通してからプレビューへ進む。
