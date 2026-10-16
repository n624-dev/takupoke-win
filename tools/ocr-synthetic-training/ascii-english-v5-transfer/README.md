# English V5 transfer feasibility tools

Closed before training; native numerical fidelity gate failed. See [results](../../../research-results/ocr-synthetic-training-20261013/ascii-english-v5-transfer-v1/README.md). These tools never activate a model, route by oracle or reduce the full438 vocabulary.

`reproduce.py`, `reproduce_fused.py`, `reproduce_math.py` are distinct once-only frozen512-input/10-shape initialization checks. `native.py` loads the explicit official local-only class with native epsilon. `native_fused.py` provides the original fusedCNN differentiable whitelist. `native_math.py` matches norm/attention operation order. `fidelity_trace.py` diagnoses saved numerical worst cases; `budget.py` is the failed larger-batch no-update experiment; `budget_small.py` is the successful independent small-batch numerical backward. No training/data-generation/qualification/export script is provided.

Dependencies: Python3.12, transformers5.19.0, torch2.7.1 CPU, onnx1.20.1, onnxruntime1.23.2, safetensors0.8.0, numpy2.3.5, Pillow12.3, PyYAML6.0.3. Runtime/assets live under external scratch paths; Unicode metadata I/O is explicit UTF8/LF. Standard Torch/ONNX primitives do not establish Apple or Windows execution qualification.
