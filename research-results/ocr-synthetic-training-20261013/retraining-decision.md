# Historical additional training decision after the first adaptation

This records the decision at the first-adaptation checkpoint. Later source-density and
whole-line evidence justified separate, newly frozen ASCII suitability and transfer
experiments; their dev/final protocols are independent. This historical diagnosis is
not a current instruction to stop the production-quality work.

Do not start another synthetic-training run at this stage. The first adaptation
remains a useful, unqualified research candidate. Acquisition, original-pixel and
cell ownership, complete-document validation, and comparison with an independent
untried recognizer are the next priorities. This decision does not mean the
application's production goal has been reached.

The consumed 384-row final contains 33 remaining errors, 23 at the unchanged 0.8
minimum-confidence floor. Twenty-three rows contain l→I (26 substituted glyphs),
mostly in Zen Kaku Gothic New; three contain l→1. Five rows regress relative to
the base. The most confident remaining wrong literal reaches 0.999806, so raising
confidence alone is not an established remedy. Additional same-distribution epochs
already plateaued: development exact match 162 at epoch 1, then 161 at epochs 2/3.

Font inspection is more specific than declaring the characters impossible to
distinguish. Zen's I/l have different vector heights (700/722 font units) and
advances (230/210). Their tightly trimmed isolated raster masks are identical at
seven of the tested 19..33px sizes. This does **not** establish that a complete
line's pixels, baseline, relative heights, advances or source positions are
identical. Preserving a natural line and improving original source density may
provide useful information. More font exposure or cautious last-backbone-stage
adaptation might improve use of that information, but no result yet shows that
it would meet the complete-document correction limit.

Safety is an independent blocker. After training, clipped probes produce
minimum-confidence >=0.8 output in 11/16 cases, versus 8/16 before; multiline
probes increase from 2/16 to 3/16. CTC confidence does not prove complete original
glyph support. Training a narrower code prior, adding epochs, or silently treating
clipped text as a valid class would not solve that requirement. Six selected-model
rows also touch normalization padding; the native rejection guard remains required.

As an arithmetic illustration, a document with 400 body fields and a maximum of
three actual corrections requires at least 397 exact body fields, plus correct
automatically acquired headers and structure. This is not an extrapolation of
synthetic-crop errors to real documents, and crop averages cannot qualify a model.

A future training condition would be justified by a reproducible error on visibly
distinguishable, complete original lines after acquisition and membership are
verified. Before training, freeze new seeds, strings and a broadened public-font
train/dev split, source-density variations that preserve line baselines and
relative heights, broad vocabulary retention controls, and fully visible
I/l/1/O/0 controls. Keep the base and current fixed candidate eligible in new dev.
If a low-rate last-stage-backbone adaptation is attempted, cap the experiment and
choose one checkpoint before opening an unused-font whole-document final.

The new final must include damage, multiline, blank, padding and ownership controls;
all headers/structure must come from the source, with no gold repair or candidate
selection and at most three actual body corrections per whole document. Prior
final pixels and literals must not enter training or receive new-final credit.
Standard ONNX/dictionary/BGR48/CTC interval contracts and optional CoreML portability
remain requirements. iOS sources remain untouched and Apple-device prediction
qualification remains unmeasured.

The reproducible read-only diagnosis is in `retraining-diagnosis.json`, generated
by `tools/ocr-synthetic-training/diagnose_retraining.py`. It performs no model
inference, training, threshold tuning, checkpoint selection or activation. All
prior checkpoints, output records and portable packages remain unchanged.
