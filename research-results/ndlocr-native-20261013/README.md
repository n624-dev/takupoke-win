# Official NDLOCR-Lite PARSeq comparison, rejected

Pinned upstream d001dad2013ceabb5736ff8d637474e443eb73c2, source/recognizer CC-BY-4.0:
https://github.com/ndl-lab/ndlocr-lite . Attribute National Diet Library, Japan.
The detector (and its separate DINOv3 terms) was not used. Three native PARSeq
30/50/100 recognizers receive the same consumed 192 PNGs; CPU provider, one thread,
native BGR [-1,1] preprocessing and first-EOS autoregressive decoding, no CTC.
All 576 calls completed in 56.56 seconds. Body exact out of 72 is 46/44/46 Sans
and 47/45/46 Serif. Room exact is only 2–3/24, and all six white blanks hallucinate
`0.00` in every arm. Header exact out of 21 is 18/16/15 Sans and 12/10/8 Serif.
Minimum token softmax including EOS is uncalibrated and not a Paddle threshold.
No peak-memory measurement was collected; no rerun for a memory claim.
Raw SHA ace78fe99c246ac14bfae6ccb22deaedb391918201f1d94c099b263459ab054a.
The parent opened the separate oracle only after the worker terminated. No
selection, correction, full-document or new-independent-data credit. Rejected,
not activated. Freeze, native raw observations and receipts are immutable.
