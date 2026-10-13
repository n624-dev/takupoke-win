# TrOCR initial integration, unassessed

This older official checkpoint lacks a composite processor_class. AutoProcessor
resolved its Roberta tokenizer, which cannot receive images. The attempt stopped
before any model.generate call; zero raw observations. Keep the frozen protocol,
source and error receipt. This is no model accuracy or rejection result. The
explicit TrOCRProcessor + official AutoImageProcessor/AutoTokenizer factory is
measured separately, not by rewriting this closed attempt.
