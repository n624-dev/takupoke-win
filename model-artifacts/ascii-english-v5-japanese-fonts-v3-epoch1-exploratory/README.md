# Exploratory Japanese-font English-code expert

Standard FP32 ONNX, full 438-symbol dictionary, opset 17. This continues
[font-balanced v1](../ascii-english-v5-font-balanced-v1/README.md), whose foundation
is official PaddlePaddle English PP-OCRv5 revision
`e4abce2bbd335f6c3eda0507b7f8e8abd5af3419` (Apache-2.0). The actual initializer
and teacher are v1 epoch 4. All training images are independently fictional;
no private PDF or personal document was used.

`model.onnx`: 22,183,975 bytes, SHA-256
`95bab179e0d6cc75269419078d68696cd4679661f94f6128b858b7a074fe2971`.
The unchanged dictionary SHA-256 is
`fbf156ed886e4c46075f599651e60a0c38953dce89e10d999c765014477a7368`.

**Epoch 1 failed the original standalone training gate and was not selected.**
Its code character errors fell 79 to 73 on consumed development, below the
registered 10% reduction requirement. Original trial selection retained v1;
its independent fresh set remains unopened. These facts and the original package
metadata remain unchanged. This artifact supports a separately registered
document-route experiment, rather than a retroactive claim that the original
training gate passed.

On the consumed native first page, the manual-only expert with fixed original-ink
margin 2 and complete v1 fallback changed exact fields 66 to 67 of 72, with one
gain and no loss. Character errors fell 6 to 5. All 85 actual C# source lines
match that exploratory proposal. This is preliminary development evidence;
whole-document native Windows and unused-font verification are pending.
The deployed bundle is unchanged at this publication.

The input, preprocessing, dictionary, CTC and valid-width contract is in
[format-contract.json](format-contract.json). Native probabilities, reject values,
CTC intervals and source geometry must be preserved. A low native confidence
requires original-image review; it is never raised to pass a threshold.

[Inference subset](inference-subset.json) lists included original package bytes.
Standard ONNX and its dictionary can be evaluated on iOS with a compatible
runtime. CoreML conversion and execution on an iPhone are unverified. iOS source
has not been edited. Original packaging metadata describes its historical
research handoff, including inactive/unpublished status at packaging time.
