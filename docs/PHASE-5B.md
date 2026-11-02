# Phase 5B — Credential Storage and Consent UI

## Implemented in this package

- API keys are stored using the current Windows user's Windows Credential Manager.
- Provider endpoint/model/deployment settings are stored separately under `%LOCALAPPDATA%\DebugPilot\ai-provider.json`; the settings serializer explicitly clears the API-key property before writing.
- AI configuration is not considered active until the user explicitly saves provider settings.
- Deterministic diagnosis runs first. AI is considered only when no deterministic rule matches.
- Each AI attempt presents a modal context preview with the exact redacted payload and an unchecked-by-default consent checkbox. Cancel means no request is sent.
- The UI displays a per-session AI diagnosis request counter and cap.
- AI responses remain explanations and review steps, not executable patches. No source files are changed.

## Local validation gate

Run from the extracted `DebugPilot` folder in a Developer PowerShell with the .NET SDK and Visual Studio build tools installed:

```powershell
dotnet restore .\DebugPilot.sln
dotnet build .\DebugPilot.sln --configuration Release
dotnet test .\DebugPilot.sln --configuration Release --no-build
```

Then install the generated VSIX in a Visual Studio experimental instance and verify:

1. The AI & Privacy tab loads and settings persist after restarting Visual Studio.
2. A saved API key appears in Windows Credential Manager and is absent from `ai-provider.json`.
3. With settings not saved, no consent dialog and no AI request occur.
4. With configured settings and a deterministic no-match diagnostic, the preview displays the exact payload.
5. Cancel sends no request; consent sends only the shown payload.
6. Invalid/missing credentials, network failure, malformed output, and request-cap exhaustion produce a safe UI message.
7. The usage counter increments for accepted AI diagnosis attempts.

## Remaining Phase 5 blockers

- Provider/model-specific token estimation and context limits (the current host uses a conservative UTF-8 byte budget).
- Full AI evaluation dataset measuring fix correctness and false confidence.
- Broader prompt-injection and transport fault tests.
- Uniform provider API JSON-schema enforcement and live provider smoke tests.
- Dependency/security review and Visual Studio 2022/2026 runtime compatibility tests.

This environment does not have `dotnet`, `msbuild`, or `csc`, so this package is not represented as compiled or runtime-verified here. Phase 5 remains open until the local gates above pass.
