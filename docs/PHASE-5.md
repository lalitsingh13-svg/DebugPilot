# Phase 5 — AI providers and structured diagnosis

## Deliverables

- `IAiProvider` returns a structured `AiDiagnosisResult` without executable patches.
- `HttpAiProvider` supports OpenAI-compatible chat completions, Azure OpenAI chat completions, and Anthropic Messages.
- `AiDiagnosisOrchestrator` refuses AI calls when deterministic diagnosis already exists, when consent is absent, or when the approved context hash has changed.
- `AiResponseValidator` enforces bounded fields, finite confidence in `[0,1]`, and evidence that exactly matches the approved context.
- `AiUsageGuard` caps AI requests per session (default five) and exposes counters for the future UI.
- Offline tests cover evidence mismatch, invalid confidence, deterministic short-circuit, consent, changed context, accepted evidence-backed output, and a recorded-response evaluation suite.

## Plan / changed files

- `src/DebugPilot.Core/Abstractions.cs` — typed AI provider contract.
- `src/DebugPilot.Core/AiDiagnosisModels.cs` — provider configuration, consent and safe result DTOs.
- `src/DebugPilot.Core/AiDiagnosisOrchestrator.cs` — deterministic-first, consent/hash-gated orchestration.
- `src/DebugPilot.Core/AiResponseValidator.cs` — schema and evidence validation.
- `src/DebugPilot.Core/HttpAiProvider.cs` — HTTP adapters and response size/error handling.
- `src/DebugPilot.Core/AiUsageGuard.cs` — in-memory per-session request cap.
- `tests/DebugPilot.Core.Tests/AiDiagnosisTests.cs` — offline unit tests.
- `Directory.Packages.props`, `src/DebugPilot.Core/DebugPilot.Core.csproj` — JSON serialization dependency.

## Risks and limitations

- This is the core provider layer, not a completed UI integration. Provider settings, secure credential-store retrieval, consent dialog, usage counter, and settings persistence are host responsibilities and remain deferred.
- AI confidence is not authorization to apply a change. The provider schema cannot return a patch; any future patch proposal must use a separate validated contract and existing preview/approval/checkpoint/rollback path.
- Evidence matching is deliberately strict and can reject paraphrased evidence. A rejected response should be shown as unavailable and must not be treated as a fix.
- Endpoint validation requires HTTPS for remote endpoints and permits HTTP only for loopback OpenAI-compatible endpoints. Users should only configure endpoints they trust.
- No live provider call is performed by tests. Live API smoke tests require explicit user consent and a credential supplied by the user.

## Gate checklist

- [ ] `dotnet restore DebugPilot.sln` succeeds.
- [ ] Release build has 0 errors and 0 warnings.
- [ ] All tests pass, including `AiDiagnosisTests` and request-cap enforcement.
- [ ] Prompt-injection and malformed provider response tests pass.
- [ ] Offline evaluation achieves 100% fixture classification accuracy (all invalid fixtures rejected, valid fixture accepted).
- [ ] Optional live provider smoke test succeeds with explicit consent.
- [ ] Host integration remains disabled until secure key storage and first-run consent UI are implemented.
