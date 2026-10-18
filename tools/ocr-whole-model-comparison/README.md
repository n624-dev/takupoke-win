# Paired whole-document specialist comparison

The baseline is the deployed `RecoveryOcrModels.OpenRuntime` factory. The candidate
uses the same detector, Japanese recognizer, dictionaries, primary original-ink
framing, parser, engine and review limits. The registered manual code expert reads
only existing eligible source crops with a fixed two-pixel original-ink frame.
It retains native confidence, reject, padding and length checks. Invalid expert
output uses the complete original v1 decision; headers, Japanese prefixes and the
ordinary route retain their existing behavior. Both expert weights and this
composite strategy are frozen before new whole-document inference. This research
reader does not activate the model in the product.

`exclusions.py` requires the training collaborator's closed train, development
and fresh manifests. It records exact hashes of previous literals that could
match the prospective generator's fields or codes. Numeric tails are checked
independently. Reused structural labels are disclosed. `prepare.py` freezes the
generator and fonts before drawing or model calls: two consumed regression fonts
and two unused fonts, five pages, three classes, eight periods, two deliberately
invalid header variants. Fresh codes vary length, case, digits and separators;
there is no special room-code grammar or correction dictionary.

After `candidate.json` and `exclusions.json` are frozen, the Windows workflow runs
`run_windows.py`. Owned fictional lossless PDFs are rendered with the existing
native Windows renderer. Both workers close before reference assessment. Every
field, missing/extra/duplicate slot, literal error, original review union, native
confidence, runtime and memory count is retained. Paired source inspection checks
all primary recognition observations and original raster hashes remain identical.

Acceptance requires complete positive coverage, valid preview or original-image
review, character error rate at most 3% per font, review union at most 128,
memory at most 6 GiB, all eight missing-header negatives refused, no positive font
losing exact fields or increasing character edits, and strictly more exact fields
on the two fresh fonts combined. Zero errors are not required. A failed trial is
retained and becomes development evidence; it cannot be relabeled as unseen or
used to reselect a checkpoint on the same holdout. Synthetic evidence does not
establish accuracy on arbitrary real PDFs or measure human review time.

The workflow always collects closed outputs, failure logs, freezes and exact
source snapshots. Full pages, PDFs, models and binaries are removed from its
temporary directory after artifact collection. No private document is used.
