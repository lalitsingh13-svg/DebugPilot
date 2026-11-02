# Phase 5D.2 — Vulnerable dependency build error

**Symptom:** restore of `DebugPilot.Vsix` failed with `NU1902 (warning as error)`: `MessagePack 2.5.301` has a known moderate-severity vulnerability (GHSA-qhrr-8q5h-9q3h, nested-array allocation amplification when untrusted data is deserialized as `object`). `MessagePack` arrives transitively through the Visual Studio SDK packages and was pinned at the vulnerable version.

**Fix:** `Directory.Packages.props` now pins `MessagePack` and `MessagePack.Annotations` to `2.5.303`, the v2 release that contains the fix. Both must stay on the same version (MessagePack depends on the matching Annotations release). If `2.5.303` is not found on your feed, use the newest `2.5.x` ≥ 2.5.303 for **both** packages.

**Not a runtime exposure of DebugPilot itself:** DebugPilot does not deserialize MessagePack. The pin only satisfies the audit.

**Verify locally:** `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release --no-build`, `dotnet list DebugPilot.sln package --vulnerable --include-transitive`. Also confirm in VS 2022 and 2026 experimental instances that the VSIX still loads: the Vsix output copies `MessagePack.dll`, which Visual Studio also ships, so a version clash is possible.
