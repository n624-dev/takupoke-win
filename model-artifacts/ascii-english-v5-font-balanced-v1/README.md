# English code specialist v1

Apache-2.0 derivative of PaddlePaddle/en_PP-OCRv5_mobile_rec_safetensors revision e4abce2bbd335f6c3eda0507b7f8e8abd5af3419. Copyright PaddlePaddle Authors; synthetic adaptation and ONNX packaging by the takupoke-win project (2026). Modified: all official CNN/encoder/full438 classifier trained for four epochs, added reject head; standard ONNX FP32 export. No school data or personal information used.

Selected font-balanced epoch4. See ../../research-results/ocr-synthetic-training-20261013/ascii-english-v5-font-balanced-v1/README.md for frozen training, dev, negatives and remaining errors. This is an English specialist, not a whole Japanese reader. Windows uses a separate Japanese reader and source-bound original image review.

model.onnx: 22,183,975 bytes, SHA256 9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703. dictionary.json: SHA256 fbf156ed886e4c46075f599651e60a0c38953dce89e10d999c765014477a7368. Standard ONNX opset17; x FLOAT32 BGR[B,3,48,W], valid_width INT64[B]; probabilities[B,T,438], reject_probability[B]. CPU FP32, widths320..960. Full438 CTC decode and unsupported-padding refusal. Rejection scores are not calibrated. See export-metadata.json for exact preprocessing and original export verification; its inactive status describes the closed research handoff, not subsequent application integration.

The same artifact may be used for iOS evaluation with a compatible ONNX runtime. CoreML conversion and iPhone execution are not claimed; iOS repository unchanged.
