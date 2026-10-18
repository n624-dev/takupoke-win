# PP-OCRv6 small on the original frozen pixels

Official Japanese-capable small recognizer, its own pinned18710-symbol dictionary,
the same192 consumed original BGR crops and their fixed gray pairs.384 native
requests each on Linux and Windows
[38347657690](https://github.com/n624-dev/takupoke-win/actions/runs/38347657690).
Both outcomes match exactly:
`5e21a663b575df72166641461e4d1c5c88a3c9a3df162c601526a5e265811a64`.

| Cohort/colour | BODY literal exact /72 | Wrong BODY above0.8 |
|---|---:|---:|
| Sans BGR |47|11|
| Sans gray |48|10|
| Serif BGR |53|12|
| Serif gray |55|12|

All blank controls are empty, but a Serif header has a wrong literal above0.8
in both colours. Room confusions remain. This is a lightweight component result,
not a qualified replacement. Detector, source/role/ink certification, full PDF
Validator and adoption remain unassessed. Production remains unchanged.
The tiny recognizer was excluded before inference because its6906-symbol alphabet
lacks required Japanese characters; no mismatched dictionary was used.

Peak process working set: Linux160,714,752bytes, Windows120,946,688bytes.
These figures cover the probe's recognition/runtime/decoder process only,
not a complete application or an isolated causal speed comparison.
The original reports, source identities and all local raw observations are
retained. No repeated V5/medium inference is counted as new study credit.
