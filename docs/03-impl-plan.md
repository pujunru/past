# PasteClip for Windows — Implementation Plan

**Status:** Draft v0.1
**Date:** 2026-07-15
**Companion docs:** [PDD](01-PDD.md), [HLD](02-HLD.md)
**Stack:** .NET 8 + WinUI 3, local-only, Windows desktop only.

---

## 0. Guiding sequencing principle
De-risk the platform-specific unknowns **first** (clipboard listener, global hotkey,
paste-back, DPAPI storage) with throwaway spikes, then build the real layered app on
proven interop. UI polish comes after the capture→store→recall→paste loop works end-to-end.

## 1. Milestones overview

| Phase | Outcome | Rough size |
|---|---|---|
| P0 | Repo, solution skeleton, CI, interop spikes | S |
| P1 | Capture → persist (encrypted) → view Recent | M |
| P2 | Global hotkey + recall overlay + paste-back | M |
| P3 | Collections: Pinned + Templates | S |
| P4 | Privacy: pause, exclusions, redaction, retention | M |
| P5 | Settings, tray, autostart, theming | S |
| P6 | Export/Import (encrypted JSON) | S |
| P7 | Hardening: perf, reliability, no-network gate, a11y | M |
| P8 | Packaging (MSIX/winget), signing, beta | M |

## 2. Phase detail

### P0 — Foundations & de-risking spikes
- Create solution `Past.sln` with projects:
  - `Past.App` (WinUI 3 desktop)
  - `Past.Core` (domain)
  - `Past.Services` (application services + interop interfaces)
  - `Past.Infrastructure` (SQLite, crypto, Win32 P/Invoke)
  - `Past.Tests` (xUnit)
- Add CsWin32, CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting/DI/Logging, Serilog file sink, Microsoft.Data.Sqlite.
- **Spike 1 — Clipboard listener:** message-only window + `AddClipboardFormatListener`; log text on `WM_CLIPBOARDUPDATE`. Prove no-poll capture + self-set suppression.
- **Spike 2 — Global hotkey:** `RegisterHotKey` `Win+Shift+V` → show a blank popup.
- **Spike 3 — Paste-back:** capture foreground HWND, `SetClipboardData`, `SendInput` Ctrl+V into Notepad.
- **Spike 4 — DPAPI + SQLite:** write/read an encrypted row; pick SQLCipher vs field-level AES (decision gate).
- CI: GitHub Actions building the WinUI app on `windows-latest`, run unit tests.
- **Exit criteria:** all four spikes green; encryption approach chosen; interop patterns documented.

### P1 — Capture pipeline & Recent view
- Domain entities: `Clip`, `Collection`, `Settings`.
- `SqliteRepository` + schema/migration (per HLD §5); WAL mode.
- `CryptoProvider` (chosen approach) with DPAPI-wrapped data key.
- `ClipboardMonitorService` → `CapturePipeline` (dedupe by hash → persist).
- Minimal `MainWindow` list showing Recent (preview, time, source app), newest first.
- Enforce caps: max item size, max Recent count.
- Tests: dedupe logic, cap enforcement, repository round-trip, crypto round-trip.
- **Exit:** copying text anywhere appears in the Recent list, encrypted at rest.

### P2 — Recall overlay + paste
- `HotkeyService` (register/rebind, conflict handling).
- `RecallOverlayWindow`: borderless, no-activation, near-caret/cursor, pre-warmed at startup.
- Live search (substring first; fuzzy later), keyboard nav (↑/↓/Enter/Esc).
- `PasteService`: set clipboard + SendInput paste to remembered HWND; plain-text modifier.
- Tests: search ranking, hotkey rebind persistence; manual matrix for paste targets.
- **Exit:** hotkey → search → Enter pastes into the previously focused app in <2s.

### P3 — Pinned & Templates
- Pin/unpin (exempt from retention); Templates CRUD (name + body, never auto-captured).
- Collection filter/tabs in MainWindow and overlay.
- Tests: pin exemption from purge; template create/edit/delete.
- **Exit:** three working collections, reachable from overlay and main window.

### P4 — Privacy controls
- `ExclusionService`: foreground-app detection; per-process exclusion; capture skipped when excluded.
- Pause/resume capture (tray + settings + optional hotkey).
- `RedactionService`: default rules (card-like, token-like) + user rules; skip or mask before persist.
- `RetentionService`: background purge (default 7 days) honoring pin/template exemption; secure "delete all now" + VACUUM.
- Ship a "Privacy" screen (what/where stored).
- Tests: redaction test suite (no secret persisted), exclusion enforcement, retention purge correctness.
- **Exit:** all PDG privacy FRs (FR4–6, FR16) satisfied and tested.

### P5 — Settings, tray, autostart, theming
- `SettingsService` (persisted), Settings UI: hotkey, retention, exclusions, redaction rules, launch-at-startup, theme, caps.
- Tray icon + context menu (open, pause, quit).
- Autostart via `HKCU\...\Run` or MSIX startup extension.
- Light/dark/high-contrast theme; keyboard a11y + screen-reader labels on overlay.
- **Exit:** app fully configurable, starts with Windows, lives in tray.

### P6 — Export / Import
- `ExportImportService`: encrypted JSON (user passphrase, AES-GCM + KDF).
- Import modes: merge vs replace; validation & versioned schema.
- Tests: round-trip export→import equality; corrupt/incompatible file handling.
- **Exit:** FR18 complete.

### P7 — Hardening
- Perf pass to hit NFR2/NFR4 (overlay <150 ms, idle RAM <80 MB, search <50 ms @10k).
- Reliability: clipboard-lock retry/backoff, DB corruption detection + recovery-from-export.
- **No-network gate:** build/CI check asserting no networking assemblies/refs (proves NFR1).
- Stress test: rapid copy loop → zero data loss.
- Accessibility audit.
- **Exit:** NFRs met; stress + a11y pass.

### P8 — Packaging & beta
- MSIX packaging with `runFullTrust`, code signing; winget manifest.
- Installer size budget (<60 MB), first-run experience, uninstall cleanup.
- Private beta build + feedback loop (no telemetry — manual reports).
- **Exit:** signed installable build dogfooded by target personas.

## 3. Cross-cutting workstreams (run throughout)
- **Testing:** unit for domain/services; interop harness for Win32; optional WinAppDriver UI smoke.
- **Security review:** redaction efficacy, DPAPI key handling, secure delete, no-network proof.
- **Docs:** update HLD decisions log as spikes resolve (SQLCipher vs AES, hotkey default).

## 4. Dependency graph
```
P0 ──► P1 ──► P2 ──► P3
        │       │
        └──► P4 ─┘
P2/P4 ──► P5 ──► P6 ──► P7 ──► P8
```
P1 unblocks everything storage-related; P2 unblocks the recall UX; P4 can start once
P1's pipeline exists; packaging (P8) is last.

## 5. Key technical decisions to lock (decision log)
| # | Decision | When | Default lean |
|---|---|---|---|
| D1 | SQLCipher vs field-level AES | P0 Spike 4 | SQLCipher if packaging OK, else AES |
| D2 | Default global hotkey | P2 | **Resolved:** ordered fallback, `Win+Alt+V` first |
| D3 | EF Core vs raw `Microsoft.Data.Sqlite` | P1 | Raw + hand migrations (lighter) |
| D4 | Restore previous clipboard after paste? | P2 | Setting, default on |
| D5 | Images/files in v1? | P1 | No — text-only v1 (PDD OQ3) |
| D6 | Store vs winget/MSIX sideload | P8 | winget + signed MSIX first |

## 6. Definition of Done (v1)
- All P1–P8 exit criteria met.
- PDD FR1–FR18 implemented (minus v2-tagged image/file items).
- NFR1–NFR7 validated; no-network gate green in CI.
- Signed MSIX installs, autostarts, survives reboot, dogfooded by ≥3 users across personas.

## 7. Risks & mitigations (impl-specific)
- **Native SQLCipher packaging pain** → AES fallback ready (D1).
- **SendInput into elevated apps blocked (UIPI)** → document limitation; detect & message the user.
- **WinUI 3 friction** → interop is UI-agnostic; WPF pivot is contained (HLD §10).
- **Caret placement inconsistency** → cursor-position fallback from day one (P2).
