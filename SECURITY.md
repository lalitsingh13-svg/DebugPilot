# DebugPilot AI Security Notes (Phase 5 implementation status)

- Deterministic analysis runs before AI; a deterministic diagnosis short-circuits the provider call.
- AI calls require explicit consent and an SHA-256 match for the exact approved context.
- Context is bounded and redacted; code, logs and exception text are treated as untrusted data in the provider prompt.
- Provider output is parsed into typed fields and checked for bounded lengths, finite confidence, and exact evidence substrings. AI output cannot carry an executable patch in the current result contract.
- `ResilientAiProvider` bounds retries, applies exponential backoff with jitter, propagates caller cancellation, and opens a circuit after repeated failures.
- Provider endpoint policy requires HTTPS for remote endpoints; HTTP is permitted only for loopback OpenAI-compatible endpoints.
- Credentials must come from OS/Visual Studio credential storage. That integration is not yet implemented by the host in this phase; do not persist keys in settings.
- Never apply an AI suggestion as a source edit. Any future patch must be previewed, explicitly approved, checked against safe paths and original-content hashes, checkpointed, applied atomically, and remain rollback-capable.

Outstanding before release: host consent/context-preview integration, credential-store integration, provider-specific token budgeting, complete AI evaluation (including false-confidence measurement), broader injection/fault corpus, dependency audit, telemetry schema review, signed VSIX, and VS 2022/2026 installation tests. See `docs/PHASE-5-AUDIT.md`.
