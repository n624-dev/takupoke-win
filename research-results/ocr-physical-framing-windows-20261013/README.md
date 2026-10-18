# Native Windows physical-input comparison: closed positive failure

Source d7d3ad225e87138b761fe37080e5f2d18e2acb72,
https://github.com/n624-dev/takupoke-win/actions/runs/38377484870 , job115229407176.
Actual strict reader reports raster eligibility for both lossless image-only PDF
containers. The OS renderer uses production dimensions, pixel format and colour
options. Windows page.Size uses device-independent units: rendered width1367,
not original drawing1025; actual native pixel hashes differ on all10 pages.
This is an observed acquisition effect, not identical pixels or a fresh corpus.
Both baseline and research arms refuse original connected-ink crop ownership
before recognizer calls. Positive exact0/2 in each arm, no wrongful result emitted.
The workflow completing successfully means a closed measurement and cleanup,
not successful recovery. Raw observations, source freeze, reader/render receipts
and exact post-worker report retained. Native raw SHA
1cdcd0c32382fa0d379ce6d6262172b1b6d8d979f2bec17f82db73360d50262f.
No manual/header fill, lowered confidence or CTC output clipping. Zero new
independent document credit or production qualification. A separate acquisition-only
run will preserve the synthetic native pixels to diagnose the boundary failure
without repeating model calls. The iOS repository and app OCR remain unchanged.
