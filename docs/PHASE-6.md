# Phase 6 — Real fixes: preview → approval → checkpoint → apply → rebuild verification → rollback

First phase in which DebugPilot can change a file. Scope is deliberately narrow and fully verified.

## What it fixes
Missing `using` directives for **CS0246** (type), **CS0103** (name such as `Console`) and **CS1061** (LINQ extension methods such as `ToList`).

1. `MissingSymbolParser` reads the missing symbol (name + generic arity) from the English compiler message.
2. Candidates come from `KnownNamespaces` (curated, unambiguous BCL catalog) and `ProjectTypeIndex` (lexical scan of the owning project and referenced projects; skips bin/obj/.git, caps file count and size, skips files declaring several namespaces).
3. `UsingDirectiveInserter` builds **one anchored patch**: it replaces an existing non-empty line with itself plus the new directive, so `PatchEngine`'s expected-text and hash checks catch a stale file. It keeps CRLF/LF, places `System.*` first then ordinal order, and refuses `#if` headers, multi-line usings and invalid namespace names.
4. A patch is produced only when **exactly one** namespace remains. With several candidates the user sees the choices and no patch is offered.
5. `UsingFixRule` is registered first in `DeterministicDiagnosisEngine`; when it has nothing, the generic guidance rules and the consent-gated AI escalation behave as before.

## Apply flow (`FixWorkflow`)
preview (unified diff) → explicit approval → unsaved-editor check → checkpoint → atomic apply → **rebuild verification** → rollback on request.

* `ProcessBuildVerifier` builds the owning project in a **child process** (never inside Visual Studio's own build): `dotnet build` for SDK-style projects, Visual Studio's MSBuild (found with vswhere) for classic projects. `-nodeReuse:false`, shared compilation off, English output, 5-minute timeout, output capped at 4 MB, cancellation kills the process tree.
* Verdicts: **Passed** (build clean), **PartiallyFixed** (original error gone, other errors remain), **Failed** (original error still reported → rollback recommended), **Inconclusive** (restore/MSBuild error, timeout, no project, cancelled). The original error is matched by code + file name + normalized message, ignoring line numbers.
* Once a patch is applied the checkpoint is never discarded: a crashed or cancelled verification is reported as Inconclusive and the Roll back button stays available (it survives *Clear* and new diagnoses).
* The editor's unsaved state is checked through the VS automation model before apply and rollback. After apply, Visual Studio reloads the file from disk; **Undo history for that file is not preserved** (checkpoint rollback is the supported way back).

## Verified here
* 177 unit tests (net8.0 mirror of Core, xunit shim) including parser, catalog, inserter layouts (CRLF, comments, `#nullable`, aliases, `global using`, `#if`), index limits, ambiguity, stale file, outside-root patch, verifier verdicts, workflow, and the real `SystemProcessRunner`.
* End-to-end against a real `dotnet build`: StringBuilder (BCL) fix, referenced-project type fix, PartiallyFixed → Passed, wrong fix → Failed, rollback byte-identical.

## Not verified / known limits
* The VSIX host code (buttons, dialogs, EnvDTE check) was only syntax-checked; build and exercise it in the Experimental Instance.
* Lexical index can offer false candidates (nested types, `#if`); the rebuild is the safety net.
* English compiler messages only. Packages (NuGet) are not resolved: types from packages that are not referenced yet are not proposed.
* Roslyn semantic analysis, persistent history, delete-all-data/retention for checkpoints, multi-file fixes and `.csproj` package fixes are still open (checkpoints accumulate under `%LOCALAPPDATA%\DebugPilot\checkpoints`).

## Phase 6.1
`VSTHRD010` on `IsDocumentUnsaved`: the apply and rollback handlers now `await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync()` before checking the editor's unsaved state (the check uses the VS automation model, which is main-thread only).
