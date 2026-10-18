# Fixed CPU image-language OCR development comparison

384 completed local requests: GLM-OCR Q8 and official Qwen3-VL-2B-Instruct Q8,
each on the same192 original fictional PNG/BGRA-hashed crops used by V5/V6.
llama.cpp b11371 CPU4/context4096/temperature0/topK1/topP1/seed17/output128;
one local loopback engine at a time, no retries or remote inference. The worker
received only ordinal images and each reader's neutral/trained prompt. All raw
responses were closed and hashed before the scorer opened the independent oracle.

| Reader / cohort | Raw BODY literal exact /72 | Raw header exact /21 | Correct empty controls /3 |
|---|---:|---:|---:|
| GLM-OCR / Sans |0|0|0|
| GLM-OCR / Serif |0|0|0|
| Qwen3-VL-2B / Sans |45|20|0|
| Qwen3-VL-2B / Serif |39|21|0|

GLM's responses consistently begin with an extra newline. A separate **zero-call
post-run formatting diagnostic** removing only that first newline gives BODY48/72
and56/72, headers21/21 and20/21. The primary raw scores stay unchanged, and no
field is repaired or promoted to Evidence. The newline is a formatting issue,
not proof of zero Japanese recognition. Remaining internal spaces/characters
still differ. The previously specified single-final-newline diagnostic fixes
none of the raw outcomes. All responses stopped normally, without output truncation.

GLM generated `---` after its newline on white controls; Qwen generated a period.
Neither is an empty-field proof. Qwen's arbitrary room lookalikes remained a major
failure: Sans3/24 and Serif5/24 exact room literals, even though its headers were
mostly correct. Confidence is autoregressive/unassessed and is not borrowed from
Paddle. Spatial glyph ownership, role binding, PDF completeness, Validator and
adoption are not executed in this component cohort.

Observed server peak resident memory was1,949,306,880 bytes for GLM and
4,588,318,720 bytes for Qwen, inside this CPU16GB development envelope. It is not
a Windows minimum-RAM or end-to-end performance qualification. Inference timings
were collected while a separate Paddle comparison also ran locally; do not claim
an isolated CPU latency or causal speed ranking from those timings.

The models/quantizations/input recipes are rejected for activation by this
measurement. Raw responses, individual native usage/timing, model/projector/server
SHA identities and original aggregate output remain unchanged. This consumed
corpus must not be labeled as a fresh held-out test. Public models and runtime
bytes were kept outside the repository; only invented pixels/output are published.
