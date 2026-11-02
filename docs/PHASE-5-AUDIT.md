# Phase 5 Master-Prompt Audit and Remediation

Audit basis: `02_DebugPilot_AI_v2.md` sections 5, 8, 9, 12, 14 and 16.

## Changes in this remediation package

- Added `ResilientAiProvider`, used by `AiDiagnosisOrchestrator`, to enforce bounded retries, exponential backoff with jitter, caller-cancellation propagation, structured-response/evidence validation before acceptance, and a circuit breaker.
- Added offline tests for retry-after-invalid-output, circuit-breaker blocking, and cancellation.
- Added Windows Credential Manager storage for API keys; provider settings persist only non-secret configuration in the current user's LocalAppData folder.
- Added an explicit, unchecked-by-default consent dialog that displays the exact redacted, bounded context for each AI request.
- Wired the AI fallback into the tool window only after deterministic diagnosis returns no match, and wired the per-session usage counter to the UI.
- The provider contract returns explanatory diagnosis only; it cannot return executable patches. This is intentionally safer than treating provider output as a patch.

## Audit status (do not infer completion from a successful build)

| Requirement | Status | Evidence / remaining work |
|---|---|---|
| Deterministic-first orchestration | Implemented in core | `AiDiagnosisOrchestrator` returns before provider invocation when deterministic diagnosis exists. |
| Consent and approved-context hash | Implemented in core | Checked before the provider is called. |
| OpenAI-compatible, Azure OpenAI, Anthropic transports | Implemented in core | `HttpAiProvider`; local OpenAI-compatible endpoints use the same adapter. Live smoke tests remain unrun. |
| Response bounds and evidence/schema-shape checks | Partially implemented | Response size, field limits, confidence and exact evidence are validated. Provider API JSON-schema enforcement is not uniform; Anthropic currently relies on the prompt and local validation. |
| Bounded retries/backoff/jitter and circuit breaker | Added in this remediation | Offline tests added; actual build/test must be run locally. |
| Token budgeting/estimation per provider/model | Incomplete | Context has a UTF-8 byte budget, but provider-specific token estimation/model limits are not implemented. |
| Credential store integration | Implemented in this iteration; runtime verification pending | Host uses Windows Credential Manager through Win32 APIs. Settings JSON stores provider endpoint/model only; API keys are not serialized. Verify on Windows/Visual Studio. |
| Consent and exact Context Preview UI | Implemented in this iteration; runtime verification pending | Every AI fallback displays the exact bounded, redacted payload and requires an unchecked-by-default explicit consent checkbox before sending. |
| Visible usage counter in UI | Implemented in this iteration; runtime verification pending | AI & Privacy tab displays the per-session request counter and cap. |
| Injection and malformed-output tests | Partial | Recorded malicious-evidence and malformed-JSON tests exist; broader adversarial corpus and integration coverage are still needed. |
| AI evaluation metrics | Partial | Current fixtures measure response acceptance/rejection only. They do not measure fix-correctness or false-confident rate against a labeled dataset. |
| Compilation/in-memory validation of proposed AI patches | Deferred by design | Phase 5 AI output contains no patch. Any later patch path must pass the Phase 3 fix engine and validation gates before presentation/application. |
| Privacy/telemetry documentation and secret redaction corpus | Partial | Context redaction exists, but a complete published privacy/security and telemetry schema audit is still required. |
| VS 2022 and VS 2026 experimental-instance tests | Not verified | User-reported build/test results do not establish host compatibility or installation behavior. |

## Gate decision

**Phase 5 remains BLOCKED for full master-prompt acceptance.** This iteration adds host-side credential storage, per-request context preview/consent, and UI usage reporting, but these changes have not been compiled or run in this environment. Provider/model-specific token estimation, full AI evaluation metrics, broader injection/fault coverage, API-specific strict JSON-schema enforcement, privacy/security review, and live/host integration checks remain outstanding. Phase 6 must not begin until the Phase 5 gate is satisfied under the master prompt.

## Local verification commands

Run from the folder containing `DebugPilot.sln`:

```powershell
dotnet restore .\DebugPilot.sln
dotnet build .\DebugPilot.sln --configuration Release
dotnet test .\DebugPilot.sln --configuration Release --no-build
```

Then review test output for the new `ResilientProvider` tests and attach the actual output to the phase gate record. This environment did not have the .NET SDK available, so no build/test result is claimed here. The host-side additions in this iteration require a local Release build, all tests, and Visual Studio runtime checks.


## Update after Phase 5C

See `docs/PHASE-5C.md`. Redaction corpus, transport retry/redirect/timeout behavior, UI-thread blocking, cancellation, VS 2026 manifest targeting and documentation accuracy were addressed. **Phase 5 remains BLOCKED** until local build/tests and VS 2022/2026 runtime checks pass and the open items in that document are closed.


## Update after Phase 5D source changes

Added a conservative input-token budget guard, strict local JSON-shape validation (including rejecting unknown `patch`/extra fields), adversarial prompt-injection tests, and a raw-response offline evaluator for schema/validation failures, human-labelled diagnosis correctness, and false confidence. These are source changes only; the current environment has no .NET SDK, so they are not compiler/test verified here. The token estimator is approximate rather than provider-specific, and the current response contract contains no structured patch; therefore the master prompt's fix-correctness evaluation and full Phase 5 gate remain open. See `docs/PHASE-5D.md`.


## Update after Phase 5D.1 remediation package

- Complete-request token budgeting now includes both the fixed system instructions and the user prompt containing the approved context; output-token limits are configurable and sent to all supported transport shapes.
- Evaluation correctness is now calculated over schema-valid diagnoses. A regression fixture has explicit CI thresholds for schema failures, correctness, and false confidence.
- CI now runs a transitive NuGet vulnerability audit.
- These are source-level changes in the remediation ZIP. This environment does not have the .NET SDK, so compilation, tests, package-audit results, and Visual Studio host compatibility have not been independently verified.
- Still open: exact provider/model tokenizer profiles and UI token meter; broad independently reviewed evaluation corpus; explicit decision on whether versioned patch generation is in scope (current provider contract is explanation-only); privacy delete-all-data/retention controls; coverage threshold; live provider and Visual Studio 2022/2026 Experimental Instance verification.

**Gate remains blocked pending local verification and closure or formal scope approval for the remaining requirements.**
