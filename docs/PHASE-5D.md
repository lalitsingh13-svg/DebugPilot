# Phase 5D — Structured output, token guardrails, offline evaluation

## Phase 5D.1 remediation in this package

- The input budget now estimates the complete system prompt plus user prompt and approved diagnostic context before the HTTP request is sent. The prior context-only check could undercount the system instructions.
- `AiProviderOptions.MaxOutputTokens` now controls the output-token reservation requested from OpenAI-compatible, Azure OpenAI, and Anthropic transports. The default remains 1,800; accepted configuration range is 128–32,000.
- The token estimate remains a conservative UTF-8 heuristic with a fixed framing allowance. It is not an exact provider/model tokenizer and does not claim provider-specific context-window knowledge.
- The offline evaluator now reports diagnosis correctness over schema-valid diagnoses (rather than over all cases, which incorrectly penalized invalid responses twice), and exposes the count of valid diagnoses.
- The evaluation regression fixture was expanded and CI tests enforce explicit fixture thresholds: schema failure rate <=25%, diagnosis correctness >=65% among schema-valid cases, and false-confidence rate <=25%.
- CI now runs a transitive NuGet vulnerability audit after build and tests.

## Implemented behavior and remaining limits

- `HttpAiProvider` locally enforces an exact JSON shape: only `rootCause`, `evidence`, `suggestedActions`, and `confidence` at the root; each action may contain only `title`, `rationale`, and `steps`. Duplicate, missing, unknown, or wrongly typed properties are rejected. Provider output cannot contain executable patch data.
- The system prompt treats source/log/exception text as untrusted data and forbids patches, commands, and unsupported claims.
- `AiOfflineEvaluator` measures schema/validation failures, human-labelled diagnosis correctness, and false confidence using recorded responses.
- The regression corpus in this repository is still a small fixture, not a broad independently reviewed corpus. The thresholds only prevent regressions against these checked-in cases; they do not establish production accuracy.
- The UI still does not expose a model-profile selector or visible estimated-token meter. The estimate is deliberately conservative and must not be represented as exact.
- The current AI result contract returns explanations and suggested steps, not versioned structured patches. This is a deliberate safety constraint in the current implementation, but it means patch correctness cannot be measured. If patch generation is required by the master prompt, it needs a separate reviewed design wired through the existing patch-validation/preview/approval pipeline; raw AI patches must never be applied directly.
- Privacy delete-all-data and retention controls remain unimplemented and must be addressed before a Marketplace release.
- No live-provider request, .NET build/test, coverage run, or VS 2022/2026 Experimental Instance test has been executed by this packaging environment. Run the local commands below and retain the actual outputs.

## Local gate commands

Run from the folder containing `DebugPilot.sln`:

```powershell
dotnet restore .\DebugPilot.sln
dotnet build .\DebugPilot.sln --configuration Release
dotnet test .\DebugPilot.sln --configuration Release --no-build
dotnet list .\DebugPilot.sln package --vulnerable --include-transitive
```

Review all output and run the VSIX on experimental instances of both supported Visual Studio versions. Do not mark Phase 5 complete until these results and the outstanding master-prompt criteria are recorded.
