# Building & Running the WinUI 3 Head

The engine (Core/Services/Infrastructure) and the 12 tests build on the bare .NET SDK.
The **WinUI 3 head** (`src/Past.App`) additionally needs the PRI/Appx MSBuild tooling that
ships only with the **full Visual Studio IDE** (not Build Tools).

## 1. Install Visual Studio Community (2022 or 2026)

Either **Community 2022** or **Community 2026** (version 18) works — both ship the WinUI 3 /
Windows App SDK build tooling. The project pins its own NuGet packages, so the VS version
only supplies MSBuild + the PRI/Appx packaging task; the build result is the same.

### Option A — GUI (simplest)
1. Install/modify VS Community from the Visual Studio Installer (or
   https://visualstudio.microsoft.com/vs/community/).
2. On the **Workloads** tab, check **".NET desktop development"**.
3. Switch to the **Individual components** tab, search **"Windows App SDK"**, and check the
   **Windows App SDK C# Templates** component.
4. Install.

### Option B — command line (run in an **elevated** terminal)
```powershell
winget install --id Microsoft.VisualStudio.2022.Community -e `
  --override "--quiet --norestart --add Microsoft.VisualStudio.Workload.ManagedDesktop --add Microsoft.VisualStudio.ComponentGroup.WindowsAppSDK.Cs --includeRecommended"
```
The `ManagedDesktop` workload + `WindowsAppSDK.Cs` component together provide the
`Microsoft.Build.Packaging.Pri.Tasks` task that Build Tools was missing.

## 2. Build the head

> **Use VS's MSBuild, not `dotnet build`.** The PRI/Appx task lives under the VS install
> (`...\18\Community\MSBuild\Microsoft\VisualStudio\v18.0\AppxPackage\`), not under the
> .NET SDK, so `dotnet build` fails with MSB4062 on `ExpandPriContent`. The engine and
> tests build fine with plain `dotnet`.

**In Visual Studio:** open `Past.sln`, set **Past.App** as the startup project, set the
configuration to **Debug / x64**, press **F5**.

**From the CLI (verified working):**
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
    src\Past.App\Past.App.csproj -restore -p:Configuration=Debug -p:Platform=x64
```
Run the produced unpackaged exe:
```
src\Past.App\bin\x64\Debug\net8.0-windows10.0.19041.0\Past.App.exe
```

(A `NETSDK1206` RID warning about `Microsoft.WindowsAppSDK` is expected and harmless.)

## 3. Try it (P0 loop)
1. It's a **tray app** — no main window at launch. Look for the tray icon (tooltip
   "Past — clipboard (Win+Alt+V)").
2. Copy some text in any app (Notepad, browser).
3. Press **Win+Alt+V** → the overlay opens at the cursor.
4. Type to filter, use ↑/↓, press **Enter** (or click) → it pastes into the app you were in.
5. Tray menu: **Pause capture**, **Clear history**, **Quit**.

## 4. Verified status (2026-07-16)

Built clean with VS 2026 Community MSBuild, and verified running end-to-end:

- App launches as a tray app and stays resident (~128 MB working set).
- Creates `%LOCALAPPDATA%\Past\` with `key.bin` (DPAPI-wrapped) + `past.db` (WAL).
- **Capture confirmed**: real Windows clipboard copies are captured via
  `WM_CLIPBOARDUPDATE`, with correct source-app attribution.
- **Dedupe confirmed**: 4 copies (one a duplicate) → 3 stored rows, duplicate bumped to top.
- **Encryption confirmed**: rows decrypt only via the DPAPI-unwrapped AES-GCM key.
- **Preview confirmed**: multi-line clips preview as their first line only.

### Still unverified (needs a human at the keyboard)
The overlay UX can't be driven headlessly. Please sanity-check:
1. Press **Win+Alt+V** → overlay opens at the cursor with the search box focused.
2. Type to filter; **↑/↓** to move; **Enter** pastes into the app you came from.
3. **Esc** hides it; tray menu offers Pause capture / Clear history / Quit.

Known spots to adjust if the overlay misbehaves (presentation only — engine unaffected):
- If the search box isn't focused on open, call `SetForegroundWindow` on the overlay HWND
  (via `WinRT.Interop.WindowNative.GetWindowHandle(this)`) before `Focus()`.
- If the popup styling/focus is odd, swap `OverlappedPresenter.CreateForContextMenu()` for
  `OverlappedPresenter.Create()` + `SetBorderAndTitleBar(false, false)` + `IsAlwaysOnTop`.
- The tray icon currently ships no `.ico`, so it may render as a default/blank glyph.
