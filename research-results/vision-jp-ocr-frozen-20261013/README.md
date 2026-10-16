# Japanese-trained local OCR on frozen development pixels

Qwen3.5-ocr-jp-2b F16 and its matching F16 projector completed192 fixed local
requests on the same consumed original PNG crops as the earlier V5/V6/VLM
comparisons. Officially trained HTML prompt, CPU4, context4096, greedy seed17,
topP0.1, thinking disabled, output512. No retry, cloud inference or answer-guided
selection occurred; raw output closed before literal scoring.

The original bounded interpreter accidentally omitted standard h1–h6 heading
tags and refused137 valid native markup outputs. Its frozen report remains;
those protocol refusals must not be described as137 OCR character errors.
`heading-protocol-diagnostic.json` is a **zero-inference protocol correction**
using the same raw bytes, preserving spaces/case/punctuation and all bbox bounds.
No extra model call or adoption occurs, and no raw/original score is overwritten.

| Cohort | Interpreted BODY exact /72 | Headers /21 | Empty controls /3 |
|---|---:|---:|---:|
| Sans |58|20|3|
| Serif |59|20|3|

Subjects are24/24 in both; rooms remain10/24 and14/24. Thus the model can read
many Japanese literals but still misreads confusable room symbols and a class
header in each cohort. Predicted boxes are not original glyph ownership; native
confidence remains uncalibrated. Full PDF/roles/ink/Validator and Windows memory
are unassessed, and activation is rejected.

Observed Linux server peak RSS is12,413,468,672 bytes (about11.56GiB), within
this experiment's12GiB server guard but leaving little of a16GB desktop budget.
No Windows minimum-memory or end-to-end latency qualification is claimed.
All responses stopped normally. Timing overlaps other CPU work; no isolated
speed comparison is made. The consumed crops are not fresh final documents.
