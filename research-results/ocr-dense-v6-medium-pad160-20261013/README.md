# Reduced normalized-zero padding: closed, no reading improvement

Consumed two complete positive PDFs, V6 medium / Ink2px / native0.8 / greedyCTC.
Every original resized FP32 value and validWidth remains unchanged. Only excess
normalized-zero right padding is removed to minimum160 rather than320. No new
resize/crop, output-clamping, ink change or text correction. Source/crop/framing
identity is identical to the separately closed full6 measurement.

All4350 recognizer calls complete. Input width changes135/2175 lines in Shippori
and130/2175 in MPLUS, but literal changes0 and below-floor decision changes0 in
both. These counts are a separate zero-call comparison of closed native outputs.
Formal adoption fails in both:680 cells/134 low vs600 cells/318 low. Shippori room
literal exact501/680 with179 wrong/169 abovefloor; MPLUS actual source slots omit
80 and receive no gold-driven body accuracy credit. No deployment improvement.

No heldout, negative-control, Windows-device or production qualification. App
preprocessing is unchanged; this research routine is not enabled in app. Full
native raw remains unchanged in owned local storage; frozen identity/receipts
and summaries retained here. Original image samples are independently proven.

Native raw SHA256 20ef707d7cfa221a1845f9da051be443a05522ab0f0f80a98226a11b77bcdb99 .
