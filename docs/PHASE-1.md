# Phase 1 — Context Engine and Redaction

## Implemented in this package

- `ContextCaptureInput` accepts only diagnostic-specific data already collected by a host adapter; the core engine does not open files or scan the solution.
- `ContextCaptureOptions` defaults to a 12,000-byte UTF-8 budget and a ±30-line source window. Hard limits are 256 bytes–1 MiB and 0–1,000 lines on each side.
- `ContextEngine` creates a bounded `ContextPreview`, tracks truncation, and excludes source/signature excerpts for `appsettings*.json`, `secrets.json`, `.env*`, and `*.pfx` paths.
- Redaction masks common credential assignments, bearer tokens, connection-string values, email addresses, and common Windows/macOS/Linux user-home prefixes.
- Debugger locals are omitted unless the caller explicitly enables them.
- Tests cover secret masking, sensitive-file exclusion, source windows, context budgets, multibyte UTF-8, optional debugger locals, and invalid options.

## Boundaries and remaining integration work

- This is a core library feature; it does not yet provide the Visual Studio context-preview UI or consent dialog.
- The Visual Studio host adapter must collect only the active diagnostic's minimal relevant snippets, must not enumerate unrelated files, and must require consent before passing preview text to an external AI provider.
- Redaction is defense in depth, not a guarantee that arbitrary source text is safe. Preview must be shown to the user before external transmission.

## Gate checklist

- [ ] `dotnet build DebugPilot.sln --configuration Release` — run on a machine with the .NET SDK.
- [ ] `dotnet test DebugPilot.sln --configuration Release` — run on a machine with the .NET SDK.
- [ ] All Phase 0 tests remain green.
- [ ] New context/redaction tests pass.
- [ ] Confirm no code is transmitted externally by this core component (it contains no networking code).

Do not mark Phase 1 complete until these checks have actual passing results.
