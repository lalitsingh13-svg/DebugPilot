# DebugPilot AI — Phase 6 (real fixes with verification)

DebugPilot AI is a Visual Studio extension (VS 2022 and VS 2026) that diagnoses compiler, build and runtime errors with a deterministic-first engine and optional, consent-gated AI analysis. This package contains the Core library, the VSSDK tool window, and unit tests. **Current status: see `docs/PHASE-5C.md` — Phase 5 is not yet fully verified or complete.**

## Requirements

- Windows with PowerShell 5.1 or PowerShell 7+
- .NET 8 SDK (the test project targets `net8.0`)
- Visual Studio 2022 is recommended for development; the current Phase 0 core can also be built with the .NET CLI.

## Open in Visual Studio

1. Extract the ZIP completely to a writable folder.
2. Open `DebugPilot.sln` from the extracted `DebugPilot` folder.
3. Allow NuGet restore to finish.
4. Select **Release** and **Build > Build Solution**.
5. Open **Test > Test Explorer** and run all tests.

## Automated setup from PowerShell

Open PowerShell in the extracted `DebugPilot` folder and run:

```powershell
.\setup.ps1
```

The script locates its own folder, restores NuGet packages, builds the solution in Release, and runs tests. It stops and reports the first failed command. If PowerShell says the script is not found, first `cd` to the folder containing `setup.ps1`.

Optional switches:

```powershell
.\setup.ps1 -SkipRestore  # use only if packages have already been restored
.\setup.ps1 -SkipTests    # build without running tests
```

If script execution is blocked by policy, for this PowerShell session only run:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\setup.ps1
```

## Projects

- `src/DebugPilot.Core`: core models, deterministic-rule and AI-provider contracts, error fingerprinting.
- `tests/DebugPilot.Core.Tests`: unit tests for error fingerprinting.
- `docs/ADR-001-extension-model.md`: proposed Visual Studio integration approach.

## Scope note

Phase 0 is a foundation only. It does not yet include a VSIX project, a Visual Studio tool window, a live Error List/build-event adapter, an AI service implementation, or automatic code fixes. Those require later phases.

## Build and test manually

```powershell
dotnet restore DebugPilot.sln
dotnet build DebugPilot.sln --configuration Release
dotnet test DebugPilot.sln --configuration Release --no-build
```

## Phase 1 — Context engine and redaction

`DebugPilot.Core.ContextEngine` builds a bounded, redacted context preview from diagnostic details and snippets explicitly supplied by the host adapter. It does not read files, traverse directories, scan the solution, or make network requests. The caller should pass only the current error's source excerpt and relevant project/package/build details.

- Default context budget: 12,000 UTF-8 bytes.
- Default source window: 30 lines before and after the error line.
- Sensitive source files (`appsettings*.json`, `secrets.json`, `.env*`, and `*.pfx`) are excluded from source excerpts.
- Credential-like assignments, bearer tokens, email addresses, and common user-home path prefixes are redacted.
- Debugger locals are excluded unless the user explicitly enables them for the session.
- `ContextPreview.Text` is the exact bounded representation intended to be shown in the context-preview UI and, only after consent, sent to a configured provider.

The Phase 1 core API does not itself transmit context or implement the Visual Studio preview UI. Those host/UI integration tasks remain for the appropriate VSIX phase. Run `dotnet build DebugPilot.sln -c Release` and `dotnet test DebugPilot.sln -c Release` in an environment with the .NET SDK to verify the gate.
## Deterministic diagnosis (Phase 2)

The Core diagnosis engine includes initial rules for CS0246, NU1605, and common runtime exceptions. It produces evidence-backed hypotheses and reviewable suggestions only; it does not modify files or auto-apply fixes. See [`docs/PHASE-2.md`](docs/PHASE-2.md).


## Phase 4 — Visual Studio tool window

The `src/DebugPilot.Vsix` project adds a VSSDK tool window reachable from **Tools → DebugPilot AI**. It contains Current Error, Root Cause, Suggested Fix, Diff Preview, Verification, and Error History panels. In this phase, diagnostics are entered manually and passed to the deterministic engine; there is no automatic Error List capture, AI provider call, or source edit. Follow [`docs/PHASE-4.md`](docs/PHASE-4.md) for build, Experimental Instance, and accessibility verification steps. Do not consider the Phase 4 gate passed until those checks are run locally.

## Phase 5 — AI providers and guarded diagnosis

Phase 5 adds `IAiProvider`, a configurable HTTP adapter for OpenAI-compatible endpoints (including local loopback endpoints), Azure OpenAI chat completions, and Anthropic Messages, plus an AI diagnosis orchestrator. AI is an optional fallback: a non-null deterministic diagnosis short-circuits AI. The orchestrator requires explicit user approval for the exact context hash, then validates the structured response and requires each evidence item to be an exact substring of that approved context.

**Privacy and safety boundaries**

- No provider is invoked unless the host has already obtained explicit consent for the current context. The host must build the preview through `ContextEngine` and show the exact preview to the user.
- The host must obtain cloud API keys from the Visual Studio/Windows credential store and pass them at runtime. Do not save keys in source, settings, logs, or this project. Unauthenticated HTTP is accepted only for loopback OpenAI-compatible local endpoints.
- The AI result schema has explanation, evidence, suggested actions, and confidence only; it has no executable patch field. AI suggestions must be reviewed by the user. Existing patch preview/approval/checkpoint/rollback remains the only patch workflow.
- Provider error response bodies are not copied into exception messages or logs. Responses are size-limited and requests are cancellable. The core applies a configurable in-memory per-session request cap (default five); the host should display its usage counters.
- This phase provides the provider/core APIs and offline unit tests. The Phase 4 tool window does not yet include provider selection, secure credential-store integration, first-run consent UI, or live AI invocation; do not assume those UI integrations are complete.

### Provider configuration notes

- OpenAI-compatible/local: `Endpoint` is the base URL or full `chat/completions` URL; `Model` is required. Local loopback HTTP is allowed; remote endpoints require HTTPS.
- Azure OpenAI: `Endpoint` is the Azure resource URL, `Deployment` is required, and `ApiVersion` can override the default.
- Anthropic: `Endpoint` is the API base URL or full `messages` URL; `Model` is required.
- `ApiKey` is runtime-only configuration. Integrators must retrieve it from a credential store and never serialize `AiProviderOptions`.

### Phase 5 gate

Run `dotnet restore DebugPilot.sln`, `dotnet build DebugPilot.sln -c Release`, and `dotnet test DebugPilot.sln -c Release`. The AI unit tests are offline and do not require provider credentials. The phase is not considered fully passed until these commands pass locally, prompt-injection and malformed-response tests pass, and a separate live-provider smoke test is run only with user consent.

## Phase 5 host integration update

The AI & Privacy tab stores API keys in Windows Credential Manager, keeps provider settings separate from secrets, displays the exact redacted context in a consent dialog before each AI request, and shows the per-session request counter. AI is attempted only when deterministic diagnosis returns no match and provider settings have been explicitly saved. These host changes are not considered verified until a local Release build, tests, and Visual Studio runtime checks pass.

## Phase 5 audit status

See `docs/PHASE-5-AUDIT.md` for the master-prompt compliance audit and remaining release blockers. `PRIVACY.md` and `SECURITY.md` document current boundaries and deferred host integrations. A clean build is necessary but does not by itself satisfy the full Phase 5 gate.


## Phase 5C — audit remediation

See [`docs/PHASE-5C.md`](docs/PHASE-5C.md) for what changed and what remains open, [`PRIVACY.md`](PRIVACY.md) for current data handling, and [`docs/ADR-002-json-in-process.md`](docs/ADR-002-json-in-process.md) for the open in-process JSON risk. Run the gate (`dotnet restore/build/test`) locally, then verify the VSIX in VS 2022 and VS 2026 experimental instances.


### Phase 5D update
Strict local AI response-shape validation, an estimated input-token guard, adversarial context tests, and offline evaluation metrics are included in `docs/PHASE-5D.md`. Phase 5 remains open pending local build/test and host/provider verification.


## Phase 6

See [`docs/PHASE-6.md`](docs/PHASE-6.md): add-using fixes with diff preview, approval, checkpoint, rebuild verification and rollback.
