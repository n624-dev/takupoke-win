# Japanese PP-OCRv3 conditional expert

Native V3 run38865270712 at source231fe87203b9c92a0c79993dc8649a6f990f25dc passed the prospectively fixed full-document quality/nonregression criteria. This conditional route and literal-code retries are selected for the next default; factory/installer verification is recorded separately. This is the already available Japanese-specific PaddleOCRv3 mobile recognizer, not newly trained weights. Its earlier global clean-line comparison remains rejected; this route only retries low-confidence Japanese characters in already observed inline role/body lines. Every other literal must agree, the complete alternate CTC output must have original-pixel support, and each changed character retains its own actual alternate-model probability. Agreement and confidence are not proof of correctness or calibrated cross-model probabilities.

- Model: `https://huggingface.co/tobiichioriguchi/japan_PP-OCRv3_mobile_rec_onnx/resolve/1c772b3dd782e6ac4e61aba8bfe9683bee3334fc/inference.onnx`
- Model bytes:10089078; SHA256`2e397c27461751befc82eca78c9b19e87322ebee34e7316468b21856f9977d64`.
- Native dictionary:26140bytes; SHA256`0cda0d37debec2f0481bb5bb7bf3223b159d8268d288781eef1668455a811f1d`.
- Dictionary4401tokens, blank0, both native trailing space tokens retained. No normalization, vocabulary whitelist or name correction table.
- Input float32BGR `[1,3,48,W]`, original supported crop, integer uint8 bilinear sampling followed by `(pixel/255-.5)/.5`, normalized-zero right padding. Output native CTC probabilities `[1,T,4401]`, greedy decoding. Source intervals use the whole input time axis and are rejected if they touch unsupported padding.
- ONNX opset11, Apache-2.0. This distribution references the immutable conversion; upstream archive and conversion details are in [UPSTREAM.md](UPSTREAM.md). The upstream numerical conversion claim has not been independently reproduced here. Actual CPU inference is tested with ONNX Runtime1.23.2.

The model and dictionary are portable ONNX assets for possible reuse. CoreML conversion and iPhone execution have not been verified. iOS sources are unchanged. The new model profile20261016-v3 has7 artifacts/135868279bytes (about130MiB); existing installations must explicitly update models. See the qualification records and factory verification before using a distribution build.
