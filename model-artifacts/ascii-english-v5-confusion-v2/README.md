# English code specialist v2

Standard ONNX FP32, full438 dictionary, opset17. A further synthetic adaptation of the
[previous font-balanced v1](../ascii-english-v5-font-balanced-v1/README.md), whose foundation is official
PaddlePaddle/en_PP-OCRv5_mobile_rec_safetensors revision `e4abce2bbd335f6c3eda0507b7f8e8abd5af3419` (Apache-2.0).
Copyright PaddlePaddle Authors; synthetic adaptation and ONNX packaging by the takupoke-win project (2026).
Modified: two further low-rate epochs on independent fictional literals, full CNN/encoder/438 classifier and reject head;
BN running statistics fixed. No real school documents or personal data were used.

`model.onnx`: 22,183,975 bytes, SHA256 `3271bfaa22d895a0d5d085346cfdd865c3eeb5a6542ae9c45f66fc48a254dc44`.
`dictionary.json`: 3,614 bytes, SHA256 `fbf156ed886e4c46075f599651e60a0c38953dce89e10d999c765014477a7368` (unchanged).

Inputs: BGR FP32 `x[B,3,48,W]` and INT64 `valid_width[B]`. Outputs:
`probabilities[B,T,438]` and `reject_probability[B]`. Preserve all CTC intervals and native scores;
never clamp emitted padding or treat reject/confidence as calibrated proof of correctness.
The [format contract](format-contract.json) and original [export metadata](metadata.json) record preprocessing and provenance.
Twelve dynamic shapes and 496 rows agree with selected Torch text, CTC positions, padding and diagnostic decisions;
small FP32 score differences remain. This is an English specialist; Japanese primary recognition is separate.

Independent fresh positive crops: exact360→368/384, CER0.463→0.303%, 10 gains/2 regressions.
Wrong accepted13→7; negative accepted11→11, including cut/damaged images. Fraunces regresses despite aggregate gains.
[Full training/fresh/fidelity report and retained failures](../../research-results/ocr-synthetic-training-20261013/ascii-english-v5-confusion-v2/README.md).
These are crop results, not complete-document or real-school accuracy.

Publication of these bytes does not itself activate them in the application. Actual application routing and full-document
validation are separate. Metadata's inactive status describes the original research handoff.
The standard ONNX/dictionary can also be evaluated on iOS with a compatible runtime.
CoreML conversion and iPhone execution are unverified; iOS source has not been edited.
