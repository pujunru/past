# Past — a privacy-first clipboard manager for Windows

A fast, local-only clipboard manager for Windows. Press a global hotkey, get a horizontal
strip of your recent clips, filter as you type, and press Enter to paste straight back into
the app you came from.

**Free software. No account, no cloud, no telemetry, no pricing.** Your clipboard history
never leaves your machine — the app ships with no networking code at all.

---

## Credit where it's due

This project is inspired, end to end, by **[PasteClip](https://pasteclip.app/)** — a
privacy-first clipboard manager for macOS ([source](https://github.com/minsang-alt/PasteClip)).
The product thinking is theirs: local-first storage, keyboard-driven recall, explicit capture
controls, bounded retention, and a card-based visual history. All kudos to PasteClip and its
author for the original design and for showing that a clipboard manager can be genuinely
privacy-respecting.

Past is **not a port** and shares no code with PasteClip. macOS's APIs have no counterpart
here, so everything was rebuilt natively against Windows:

| PasteClip (macOS) | Past (Windows) |
| --- | --- |
| `NSPasteboard` + `changeCount` polling | `AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE` (event-driven) |
| Carbon / `NSEvent` global hotkey | `RegisterHotKey` on a message-only window |
| `CGEvent` paste | `SetClipboardData` + `SendInput` |
| Keychain / FileVault | DPAPI-wrapped key + AES-256-GCM |
| AppKit | WinUI 3 (Windows App SDK) |
| iCloud sync | *(intentionally dropped — local only)* |

If you're on a Mac, go use PasteClip.

## Features (v1)

- **Text clipboard capture** — event-driven, no polling
- **Horizontal card strip** — each clip is a card with type badge (TEXT / LINK / CODE),
  source app, relative age and character count
- **Global hotkey** — tries `Win+Alt+V` first (Windows reserves `Win+V`, and `Win+Shift+V`
  is taken on some machines), falling back through other chords until one registers
- **Instant search** — just start typing
- **Paste on select** *(default on)* — pick a clip and it lands in your app immediately.
  Turn it off in the tray menu to only put it on the clipboard and paste it yourself.
- **Encrypted at rest** — SQLite store with AES-256-GCM; the key is wrapped with Windows
  DPAPI, so the database is unreadable on another machine or user account
- **Tray controls** — pause capture, clear history, quit

## Privacy

- Everything is stored locally in `%LOCALAPPDATA%\Past\`
- Clip contents are encrypted at rest and **never written to logs**
- The local diagnostic log holds metadata only, and nothing is ever sent anywhere
- `settings.json` is deliberately plain text: they're non-sensitive toggles, and being able
  to read exactly what's stored suits a privacy-first tool

## Install

Grab `PastSetup.exe` from the [latest release](https://github.com/pujunru/past/releases/latest)
and run it. It installs per-user, so there is no admin prompt, and it adds a Start menu
entry, an optional "start when I sign in" task, and a normal uninstall entry under
"Apps & features".

The installer is not code-signed, so SmartScreen may warn on first run — choose
**More info → Run anyway**.

Prefer not to install anything? `Past-x64.zip` from the same release is the raw build:
extract it anywhere and run `Past.App.exe`. Both are self-contained, so no .NET install
is needed.

Uninstalling leaves your clipboard history in `%LOCALAPPDATA%\Past` alone; delete that
folder too if you want it gone.

## Requirements

- Windows 10 21H2+ / Windows 11 (x64)

## Building

The engine and tests build with the plain .NET SDK:

```powershell
dotnet test tests/Past.Tests/Past.Tests.csproj
```

The WinUI 3 app **must be built with Visual Studio's MSBuild**, not `dotnet build` — the
PRI/Appx task ships with VS, not the .NET SDK. Install VS 2022/2026 with the **.NET desktop
development** workload plus the **.NET WinUI app development tools** component, then:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
    src\Past.App\Past.App.csproj -restore -p:Configuration=Debug -p:Platform=x64
```

Full details in [docs/06-BUILD-WINUI.md](docs/06-BUILD-WINUI.md).

### Building the installer

Needs [Inno Setup 6](https://jrsoftware.org/isdl.php). After a Release build:

```powershell
ISCC.exe installer\Past.iss /DAppVersion=0.2.0
# -> dist\PastSetup.exe
```

CI does this automatically and attaches it to releases built from a `v*` tag.

## Project layout

```
src/Past.Core            domain model (Clip, hashing) — no platform dependencies
src/Past.Services        history logic (dedupe, caps, search), settings, interfaces
src/Past.Infrastructure  SQLite store, DPAPI/AES crypto, Win32 interop
src/Past.App             WinUI 3 tray app + overlay
tests/Past.Tests         unit tests
```

Design docs — product definition, high-level design, roadmap — live in [docs/](docs/).

## Not planned

Cloud sync, accounts, telemetry, mobile companions, cross-platform builds, and monetisation
are all explicit non-goals. See [docs/01-PDD.md](docs/01-PDD.md).

## License

MIT — see [LICENSE](LICENSE).
