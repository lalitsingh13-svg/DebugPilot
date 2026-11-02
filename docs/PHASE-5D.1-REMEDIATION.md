# Phase 5D.1 Remediation Notes

## Changes
1. Count the full system prompt and user prompt in the input-token estimate before sending any provider request.
2. Add configurable `MaxOutputTokens` and pass it to OpenAI-compatible, Azure OpenAI, and Anthropic request payloads.
3. Correct offline evaluation denominator: diagnosis correctness is measured among schema-valid responses.
4. Expand the checked-in offline fixture to eight cases and assert CI regression thresholds.
5. Add a transitive NuGet vulnerability audit to the Windows CI workflow.

## Safety boundaries
- AI output remains explanation-only; no AI-generated patch is automatically applied.
- The token estimate is approximate and not a provider/model-specific tokenizer.
- The checked-in evaluation set is a regression fixture, not a statistically representative quality benchmark.

## Local verification required
Run in PowerShell from the folder containing `DebugPilot.sln`:

```powershell
dotnet restore .\DebugPilot.sln
dotnet build .\DebugPilot.sln --configuration Release
dotnet test .\DebugPilot.sln --configuration Release --no-build
dotnet list .\DebugPilot.sln package --vulnerable --include-transitive
```

Then run the VSIX in Visual Studio 2022 and 2026 Experimental Instances. Do not claim the phase gate is passed until the command outputs and runtime results are reviewed.
