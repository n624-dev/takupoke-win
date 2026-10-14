# English V5 font-balanced bounded experiment

Independent unmodified official Transformers English V5 foundation, full438 dictionary. Not a faithful transplant of native ONNX. The closed ONNX-fidelity failures and older zero-wrong-accept trial are unchanged.

New training8 font families (4mono/4proportional), development4 family-disjoint fonts (2mono/2proportional). Positives exactly50% natural short English sentences/fragments,40% arbitrary codes/confusables,10% printable95 coverage. Every positivegroup equally balanced perfont. No old developmentfonts moved to training; previous literals exclude only. CJK unknown negatives use previously consumed Noto fonts, remain whole crop, and give no Japanese-quality claim. No school data.

Original whole nonwhite ink bbox+2 pixels, commonbaseline/relativeheight/internalgaps, FP32 BGR48 sampling and losslessFP32 cache. Width320..960, source offset/validwidth recorded. No officialHF imageprocessor/nativeWindows byte-parity claim. Initialdirectofficial forward and adapter byte self-consistency required. Official CNN/encoder/full438head adapt; BNrunningstats/config/normeps unchanged. Startfrom originalofficialweights, not prior checkpoints.

CPU2,batch2,RSS4GiB,4epochs with2/4evaluation,30min cache/training budgets,extra1GiB/free2GiB. Originalbaseline remains selectable. Newresearchgate: wholeCER and codeCER at least10%relative improvement, naturalCER at most1percentagepoint regression, eachmono codeCER no regression, negativeaccepted no increase, insufficientpadding alwaysrefused. Rankeligible by wholeCER/codeCER/naturalCER/exact/earlier. Wrongacceptzero is not required. Reject/minCTC .8 remains uncalibrateddiagnostic; report rawerrors, confidence, reviewcounts and negativekinds separately.

Sawarabi final remains sealed; no final/ONNX/CoreML/productactivation/publicweights/iOSediting/push. Passingresearchgate only supports separatelyauthorized prospective work, not document qualification. Originalsource/domain/geometry proof remains external.

Run with /workspace/scratch/ocr-training-runtime/bin/python: preflight.py, then train.py, then report.py. Paths intentionallyrecord researchworkspace; weights/PNGs/caches/checkpoints externalGit. All Unicode I/O explicitUTF8/LF. Frozen source/raw/protocol never rewritten afterexecution.
