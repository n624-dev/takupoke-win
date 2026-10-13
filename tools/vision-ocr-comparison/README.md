# Frozen-pixel local image reader development comparison

CPU-only, 16GB target. This isolated experiment compares GLM-OCR Q8 with its
own Q8 image projector and official Qwen3-VL-2B-Instruct Q8 with its matching
Q8 projector. llama.cpp b11371's verified official Linux CPU binary hosts one
model at a time, listening only on loopback. No remote inference API is used.
Public model/runtime downloads occur before any image payload is staged.

The192 ordinal crops are the exact original BGR pixels from the consumed
20261013 V5/V6 development measurement. `fictional-pixels.json` transports those
pixels losslessly as PNG with both PNG and decoded BGRA hashes. It contains no
expected text, role labels, OCR answers, class vocabulary or original school
data. `oracle.json` is opened for scoring only after all raw responses are closed
and frozen. These are development components, not new held-out documents.

Each model makes192 fixed fresh requests with temperature0, topK1, topP1,
seed17, context4096 and maximum128 output tokens, CPU4 threads and one slot.
There is no retry, output-fed correction or candidate selection using the
oracle. GLM uses its officially trained `Text Recognition:` prompt; Qwen uses
one neutral plain transcription instruction. Different trained input protocols
are recorded rather than treated as a prompt-only causal comparison.

Raw literal exactness is primary. A separately labeled single-final-newline
formatting diagnostic never changes raw text or creates adoption credit. Length
stops are counted separately. Autoregressive confidence is uncalibrated and is
not transferred from Paddle or compared with CTC scores. Source/role/ink
certification, PDF rendering, complete document Validator and adoption are
unassessed. Empty output alone never certifies a real empty timetable field.

Examples after separately downloading/verifying the pinned public assets:

```sh
python -m unittest discover -s tools/vision-ocr-comparison -p 'test_*.py'
python tools/vision-ocr-comparison/run.py --server /path/to/llama-server --models /owned/models --output /owned/results
```

The output must not already exist. It retains raw responses, frozen identities,
resource observations and aggregate scoring. At most12GiB observed server RSS
is allowed in this CPU16GB development condition. Servers and owned process
groups are stopped on success/failure; downloaded assets are not committed.
No inference process receives hosted account tokens. No model catalog or
application provider is activated by this experiment.

Official references: [GLM-OCR model/prompt/license](https://huggingface.co/zai-org/GLM-OCR),
[llama.cpp GLM export](https://huggingface.co/ggml-org/GLM-OCR-GGUF),
[Qwen's official export](https://huggingface.co/Qwen/Qwen3-VL-2B-Instruct-GGUF),
[llama.cpp multimodal support](https://github.com/ggml-org/llama.cpp/blob/master/docs/multimodal.md).
GLM-OCR uses MIT; Qwen's export uses Apache-2.0. Asset revisions and sizes are
retained in the separately verified local download receipt and SHA pins in code.
