# Inactive English V5 font-balanced research package

Standard ONNX FP32, full438 dictionary, independent reject probability. This is the selected newly adapted officialTorch model, not a faithful transplant of the previous nativeONNX model. The earlier strict transplant failure and zero-wrong-accept training records remain unchanged.

Load model.onnx locally with ONNX Runtime CPU. Inputs: x float32[B,3,48,W], BGR, W320..960; valid_width int64[B]. Outputs: probabilities float32[B,T,438], reject_probability float32[B]. Metadata defines exact originalpixel preprocessing, dictionary/blank/space, CTC max-run confidence, fullT padding refusal, and position mapping. image_tensor.py is the pinned Python reference. CTC SourceBox is an estimate and does not establish original ink ownership.

Newmodel Torch/ONNX dynamicshape and fixeddev raw comparisons are in summary.json/JSONL receipts in research-results. Numeric differences and confidence/review tradeoffs are recorded rather than repaired. The training stage was pre-registered without exports; this separate portable-v1 stage was requested after fixeddev usefulness evidence. No more training occurred.

Weights stay outsideGit. No final/modelpublication/appactivation/iOS edit. CoreML/iPhone execution not assessed. English specialist cannot represent wholeJapanese mixed fields; suffix accuracy is not wholefield or document qualification. Interior damage and crop-source contradictions remain limitations. Declared officialfoundation license Apache-2.0; provenance/checkpoint/preprocess SHA are in metadata.json.
