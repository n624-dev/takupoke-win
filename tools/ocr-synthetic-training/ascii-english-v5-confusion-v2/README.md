# English V5 confusion continuation v2

One bounded continuation from the successful font-balanced epoch-4 checkpoint, matching the original active ASCII ONNX SHA `9dd3ed77db4f2af171cf434d200c666033c9bdfe5f33446bc47dbfecac90d703`. The active checkpoint is both initialization and teacher. Full 438-class softmax, original architecture/config and frozen BN statistics remain. No vocabulary renormalization, dictionary correction, confidence tuning, private PDF data, product activation, model publication or iOS changes.

Train uses only the previous eight **training** font families and fresh fictional literals. It includes short arbitrary classroom-shaped codes, arbitrary codes, I/l/1/O/0 pairs, natural English retention, all printable ASCII and seven negative categories. Existing development/public evaluation fonts never enter training. Prior synthetic manifest/cache and repository study strings/hashes are exclusion data only. An additional preoptimizer audit explicitly checks old code-v1/code-v2 and current room-candidate oracle literals.

Development uses new RedHatMono, OverpassMono, SourceSerif4 and Literata; the once-only fresh comparison reserves SplineSansMono, MartianMono, Newsreader and Fraunces. Both retain original whole-line baseline, relative heights, gaps and all nonwhite pixels before BGR48 normalization. The new eight fonts and OFL files are pinned to google/fonts revision `94301472b4d809299b4775bc6502592b83dd6934`; training font bytes retain their previous pinned revision. Data split seeds, counts, optimization and practical selection criteria are frozen in protocol.json. Development becomes consumed after selection. Only selected-hash-frozen fresh evaluation may support an independent result, and it does not establish real-document accuracy.

CPU 2 threads, batch 2, two epochs; 30-minute training ceiling, 4-GiB own RSS, 1-GiB additional scratch ceiling and 1.5-GiB free reserve. Old artifacts are retained. `train.py` refuses an existing freeze; `fresh.py` refuses an existing fresh freeze and does not generate fresh data when the original active model wins development. A failed/no-improvement candidate is a valid outcome.

Run in the research Python environment, with the external model/font assets present:

```sh
python download_fonts.py
python train.py
# Only after immutable selection and if a trained candidate wins:
python fresh.py
```

The frozen protocol inherits descriptive fields from the prior font-balanced experiment. For this continuation, `initialization`, `cache`, `selection`, `continuationGate`, `final`, and the executable checkpoint loading are authoritative: “official teacher” means the active official-architecture-derived checkpoint, not the unadapted official weights. This clarification does not change the frozen protocol or training source bytes.

Portable export, if justified by the frozen fresh comparison, is a separate stage with standard ONNX FP32, full438 dictionary, `x` BGR float32[B,3,48,W], `valid_width` int64[B], full CTC probabilities and reject probability. It requires Torch/ONNX decisions and CTC interval comparisons, multiple dynamic widths/batches, and original active ONNX fresh comparison. CoreML/iPhone inference is outside this trial; iOS source stays untouched.
