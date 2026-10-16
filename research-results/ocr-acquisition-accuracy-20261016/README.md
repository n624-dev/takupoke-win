# Original paired native trial: quality passed, nonregression failed

[Windows run38831256353](https://github.com/n624-dev/takupoke-win/actions/runs/38831256353), immutable source `b48f67c8a6ec6c8b9a16f02b1bf3d6d10e8ea617`. Both actual readers closed all18documents. All6positives retained120slots/360fields, all12negative documents completed acquisition and refused. RSS stayed below6GiB and original-image review/correction budgets were met. **The original trial still FAILS per-font nonregression; no default activation followed.**

| Font | Historical exact /360 | Candidate exact /360 | Historical character edits | Candidate edits |
|---|---:|---:|---:|---:|
| Sawarabi Gothic |329|330|31|30|
| Sawarabi Mincho |337|337|23|23|
| BIZ UDP Gothic |0, acquisition failed|343|unscored|17|
| Zen Maru Gothic |0, acquisition failed|354|unscored|7|
| BIZ UDP Mincho, new at original trial |0, acquisition failed|351|unscored|12|
| MPLUS2 regular wght400, new at original trial |328|332|35|55|

Missing fields are explicit, and coverage-aware edits count every absent reference character as a deletion. Unscored conditional CER is not zero error. The lastfont has4more exact fields but20more character edits, so the registered nonregression gate failed. Both formerly newfontfiles/literalsets are consumed after this closed trial. No threshold/checkpoint was selected from them.

Diagnosis found height-dependent reading-order inversion in several original inline-role rows: correctly read `架空室3B` became `3B架空室` when a shorter specialist crop changed its vertical centre. Original glyphs, confidence and SourceOrder were already correct. Three actual I/l expert substitutions also remain; no error was removed from the original report. The subsequent generic proved-row ordering fix is a separate consumed development result in [the ordering records](../ocr-source-order-development-20261016/README.md).

`study/report.json` is the original unchanged failed report. `raw-reassembly.json` restores every exact original JSONL stream from deterministic per-document gzip segments. Original artifact bytes:10,257,496, SHA256`b08e40c355d585cb138ad14f48ffee5b4c66ff1f4ab2a2a23ec3e2778b000943`; all78artifact manifest entries were verified before diagnosis. Original full-page BGRA is excluded from public source. No private PDF, weight binary or internal policy file is included.
