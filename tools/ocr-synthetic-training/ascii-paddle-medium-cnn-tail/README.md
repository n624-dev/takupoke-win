# Research final-CNN spatial adaptation

Closed bounded CPU experiment. Read [the research report](../../../research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-cnn-tail-v1/README.md) for actual results and fidelity limits. Original fallback selected; no trained model is active or qualified.

`reproduce.py` checks original native lastCNN/downstream/head on consumed initialization inputs; `benchmark_backward.py` measures no-update numeric backward. `tail_protocol.json` freezes new data/selection/budgets; `tail_train.py` executes once and refuses an existing freeze. `tail_report.py` audits completed source/raw/checkpoints/lossless FP32 caches; `tail_errors.py` diagnoses saved outputs; `tail_probes.py` runs consumed source-proof controls only after selection. No oracle inference routing, dictionary reduction or output correction.

Python3.12 / torch2.7.1 CPU / onnx1.20.1 / onnxruntime1.23.2 / numpy2.3.5 / safetensors0.8 / Pillow12.3 / fonttools4.61. Inputs, downloaded fonts, original model, checkpoints and caches are external scratch assets, not repository payload. Shared frozen height-neck and exactSVTR source dependencies are recorded in shared-source-audit.json. All Unicode metadata I/O explicitly UTF8/LF. No trained export/CoreML or Windows/iPhone execution is qualified; iOS is unchanged.
