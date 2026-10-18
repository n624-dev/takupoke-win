# Initial GOT-OCR2 initialization failure, unassessed

Transformers5.19 does not implement SDPA for this GOT architecture. Initialization
failed before inference: zero native predictions, no accuracy or refusal credit.
The separately preserved next arm uses the supported eager attention backend.
Neither failed execution nor a supported-backend repair establishes model quality.
