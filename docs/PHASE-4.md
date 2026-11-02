# Phase 4 — Visual Studio Tool Window UI

## Included in this phase

- Adds a thin VSSDK host project (`src/DebugPilot.Vsix`) and a **Tools → DebugPilot AI** command that opens the tool window.
- Provides all six required panels: Current Error, Root Cause, Suggested Fix, Diff Preview, Verification, and Error History.
- Connects manual single-error input to the existing deterministic diagnosis engine. The UI reports the rule ID, evidence, confidence, and reviewable suggestions.
- Adds named controls, keyboard-operable standard WPF controls, panel labels, and text-based status messages.
- Keeps Phase 4 offline: no provider call, no automatic source scanning, and no source-file modification.

## Explicit limitations

- The UI currently accepts a diagnostic entered by the user. Automatic subscriptions to Error List, build completion, debugger exceptions, and Test Explorer are not implemented.
- The Diff Preview panel is an empty state unless a future host integration supplies a validated patch. Phase 4 does not apply patches.
- Error History is in-memory for the current tool-window session; persistent history belongs to hardening/integration work.
- This project uses the VSSDK as a thin in-process host. `DebugPilot.Core` remains UI-free and has no Visual Studio SDK reference.
- VSIX installation target in this package is Visual Studio 2022 (17.x). Visual Studio 2026 compatibility must be separately smoke-tested and the manifest range adjusted only after verification.

## Build and test on Windows

Run from the extracted `DebugPilot` directory in Developer PowerShell for Visual Studio 2022:

```powershell
dotnet clean .\DebugPilot.sln --configuration Release
dotnet restore .\DebugPilot.sln
dotnet build .\DebugPilot.sln --configuration Release
dotnet test .\DebugPilot.sln --configuration Release
```

Then in Visual Studio:

1. Open the solution and set `DebugPilot.Vsix` as the startup project.
2. Start debugging to launch the Experimental Instance.
3. Select **Tools → DebugPilot AI**.
4. Verify the six panels load and the window can be resized.
5. Enter `CS0246` and a message such as `The type or namespace name could not be found.`; select **Diagnose**. Verify the Root Cause panel displays a matching deterministic rule and evidence.
6. Try an unsupported code and verify the UI displays a no-match state.
7. Select **Clear** and verify the UI returns to Current Error.
8. Verify no source file is changed and no network call occurs.
9. Inspect with keyboard only and a screen reader; verify focus order, labels, high contrast, DPI scaling, and light/dark themes.
10. Install the generated VSIX in a clean VS 2022 instance and repeat the smoke test.

## Phase 4 gate

Do not mark Phase 4 complete until Release build has zero errors and zero warnings, all tests pass, the VSIX builds, the tool window opens in the Experimental Instance, the six-panel smoke test passes, and accessibility checks are recorded. The package creator cannot run Windows Visual Studio or the .NET SDK in this environment; the above local verification remains required.
