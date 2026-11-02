# ADR-002: JSON library inside the VSIX (open risk)

Status: OPEN — verify before Marketplace release.

`DebugPilot.Core` (netstandard2.0) uses `System.Text.Json` 10.x. The VSIX runs in-process inside Visual Studio (net472), which loads its own copies of `System.Text.Json`, `System.Memory`, `System.Buffers`, `System.Runtime.CompilerServices.Unsafe`, `Microsoft.Bcl.AsyncInterfaces`, etc. A version mismatch can surface as `FileLoadException` / `MissingMethodException` at runtime even though the build is clean.

## Verification (required)
Install the VSIX in an experimental instance of **VS 2022 and VS 2026**, then: save provider settings (serializes JSON), run an AI request against a local endpoint (deserializes JSON), and watch for assembly-load errors (Fusion log / `ActivityLog.xml`).

## If it fails
1. Pin `System.Text.Json` to the version shipped with the oldest supported VS, or
2. switch Core to `Newtonsoft.Json`, which Visual Studio ships and binds consistently, or
3. move AI I/O out-of-proc (VisualStudio.Extensibility), per ADR-001.
