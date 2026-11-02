# Phase 2 — Deterministic Diagnosis

## Scope

This phase adds a UI-free deterministic diagnosis engine and initial rules for:

- `CS0246`: unresolved type or namespace.
- `NU1605`: NuGet package downgrade.
- Runtime exceptions: initial recognition of common exception types.

Rules return a stable rule ID, root-cause hypothesis, evidence, confidence, and human-reviewable fix suggestions. Unknown diagnostics return `null`, allowing a later orchestration layer to consider other rules or AI. No AI provider is called from this engine.

## Safety boundaries

- The rules do not edit source or project files.
- Suggested `using`, project-reference, or package-version changes are guidance only; no concrete patch is generated without enough project/semantic evidence.
- `CanAutoApply` is `false` for all current rules. Phase 3 is responsible for validated patch previews, approval, checkpointing, atomic apply, and rollback.
- Confidence describes the rule's classification strength, not proof that a suggested change is correct.

## Verification

Run from the solution root:

```powershell
dotnet clean DebugPilot.sln
dotnet build DebugPilot.sln --configuration Release
dotnet test DebugPilot.sln --configuration Release
```

The Phase 2 gate requires the golden/classification tests to pass, a clean build with warnings treated as errors in Core, and no skipped tests without a documented reason. This environment did not have the .NET SDK available during artifact preparation, so the build and tests must be verified on the development machine.
