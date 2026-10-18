# Closed paired native Windows comparison — not adopted

Run [38824053198](https://github.com/n624-dev/takupoke-win/actions/runs/38824053198), source `4ff7d2c4c894c8748d9c6c4182d15dde2e962904`.

Both actual C# readers closed on all 12 fictional documents. The original prospective whole-document gate failed; the deployed bundle was not changed. This does not require zero OCR errors.

| Positive font | Baseline exact fields | Expert exact fields | Missing fields, both arms |
|---|---:|---:|---:|
| Sawarabi Gothic, consumed regression | 329/360 | 330/360 | 0 |
| Sawarabi Mincho, consumed regression | 337/360 | 337/360 | 0 |
| BIZ UDP Gothic, unused at original run | 0/360 | 0/360 | 360 |
| Zen Maru Gothic, unused at original run | 0/360 | 0/360 | 360 |

The BIZ document stopped in original connected-ink crop ownership before recognition. Zen Maru stopped on an unsupported CTC padding interval after four complete pages. The unchanged primary reader fails in both arms. No scored characters or fields on these positives is **not** zero character error. All eight negative documents refused, but the new-font negatives also have acquisition failures and cannot establish header-specific refusal.

The expert made one literal correction and no literal regression on the old full documents; correction plus mandatory-review unions were 35→33 and 25→23. High-confidence wrong fields were 24→2 and 23→23; confidence scores remain uncalibrated. These are consumed regression results, not independent success. Both formerly fresh fonts and literals are now consumed for future work.

`study/report.json` is unchanged. `raw-reassembly.json` records every exact original JSONL byte stream and ordered per-document gzip segments. Decompress and concatenate those segments in manifest order to reproduce the full raw hash. Original artifact metadata, source snapshots, receipts, oracle and all outcomes are retained. `original-artifact-curation-manifest.json` describes the original artifact paths, including raw streams represented here by gzip segments and the deliberately excluded fictional full-page BGRA.

Native Windows PDF rendering changed the generator pixels, so generator originals cannot substitute for the native raster in diagnosis. Neither PDFs, full-page BGRA, model binaries, private school data nor internal policy files are included here. Subsequent acquisition diagnosis must retain these failed results and use new fonts/literals for final independent confirmation.
