# DebugPilot AI — VSIX packaging (Visual Studio 2022 and 2026)

This solution includes `DebugPilot.Package`, a classic VSIX packaging project for Visual Studio 2022.
The package project compiles the existing extension source files from `src/DebugPilot.Vsix` and references `DebugPilot.Core`.

## Build from a clean state

Open **Developer PowerShell for Visual Studio 2022**, change to the folder containing `DebugPilot.sln`, and run:

```powershell
# Remove stale build and restore outputs for this package project
Remove-Item .\DebugPilot.Package\bin, .\DebugPilot.Package\obj -Recurse -Force -ErrorAction SilentlyContinue

# Restore the solution, then build the package project
MSBuild.exe .\DebugPilot.sln /restore /t:Build /p:Configuration=Release /p:Platform="Any CPU" /m

# Locate the generated VSIX
Get-ChildItem . -Filter "*.vsix" -Recurse | Select-Object FullName, Length
```

The `Microsoft.VSSDK.BuildTools` package is referenced in the project and its targets are imported by NuGet. The project deliberately does not import `Microsoft.VsSDK.targets` a second time.

If the build reports that `Microsoft.VSSDK.BuildTools` targets or `VSCTCompile` tasks are missing, confirm the **Visual Studio extension development** workload is installed in Visual Studio Installer, restore packages, and share the complete build output. A ZIP archive cannot itself prove that the Windows VSIX packaging target ran; the `.vsix` is confirmed only when the command above returns a package path.


### HttpClient compile fix

The legacy VSIX packaging project explicitly references the .NET Framework `System.Net.Http` assembly, which provides `HttpClient` for `DebugPilotToolWindowControl.xaml.cs`.


## v5 packaging corrections

- **Install targets** in `DebugPilot.Package/source.extension.vsixmanifest` are now `[17.0,19.0)` (VS 2022 = 17.x, VS 2026 = 18.x). The previous `[17.0,18.0)` made the VSIX uninstallable on VS 2026. Installation on VS 2026 is still **unverified** — test in an Experimental Instance.
- **One VSIX only.** `DebugPilot.Package` is the packaging project. The SDK-style `DebugPilot.Vsix` project no longer sets `CreateVsixContainer`, so it cannot emit a second `.vsix` with a different identity but the same package GUID.
- **`dotnet build DebugPilot.sln` cannot build `DebugPilot.Package`** (legacy VSSDK project, needs full MSBuild). `setup.ps1` now builds/tests Core via the test project and builds the VSIX only with `.\setup.ps1 -BuildVsix`. CI does the same.
- **MessagePack** is pinned (2.5.303) only to satisfy NuGet audit; `ExcludeAssets="runtime"` stops `MessagePack.dll` from being copied into the VSIX, because Visual Studio supplies its own copy.
- Removed the leftover `DebugPilot.PackagePackage.cs` template (it was not compiled and was invalid C#: `class DebugPilot.PackagePackage`).

## Still missing for a Marketplace-ready package
Icon, preview image, license file/`<License>`, release notes and more-info URL in the manifest; code signing; install/uninstall/upgrade tests on clean VS 2022 and VS 2026 machines.

## v6 compile fix

`CS0012` / `CS1061` on `OleMenuCommandService.AddCommand` (`DebugPilotPackage.cs`): the legacy packaging project did not reference `System.Design`, where `MenuCommandService` lives. Added `<Reference Include="System.Design" />`. The `MSB3277` System.Collections.Immutable warning is expected (Visual Studio provides that assembly at run time) and is downgraded to a message.


## v7 VSIX container generation fix

- `DebugPilot.Package` now explicitly enables `CreateVsixContainer`, includes the package definition, and copies the VSIX manifest. This is required for command-line builds because VSSDK defaults may disable container creation outside the Visual Studio IDE.
- The classic project explicitly imports `Microsoft.VsSDK.targets` from the restored `Microsoft.VSSDK.BuildTools` 17.14.2120 package. Without that import, a C# build can succeed without registering the VSIX container target.
- Build with full Visual Studio MSBuild (not `dotnet build`): `msbuild .\DebugPilot.Package\DebugPilot.Package.csproj /restore /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /v:minimal`
- Expected artifact: `DebugPilot.Package\bin\Release\DebugPilot.AI.vsix`. The build is not considered packaging-successful unless that file exists and is non-empty.


## v8 tool-window diagnostics

If the Tools > DebugPilot AI command is visible but the window does not open, this build catches tool-window creation exceptions, writes the full exception to Visual Studio ActivityLog.xml, and displays a message box with the immediate error. Launch Visual Studio with `devenv.exe /log`, reproduce the issue, then inspect `%APPDATA%\Microsoft\VisualStudio\17.0_*\ActivityLog.xml` for `DebugPilot AI`.


## v9 tool-window hardening

- `DebugPilotPackage` now overrides `GetAsyncToolWindowFactory`, `GetToolWindowTitle` and `InitializeToolWindowAsync` (analyzer VSSDK003) and `DebugPilotToolWindow` has the one-parameter constructor the async factory expects. If `ShowToolWindowAsync` fails or returns no frame, the package falls back to `FindToolWindow(...)` and shows the frame explicitly.
- If the workspace control throws while loading, the window now opens and displays the exception (type, message, inner exception) instead of failing silently; the full text goes to ActivityLog.xml.
- AI settings loading and the shared `HttpClient` can no longer break window creation (settings errors are logged; the client is created lazily).
- The VSSDK targets import uses `$(PkgMicrosoft_VSSDK_BuildTools)` so it follows the package version.

If the window still does not open, run `devenv.exe /rootsuffix Exp /log`, reproduce, and send the `DebugPilot AI` entries from `%APPDATA%\Microsoft\VisualStudio\17.0_*Exp\ActivityLog.xml`.

## v9.1 analyzer fix

`VSTHRD010` on `GetAsyncToolWindowFactory`: the override no longer calls `base.GetAsyncToolWindowFactory(...)` (the analyzer treats `IVsAsyncToolWindowFactoryProvider` as main-thread-only and Visual Studio may query the factory from a background thread). It returns `this` for the DebugPilot tool window and `null` otherwise, which is what the base implementation returns for non-async windows.
