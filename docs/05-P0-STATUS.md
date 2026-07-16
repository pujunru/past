# P0 Implementation — Status

**Date:** 2026-07-15
**Stack:** .NET 8, WinUI 3 (head), Windows-only, local-only.

## What's built

```
Past.sln
 src/Past.Core            Clip, ClipDraft, ClipContentType, ClipHasher
 src/Past.Services        HistoryService (dedupe/caps/search), options, interfaces (IClipStore,
                          IClipboardMonitor, IGlobalHotkey, IPasteService, IClock)
 src/Past.Infrastructure  SqliteClipStore (encrypted-at-rest, serialized access),
                          DPAPI key + AES-GCM protector, Win32 interop:
                          MessageWindow (message-only pump), Win32ClipboardMonitor,
                          Win32GlobalHotkey, Win32PasteService, ForegroundApp, SelfCopyGuard
 src/Past.App            WinUI 3 head: App, OverlayWindow (search + list + keyboard paste),
                          AppHost (composition + tray), app.manifest
 tests/Past.Tests        12 tests (history logic, crypto round-trip, SQLite store)
```

## Verification status

| Component | Builds | Tested |
|---|---|---|
| Past.Core | ✅ | ✅ (via HistoryService) |
| Past.Services | ✅ | ✅ 6 history tests |
| Past.Infrastructure | ✅ | ✅ 6 crypto/storage tests |
| **Past.Tests** | ✅ | ✅ **12/12 passing** |
| Past.App (WinUI head) | ✅ (VS 2026 MSBuild) | ✅ runtime E2E verified |

The engine compiles and all 12 tests pass with the plain .NET SDK. The WinUI head builds
with **VS MSBuild** (not `dotnet build` — see [build guide](06-BUILD-WINUI.md)) and was
verified running: capture, dedupe, encryption-at-rest, and source-app attribution all
confirmed against real Windows clipboard copies. The overlay UX still needs a human to
click through (hotkey → filter → paste).

## P0 feature coverage (per roadmap)

| P0 feature | Where | State |
|---|---|---|
| C1 Text capture (event-driven) | Win32ClipboardMonitor | code complete |
| C2 Local encrypted storage | SqliteClipStore + DPAPI/AES-GCM | ✅ built & tested |
| C3 Recent history | HistoryService + store | ✅ built & tested |
| C4 Global hotkey overlay | Win32GlobalHotkey + OverlayWindow | code complete |
| C5 Live search | HistoryService.SearchAsync | ✅ built & tested |
| C6 Paste-back | Win32PasteService (SetClipboard + SendInput) | code complete |
| C7 Keyboard nav in overlay | OverlayWindow key handlers | code complete |
| C8 Dedupe + caps | HistoryService | ✅ built & tested |
| C9 Tray (open/quit) | AppHost tray | code complete |
| C10 Delete clip / clear all | HistoryService + tray "Clear history" | ✅ logic tested |

"Code complete" = written and reviewed but not yet compiled (blocked on the WinUI head build).

## The WinUI build gap

`dotnet build src/Past.App` fails at:

> The "Microsoft.Build.Packaging.Pri.Tasks.ExpandPriContent" task could not be loaded …
> AppxPackage\Microsoft.Build.Packaging.Pri.Tasks.dll … system cannot find the path

This MSBuild task ships with **Visual Studio's Windows App SDK / MSIX tooling**, not the bare
.NET SDK. This machine has only the .NET SDK (the `Program Files\Microsoft Visual Studio`
folders are empty). WinUI 3 heads require that VS tooling to compile XAML/PRI.

### Finding: VS Build Tools cannot build the WinUI 3 head
Attempted (2026-07-15): installed VS Build Tools 2022 (17.14) and drove `setup.exe modify`
elevated to add `Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools`,
`Microsoft.VisualStudio.ComponentGroup.WindowsAppSDK.Cs`, and the Windows 11 SDK.
The modify **no-ops** — monitored run showed zero installer processes, no package-cache
growth, and the PRI task DLL never appeared. The WinUI 3 PRI/Appx MSBuild tooling
(`Microsoft.Build.Packaging.Pri.Tasks.dll`) is **not offered for the headless Build Tools
SKU**; it ships only with the full Visual Studio IDE. Elevation itself works fine.

### Remaining options
1. **Install full Visual Studio Community 2022** + Windows App SDK workload here (free,
   but a larger ~8–15 GB install). Then `dotnet build -p:Platform=x64` builds the head. No
   code changes; stays on WinUI 3.
2. **Pivot the head to WPF** — the HLD already blessed this as a low-risk fallback because
   all interop and the engine are UI-framework-agnostic. WPF builds and runs on the bare
   .NET SDK already installed, so the full app is verifiable in this environment now.
   Deviates from the WinUI 3 decision (engine/interop untouched).
3. **Leave the head as WinUI 3 code** and build it on a machine with full VS 2022.

## Build & test the engine (works today)
```
dotnet test tests/Past.Tests/Past.Tests.csproj
```

## Deferred to P1 (unchanged from roadmap)
Pinned, Templates, exclusions, redaction, timed retention, settings UI, hotkey rebinding,
autostart, theming, export/import, images/files.
