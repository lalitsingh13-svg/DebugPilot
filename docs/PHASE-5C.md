# Phase 5C — Audit remediation

Fixes the blockers found when auditing the Phase 5B package against `02_DebugPilot_AI_v2.md`.

| # | Finding | Change |
|---|---|---|
| 4 | Redaction missed AWS/GitHub/`sk-`/JWT/Google keys, PEM blocks; `Authorization: Bearer x` left the token behind; quoted secrets with spaces were partly shown; truncation ran before redaction; no regex timeouts | New `SecretRedactor` (ordered rules, timeouts, **fail-closed**, idempotent). `ContextEngine` redacts first, then fits each section to the *remaining* budget; build output, stack traces and test failures keep **both ends** (errors are usually at the end). More sensitive-file rules. Debugger locals: max 20 lines × 200 chars and secret-named variables masked. |
| 6 | 401/400/404 were retried and counted toward the circuit breaker | `AiProviderHttpException`: only 408/429/5xx, network errors, timeouts, malformed/rejected responses are retried. Config errors fail fast and never open the breaker. `Retry-After` honored (capped at 30 s). |
| 7 | `HttpAiProvider` mutated `HttpClient.Timeout` (throws on a used client) | Per-request timeout via linked `CancellationTokenSource`; clear `TimeoutException`. |
| 8 | Redirects could forward `x-api-key`/`api-key` | `HttpAiProvider.CreateHttpClient()` disables redirects/cookies; provider also refuses any 3xx. Host uses it. |
| 5 | `JoinableTaskFactory.Run` froze VS during AI calls; no cancel | Non-blocking `async void` handlers, one `CancellationToken` end-to-end, **Cancel** button. |
| 2 | Guidance-only rules (no patch) prevented AI for CS0246 and all runtime exceptions | `Diagnosis.HasConcreteFix`. AI is skipped only when a rule produced a concrete patch. For guidance-only results the user can click **Ask AI to go deeper** (still consent-gated; result shown as an addendum). |
| 3 | Manifest targeted `[17.0,18.0)` — not installable on VS 2026 | `[17.0,19.0)` + `amd64` for all four editions. |
| — | Static session state | Usage cap, shared `HttpClient`, and a long-lived `ResilientAiProvider` (circuit breaker) now survive tool-window reopen. |
| — | Docs contradicted the code | `PRIVACY.md`, manifest description and README rewritten. |

## Tests added (offline)
`SecretRedactorTests`, `ContextHardeningTests`, `TransportTests`, plus orchestrator tests for concrete-fix vs guidance-only.

## Still open (do not claim Phase 5 complete)
- **Not yet verified on a real machine:** VSIX build, `dotnet test` with real xunit, VS 2022 and VS 2026 experimental-instance runs.
- Finding 1: no rule produces a real patch and the patch engine is not wired to the UI (Phase 6).
- Finding 9: `System.Text.Json` inside a net472 VS 2022 extension — see `ADR-002`.
- No automatic Error List / build / exception / test capture; no Roslyn analysis.
- History not persisted or grouped; no delete-all-data/retention; no token-level budgeting; no live provider smoke test; no strict JSON-schema enforcement for Anthropic.
- Marketplace assets (icon, license, screenshots, signing).
