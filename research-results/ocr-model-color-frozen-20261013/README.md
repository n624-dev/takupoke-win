# Original-pixel transport isolates cross-OS recognition

Source `ab4b19a` changes input transport only: the192 consumed original BGR crops
are decoded from pinned fictional PNGs and verified against their original BGRA
hashes before inference. The unchanged gray transform receives those same bytes.
All input/oracle JSON is canonical UTF-8/LF. No model, sampler, preprocessing,
confidence floor, expected literal or production decoder changed.

Both local Linux and native Windows [Actions38344961198](https://github.com/n624-dev/takupoke-win/actions/runs/38344961198)
completed768 calls. **Input, oracle and outcome digests match exactly**, including
text, every CTC interval, source widths, unsupported-padding and floor dispositions.
All per-kind quality aggregates also match. Outcome SHA:
`c03c7e890cd9ffc38486bee457107dde7bc96af2b2a19032580d1b1e7d98856c`.
Native float probabilities, timing and runtime binary hashes remain OS-specific.

This removes the earlier per-OS painting discrepancy from this fixed comparison.
It does not qualify the recognizers: BODY errors and high-confidence wrong
readings remain the same as the original Linux V5/V6 experiment. Original raw
source output, frozen identities, both reports and cleanup receipt are retained
with inventory hashes. No model/pixel cache is uploaded as an Actions artifact.

The examples remain consumed development data. Further quality improvements
require complete fictional PDFs, negative controls and unconsumed final cases.
This transport result cannot be counted as additional independent documents,
complete source/role certification, a model activation or saved adoption.
