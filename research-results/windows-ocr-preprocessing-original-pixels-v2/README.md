# Fixed OCR preprocessing: one development measurement

After the bounded resize/padding/source-coordinate fix in `c2fcdb5`, the same two
fictional ordinary PDFs produced **0/2 complete recoveries**. Both readable
positives were refused. Incorrect Validator acceptances and operational errors
were zero; no document reached Builder, Validator, or formal conversion. There
are no negative documents in this measurement and no model qualification.

The actual production `OnnxJapaneseOcr.Read` ran once on each of ten original
pages, with the existing hash-pinned detector, recognizer, dictionary, and
unchanged thresholds. No new weights were downloaded. The lossless embedded
PDF pixels were independently verified against the original PNGs. These are
**original image pixels, not Windows PDF-rendered pixels**. Actual native and
managed ORT were 1.23.2; historical Windows research used a Foundry-linked 1.26
runtime. The actual current Windows app dependency restore also resolves
`OnnxRuntime.Managed/1.26.0` and `OnnxRuntime.Foundry/1.26.0`, while Infrastructure
alone declares1.23.2. The Linux measurement does not prove Windows OCR runtime
compatibility. The recorded source/runtime/pixel differences prevent causal accuracy
or latency comparisons against that historical run.

For original 乙, all five page reads returned text, including the eight period
headings and the three literal page-one body fields. The original-ink guard
still rejected each page. A posthoc replay of the exact mask semantics finds
10 uncovered pixels on page one and four on each later page, at the left edge
of `2027年度 前期`: x=5..6/y=12..25 on page one, x=5/y=13..25 later.
These original pixels fall outside the detector's recognized boxes and physical
rule mask. This establishes the refusal's coverage cause; boxes were not
inflated and uncovered ink was not ignored. It does not establish full formal
correctness of the returned text or all source coordinates.

For original 丙, all five reads threw the existing unreadable-character guard.
The production API does not expose the failing internal CTC pieces, so empty
CTC versus low confidence and the particular crop remain unknown. No diagnostic
re-inference was performed. Neither original produced a padding-boundary error
in this run. Partial returned pages do not count as recovered documents.

`native-results.json` retains returned glyphs/geometry, exact errors, page and
source hashes, runtime identities and the complete aggregate. `native-console.json`
retains the exact console bytes as base64 plus SHA-256. The archived driver and
preparation code retain their as-run absolute paths for forensic identity; they
are not a portable installer or production runtime. The input manifest contains
only original file/pixel identity and dimensions, plus a separately hashed
post-inference oracle path. Literal gold is opened after native calls and formal
conversion; it never enters OCR. The independent full-formal checker is archived,
but was not reached because original-ink/character guards refused both documents.

The exact source passed all seven jobs of normal CI
[37211245937](https://github.com/n624-dev/takupoke-win/actions/runs/37211245937):
Core 270 and Integration 357 on Linux and Windows, x64 Debug/Release and ARM64
builds, and 537 native Windows UI checks. CI success verifies source regressions,
not OCR-model quality. Peak evaluator working set was 424,050,688 bytes; this is
not minimum-device evidence. This finite comparison is complete; no additional
model, threshold or preset loop follows from these results.
