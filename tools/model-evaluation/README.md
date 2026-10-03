This manual Windows-only probe evaluates the official fixed Foundry Local CPU variants `qwen3-0.6b-generic-cpu:4` and `qwen2.5-1.5b-instruct-generic-cpu:4` with SDK 1.2.4. It prints each local artifact's relative path, byte size and SHA-256, plus aggregate results for four entirely invented labeled cells.

The probe disables nonessential SDK and ONNX telemetry, does not start a web service, never reads application storage, and has no school URL, credentials, document input, upload or release step. Its unique temporary model cache is deleted in `finally`. Native SDK log files and exception messages are not printed, because downloads may include temporary signed URLs. No model cache or artifact is uploaded by the workflow.

This is a candidate evaluation, not a release manifest or evidence of timetable recovery accuracy. An approved artifact manifest and broader synthetic/real local validation are required before an app adopts a model.
