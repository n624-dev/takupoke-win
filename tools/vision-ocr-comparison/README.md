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

The separately frozen `--model-set japanese-html` condition runs only the
Japanese-trained Qwen3.5-ocr-jp-2b F16 model and its matching F16 projector,
not another pass of the closed base conditions. Its trained prompt is
`OCR this image as HTML layout blocks with bbox and label.`; output is bounded
to512 tokens, topP0.1, thinking disabled. Other CPU/sampling/image parameters
stay fixed. Both raw HTML exactness and protocol-interpreted text exactness are
reported. The bounded parser preserves text spaces, case and punctuation and
refuses malformed markup instead of repairing it. Predicted boxes never become
original glyph/ink ownership evidence. This consumed component comparison
still cannot qualify a complete PDF or activate the application model.

Original [Japanese model](https://huggingface.co/ebinan92/Qwen3.5-ocr-jp-2b)
uses Apache-2.0. The pinned
[community F16 export](https://huggingface.co/nakanishi08/Qwen3.5-ocr-jp-2b-GGUF)
revision is `3f64928e34e6b619a7ea7a8b804d03f2ed01b978`. Full model/projector
hashes are enforced before requests. F16 was selected before evaluation to
measure this candidate without4-bit quantization loss within the CPU16GB budget.
