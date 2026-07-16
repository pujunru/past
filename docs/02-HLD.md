# PasteClip for Windows — High-Level Design (HLD)

**Status:** Draft v0.1
**Date:** 2026-07-15
**Companion docs:** [PDD](01-PDD.md), [Implementation Plan](03-impl-plan.md)

---

## 1. Design Goals & Constraints
- Native, lightweight, always-on tray utility. **Stack: .NET 8 + WinUI 3 (Windows App SDK).**
- **Windows-only by design.** No cross-platform or hybrid-framework abstraction layer;
  we bind directly to Win32/WinRT APIs. Portability to other OSes is a non-goal (PDD N5),
  which frees us to use the most native, lowest-overhead API for each concern.
- **Local-only, zero networking.** No sync, no account, no telemetry (PDD N1/NFR1).
- Sub-150 ms overlay; keyboard-first.
- Encrypted-at-rest local store.
- Windows 10 21H2+ / Windows 11.

### Key platform decision: packaging & trust level
Global hotkeys, low-level clipboard monitoring, and "send paste to foreground app"
(SendInput) are **friction-prone or blocked inside the MSIX/Store AppContainer sandbox.**
Therefore v1 targets a **packaged-but-full-trust** desktop app:
- Windows App SDK (WinUI 3) desktop app,
- packaged with **MSIX using `runFullTrust` capability** (or unpackaged + winget) so we
  retain Win32 clipboard/hotkey/SendInput access,
- distributed via signed MSIX + winget (Store is a later evaluation — see PDD OQ1).

## 2. System Context

```
            ┌─────────────────────────────────────────────┐
            │              Windows OS                       │
            │                                               │
  copy ───► │  System Clipboard  ◄──── other apps          │
            │        ▲   │                                  │
            │        │   │ WM_CLIPBOARDUPDATE               │
            │        │   ▼                                  │
            │   ┌──────────────────────────────────────┐    │
            │   │        Past (our app, full-trust)     │    │
            │   │  Tray icon • Global hotkey • Overlay   │    │
            │   └──────────────────────────────────────┘    │
            │        │                                       │
            └────────┼───────────────────────────────────────┘
                     ▼
        %LOCALAPPDATA%\Past\  (SQLite DB + DPAPI-protected key)
                     ▲
                     │ export/import
                     ▼
             Encrypted JSON file (user-chosen location)

     ⚠ No network boundary crossed. Ever.
```

## 3. Component Architecture

Single process, layered, MVVM on the UI side.

```
┌───────────────────────────────────────────────────────────────┐
│ Presentation (WinUI 3, MVVM)                                   │
│  • TrayHost (NotifyIcon + context menu)                        │
│  • RecallOverlayWindow (borderless, near-caret popup)          │
│  • MainWindow (history browser, Pinned, Templates)             │
│  • SettingsWindow                                             │
│  • ViewModels + value converters                               │
├───────────────────────────────────────────────────────────────┤
│ Application / Services                                         │
│  • ClipboardMonitorService  (listener → capture pipeline)     │
│  • CapturePipeline (dedupe → exclude → redact → persist)      │
│  • HistoryService (query Recent/Pinned/Templates, search)     │
│  • PasteService (set clipboard + SendInput paste)             │
│  • HotkeyService (RegisterHotKey / conflict handling)         │
│  • RetentionService (background purge)                        │
│  • ExclusionService (foreground-app detection)                │
│  • RedactionService (pattern rules)                           │
│  • ExportImportService (encrypted JSON)                       │
│  • SettingsService                                            │
├───────────────────────────────────────────────────────────────┤
│ Domain                                                        │
│  • Entities: Clip, Collection(Recent/Pinned/Template),        │
│    SourceApp, RedactionRule, ExclusionRule, Settings          │
│  • Rules & invariants (retention, dedupe, pin exemption)      │
├───────────────────────────────────────────────────────────────┤
│ Data / Infrastructure                                         │
│  • SqliteRepository (EF Core or Microsoft.Data.Sqlite +       │
│    SQLCipher/manual field encryption)                         │
│  • CryptoProvider (DPAPI-protected data key; AES-GCM fields)  │
│  • Win32 Interop (P/Invoke): clipboard, hotkeys, foreground   │
│    window, SendInput                                          │
│  • FileSystem (paths, export files)                           │
└───────────────────────────────────────────────────────────────┘
```

Dependency rule: Presentation → Application → Domain ← Infrastructure. Domain has no
platform deps; Win32 is isolated behind interfaces so services stay testable.

## 4. Key Platform Interop (the "macOS API is different" core)

| Concern | macOS (PasteClip) | Windows (Past) |
|---|---|---|
| Clipboard access | `NSPasteboard` | `Win32` clipboard: `AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE`; `Get/SetClipboardData` (or WinRT `Clipboard` API) |
| Change notification | pasteboard `changeCount` poll | `WM_CLIPBOARDUPDATE` (event-driven, no polling) |
| Global hotkey | `NSEvent` global monitor / Carbon | `RegisterHotKey` (Win32) with a message-only window |
| Foreground app | `NSWorkspace.frontmostApplication` | `GetForegroundWindow` → `GetWindowThreadProcessId` → `QueryFullProcessImageName` |
| Paste into app | CGEvent | `SetClipboardData` then `SendInput` (Ctrl+V) to prior foreground window |
| Secure storage | Keychain / FileVault | **DPAPI** (`ProtectedData`) for the DB key; AES-GCM for sensitive fields |
| Autostart | LaunchAgent | `HKCU\...\Run` or Startup task / MSIX startup extension |
| Sync | iCloud | **none** (removed) |

### 4.1 Clipboard monitoring
- A hidden **message-only window** subscribes via `AddClipboardFormatListener`.
- On `WM_CLIPBOARDUPDATE`, read available formats. v1: `CF_UNICODETEXT`. v2: `CF_DIB`/`CF_BITMAP`, `CF_HDROP`.
- Guard against our own `SetClipboard` re-triggering capture (ignore next update after a self-set, keyed by an internal sequence token).
- Robustness: clipboard can be locked by another process → retry with backoff; never block the UI thread.

### 4.2 Global hotkey & conflict strategy
- `RegisterHotKey` on the message-only window, **called on the window's own pump thread** —
  calling it from another thread fails with `ERROR_WINDOW_OF_OTHER_THREAD` (1408).
- Try an ordered candidate list, use the first that registers: `Win+Alt+V` → `Win+Shift+V`
  → `Ctrl+Alt+V` → `Win+Shift+C`. (`Win+V` is reserved; `Win+Shift+V` was observed taken
  on real hardware. `Ctrl+Shift+V` is excluded — it would hijack in-app paste-as-plain-text.)
- If registration fails (chord taken), surface a settings prompt to pick another; never silently fail.
- Overlay is a separate always-on-top, no-activation-stealing borderless window positioned near the caret (via `GetGUIThreadInfo`/`GetCaretPos` where available, else near cursor).

### 4.3 Paste-back
- Remember the foreground window handle captured at hotkey time.
- On selection: `SetClipboardData` with the chosen clip → restore focus to that window → `SendInput` Ctrl+V.
- "Paste as plain text": force `CF_UNICODETEXT` only.
- Optionally restore the user's *previous* clipboard content after paste (setting).

## 5. Data Model & Storage

**Store:** SQLite at `%LOCALAPPDATA%\Past\past.db`. Access via `Microsoft.Data.Sqlite`.
Encryption approach (pick one in impl phase): **SQLCipher** for whole-DB encryption
(preferred), or app-level **AES-GCM** on content columns with a DPAPI-protected key.
Either way the symmetric key is wrapped by **DPAPI (CurrentUser)** so it never leaves the machine and is bound to the Windows user.

```sql
-- Core tables (illustrative)
Clip(
  id INTEGER PK,
  content_type TEXT,        -- 'text' (v1), 'image','files' (v2)
  content BLOB,             -- encrypted
  preview TEXT,             -- short, encrypted, for list display
  hash TEXT,                -- for dedupe (hash of plaintext, salted)
  size_bytes INTEGER,
  source_app TEXT,          -- process name/path
  collection TEXT,          -- 'recent' | 'pinned' | 'template'
  is_pinned INTEGER,
  created_utc INTEGER,
  last_used_utc INTEGER,
  template_name TEXT NULL
)
Setting(key TEXT PK, value TEXT)
ExclusionRule(id, match_type, pattern)      -- process name/path/window title
RedactionRule(id, name, regex, action)      -- 'skip' | 'mask'
```

Invariants:
- Pinned/Template rows are exempt from retention purge.
- Dedupe on `hash`; existing match → update `last_used_utc`, move to top (no new row).
- Enforced caps: max item size, max Recent count → oldest purged.

## 6. Cross-Cutting Concerns

### Security & Privacy
- **No network stack referenced or linked** in v1; add a build-time check / assembly
  scan gate to prove it (supports NFR1 "verifiable no outbound").
- Contents never written to logs; logging is metadata/level only.
- DPAPI-bound key → DB unreadable if copied to another machine/user.
- Redaction runs *before* persistence; excluded apps never reach the pipeline.
- "Delete all now" performs a secure delete + `VACUUM`.

### Performance
- Event-driven capture (no polling).
- Search: SQLite index on `preview`/`created_utc`; for fuzzy, in-memory scoring over a
  bounded working set; FTS5 as an option if scale demands.
- Overlay pre-warmed (window created hidden at startup) for <150 ms show.

### Reliability
- Capture pipeline is queue-based; UI never blocks capture.
- Clipboard read wrapped in retry/backoff for lock contention.
- DB writes are transactional; WAL mode.

### Observability (local only)
- Rotating local log file (metadata only), user-openable from Settings. No remote sink.

## 7. Threading Model
- UI thread: WinUI dispatcher only.
- A dedicated message-only window runs its own message loop for clipboard/hotkey messages.
- Capture pipeline + retention run on background tasks; results marshaled to UI via dispatcher.

## 8. Failure Modes & Handling
| Failure | Handling |
|---|---|
| Clipboard locked by other app | Retry w/ backoff; drop after N tries, log metadata |
| Hotkey registration fails | Prompt user to rebind; app still usable via tray |
| DB corruption | Detect on open; offer restore-from-export or reset |
| DPAPI key unwrap fails (profile change) | Treat DB as unreadable; guided reset |
| Overlay can't find caret | Fall back to cursor-position placement |

## 9. Technology Choices (summary)
- **Language/runtime:** C# / .NET 8.
- **UI:** WinUI 3 (Windows App SDK 1.5+), MVVM (CommunityToolkit.Mvvm).
- **DI/logging:** `Microsoft.Extensions.*` (Hosting, DI, Logging to local file via Serilog file sink).
- **Data:** `Microsoft.Data.Sqlite` (+ SQLCipher) — or EF Core if we want migrations tooling.
- **Crypto:** `System.Security.Cryptography` (AES-GCM) + `ProtectedData` (DPAPI).
- **Interop:** hand-written P/Invoke (CsWin32 source generator to reduce boilerplate).
- **Packaging:** MSIX `runFullTrust`, signed; winget manifest.
- **Tests:** xUnit + a Win32 interop test harness; UI smoke via WinAppDriver (optional).

## 10. Alternatives Considered
- **Electron/Tauri:** rejected for footprint / native-integration reasons (PDD NFR2/NFR4). Their main draw — cross-platform reach — is an explicit non-goal (PDD N5), so it buys us nothing while adding weight and interop distance from Win32.
- **WPF:** viable fallback; WinUI 3 chosen for modern styling + long-term support. Interop layer is UI-agnostic, so a WPF pivot is low-risk if WinUI 3 friction is high.
- **Built-in Windows Clipboard History:** insufficient (no templates/pinning/redaction/retention control); we intentionally coexist with it.
- **Whole-DB SQLCipher vs. field-level AES:** SQLCipher simpler & covers previews/indexes; field-level avoids native dep. Decision deferred to Phase 2 spike.

## 11. Risks
- R1. Sandbox vs. full-trust tension if Store distribution is later required.
- R2. `SendInput` paste can be unreliable against elevated/UIPI-protected windows (can't paste into higher-integrity apps). Document limitation.
- R3. Caret positioning APIs are inconsistent across apps → overlay placement fallback needed.
- R4. Native SQLCipher dependency complicates packaging → have field-level AES fallback.
