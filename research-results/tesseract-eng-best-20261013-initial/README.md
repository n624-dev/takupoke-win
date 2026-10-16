# Initial Tesseract execution failure, unassessed

The fixed 48-input arm failed after one native call because external `txt`/`tsv`
configuration files were absent in the owned tessdata directory. The worker
could not read the required output. No assessed raw rows, no accuracy or negative
credit. This failed invocation is preserved separately. The next arm changes only
output creation flags, never pixels, model, recognition or exact-text scoring.
