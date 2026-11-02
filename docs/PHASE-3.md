# Phase 3 — Safe Patch / Preview / Approval / Rollback Engine

## Scope

Phase 3 introduces a deterministic, UI-independent, single-file patch engine in `DebugPilot.Core`. It does not apply AI output or make any edits automatically.

## Workflow

1. Construct a `FixPatch` with a solution-relative or absolute file path, character offset/length, expected original text, SHA-256 of that exact original text, and replacement text.
2. Call `PreviewAsync`. The preview returns the full before/after content, before/after hashes, edit lengths, and a simple diff block. Preview is read-only.
3. Present the preview to the user and collect explicit approval in the host UI.
4. Call `ApplyAsync(patch, userApproved: true)`. The engine revalidates path, range, expected text, hash, and size; creates a persistent byte-for-byte backup; writes a temporary file beside the target; and replaces the target atomically with `File.Replace`.
5. Keep the returned `PatchCheckpoint`. Its metadata is persisted beside the backup; after a restart, call `LoadCheckpoint(checkpointId)` and then `RollbackAsync(checkpoint)` to restore the exact original bytes. Rollback refuses to overwrite a file that changed after the patch was applied.

## Security controls

- Rejects targets outside the configured solution root.
- Rejects path components marked as reparse points/symbolic links.
- Enforces expected-original-text and SHA-256 matching to detect stale proposals.
- Enforces a configurable patch payload size limit (default 256 KiB).
- Does not write without an explicit `userApproved` value of `true`.
- Persists original file bytes before replacement so recovery data survives process restart; backup deletion/retention is intentionally not automatic.
- On an apply exception, attempts to restore the original bytes and preserves backup data if restoration fails.
- Rollback verifies the current target matches the applied content before replacing it, protecting later user edits.

## Current limitations / deferred work

- The engine currently applies one text patch to one existing file per transaction. Multi-file transactions and cross-file atomic rollback are deferred until their transaction semantics and fault tests are designed.
- The engine persists checkpoint metadata and backup bytes so the host can restore rollback controls after restart by retaining the checkpoint ID. The host still needs to expose checkpoint history in its UI.
- `DiffText` is a minimal changed-text preview, not a full context-rich unified diff. A richer diff renderer belongs in the UI phase.
- `File.Replace` requires a filesystem that supports atomic replacement; unsupported filesystems will fail closed rather than fall back to a non-atomic overwrite.
- This phase does not yet compile the proposed result in-memory or run post-fix builds/tests; that belongs to the validation-loop phase.

## Phase 3 gate

On a Windows development machine with the .NET SDK installed:

```powershell
dotnet clean .\DebugPilot.sln --configuration Release
dotnet build .\DebugPilot.sln --configuration Release
dotnet test .\DebugPilot.sln --configuration Release
```

The gate requires zero build errors, zero warnings, all tests passing, and successful manual verification of preview → approval → apply → rollback on a disposable sample file. Do not mark the gate complete before these checks are run.
