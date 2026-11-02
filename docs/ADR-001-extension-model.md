# ADR-001: Extension model (VisualStudio.Extensibility vs VSSDK)

Status: PROPOSED — must be confirmed in Phase 0 spike before Phase 4.

## Needs
Error List access, build/exception/test events, debugger locals (opt-in), tool window with 6 panels, VS 2022 + VS 2026.

## Options
1. VisualStudio.Extensibility (out-of-proc): modern, isolated from VS crashes, async by design. Gaps: debugger/Error List coverage may be incomplete.
2. VSSDK (in-proc, net472): full API surface, tool window/WPF familiar. Risks: UI-thread blocking, VS-version coupling.
3. Hybrid: UI + commands on VisualStudio.Extensibility, thin VSSDK shim only where an API is missing.

## Decision (proposed)
Keep ALL logic in `DebugPilot.Core` (netstandard2.0, zero VS references). Host is a thin adapter, so the choice can change without rewriting logic.
Phase 0 spike: prove (a) read Error List items, (b) subscribe to build-finished, (c) show a tool window, on VS 2022 AND VS 2026, with option 1; fall back to 3 if any is blocked.

## Consequences
Core stays unit-testable on any OS. Host adapter is the only piece needing VS integration tests.
