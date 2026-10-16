# Official TrOCR printed: consumed header suitability failure

microsoft/trocr-base-printed revision93450be3f1ed40a930690d951ef3932687cc1892,
safetensors1,333,384,464bytes SHA256
1cf4a6eedab26afaaf505f1c7f73d9634944924dbd1ed049d569db98039cd596.
The HF model card supplies no explicit weight license; no weight distribution
or application activation is qualified. No model binary is committed.

Explicit official TrOCRProcessor/ViT PIL backend, RGB384 bilinear/0.5 normalization,
FP32CPU2/eager, greedy32-token budget, no prompt, crop/contrast retry, lexicon,
case conversion, stripping or answer cleanup. All48 planned existing consumed
ordinals (42 short headers+6 blanks) complete. Oracle opened only after worker
exit/raw receipt. Autoregressive probabilities including EOS are uncalibrated,
not Paddle CTC confidence or independent source-glyph boxes.

Header exact14/21 Sans,11/21 Serif; at diagnostic0.8 token minimum,12 and5 correct,
3 and6 wrong. All six blank crops hallucinate. This is a closed suitability failure,
no full-document/heldout/Windows/qualification credit or additional training.
Peak Linux RSS1,976,037,376bytes, not total Windows16GB-device memory.

The loader initializes two absent encoder pooler parameters. The recorded
read-only compatibility audit confirms generation consumes encoder last_hidden_state,
not pooler_output: these unused weights cannot repair or alter sequence evidence.
The original warning and runtime source hash are retained. Raw generated token IDs,
probabilities including EOS, budget/EOS state and original PNG/tensor hashes are
unchanged. The initial processor integration failure remains in its own directory.

Raw SHA256 d370f67d8a089c845afd75586c30e135afba6feec54605fdde08f010f66997fe .
