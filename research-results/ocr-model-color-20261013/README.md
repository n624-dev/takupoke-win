# 2026-10-13: Same-pixel OCR comparison and CTC decoding

New fictional recognition-component corpus, not a whole-document benchmark.
Implementation: `659aa1bcce02af8de705e3652f063f9b921ce0f5`.
The generator, model revisions/artifact bytes, matching dictionaries, source
font bytes, original pixels and oracle were frozen before native inference.
Each font has 72 BODY rows, 21 class/lookalike headers and three blank controls.
The 14/18/22-pixel sizes, four colors and fixed all-ink-plus-four-pixel-margin
crop are paired conditions. They are not independent document successes.

Linux completed all 768 scheduled recognizer calls with no retries, detector,
language model, PDF acquisition or adoption. Raw output was flushed and hashed
before the oracle was opened for scoring. The raw, frozen execution identities
and per-kind aggregates are retained alongside this report, with inventory hashes.
Raw output SHA-256:
`3feda5fcee1f622a9ca59e662d8c6b3271308dc6e058be1cd0550a5406ed1eff`.

| Font / model / color | BODY literal exact /72 | Incorrect BODY above 0.8 | Header exact /21 | Unsupported padding, all rows |
|---|---:|---:|---:|---:|
| Sans / V5 / BGR |46|16|18|3|
| Sans / V5 / gray |46|18|18|3|
| Sans / V6 / BGR |54|7|19|0|
| Sans / V6 / gray |54|7|19|0|
| Serif / V5 / BGR |44|23|19|3|
| Serif / V5 / gray |43|23|19|3|
| Serif / V6 / BGR |57|9|21|0|
| Serif / V6 / gray |55|10|21|0|

Raw literal exactness does not depend on the confidence/padding guard. Conversely,
the above-floor counts require a nonempty output and supported source intervals.
V5's three padding failures per condition are the blank controls, with unwanted
text and zero confident above-floor readings; V6 returns empty output for those
controls. Neither empty output nor OCR agreement proves a production cell blank.

On nonblank BGR rows, V6 repaired ten and regressed one literal for Sans; it
repaired fifteen and regressed none for Serif. High-confidence wrong BODY readings
still remain in both cohorts, so V6 is not qualified or activated. Grayscale is
also not activated: it leaves the Sans V5 literals unchanged while increasing
above-floor errors, and regresses Serif literals in both recognizers. No threshold,
punctuation, whitespace, case or field value is repaired. Unicode-scalar CER and
per-kind uncertainties are reported separately in `linux-report.json`.

## Production improvement

The application now uses one CTC scan for probability validation and argmax,
with the dense row-major buffer when available. Nonstandard tensor layouts keep
logical indexing. Every losing probability remains validated; first-token tie
order, CTC blank, printed spaces, repeated-token maximum confidence and source
intervals are unchanged. Cancellation is checked before, during and after
decoding. The production V5 alphabet, model manifests and 0.8 guard remain fixed.

For **all 768 actual native tensors**, former and new decoders produced identical
pieces, text, intervals and float confidences. Three fixed decoding repetitions
per tensor alternated the timing order after prior unmeasured decoding. Aggregated
mean-per-tensor decode times were 8,361.10 ms former / 1,642.71 ms new, about
5.09 times faster for this component. No extra recognition was performed for
the CTC comparisons. This is not total application speed; model inference and
PDF/detector work are separate.

The worker used ONNX Runtime 1.23.2, two intra-op threads and one inter-op thread
on Linux x64. Its observed peak working set was 318,537,728 bytes, not a required
Windows/device RAM figure. Native runtime SHA and exact .NET/Python versions are
in the report. V6 took substantially longer for inference despite faster CTC;
model weights alone do not establish practical performance.

Linux Core288, Integration886, the four Python comparison tests and thirteen
package-notice tests passed. The cancellation regression uses a deterministic
in-process cancellation point after the first decoded row; an initial timer-based
test was replaced after its full-suite run exposed a thread-pool scheduling race.

The corrected native Windows comparison at `c158d11` completed all768 calls in
[Actions38343383422](https://github.com/n624-dev/takupoke-win/actions/runs/38343383422).
Every native tensor again produced identical old/new decoder pieces. Timing sums
were8647.38ms former /1391.26ms new, about6.22× for CTC only. The Windows report
retains native runtime identity and303,001,600-byte process peak working set.
The initial Windows run38342832269 failed terminal runtime identification after
the recognition loops and earns no quality credit; its refusal/cleanup receipt
is retained. The corrected tool captures module identity before recognition while
the session is alive, with case-insensitive Windows filenames.

**Cross-OS recognition parity is not established.** The outcome digests and
several literal/floor counts differ. Public font identities match, but source
pixels are painted separately by each platform's Pillow/FreeType stack, and the
native runtimes also differ. File hashes include platform newline representation.
The retained evidence cannot isolate source rasterization from runtime probability
differences. Do not pool either cohort or call these byte-identical cross-OS input
measurements. A subsequent comparison must transport frozen original pixels and
assert their exact hashes before either reader, rather than repainting per OS.

The full application [CI38343386065](https://github.com/n624-dev/takupoke-win/actions/runs/38343386065)
passed all seven jobs, including both hosts' Core288/Integration886, native x64
Debug/Release and ARM64 builds and639 Windows UI checks. This
experiment does not qualify actual-PDF recognition, detection/ownership, role
binding, merges, parallel lessons, whole-document Validator or saved adoption.
All source data and output here are invented; downloaded model/font/pixel assets
are not committed. No Actions artifact or model cache is uploaded.
